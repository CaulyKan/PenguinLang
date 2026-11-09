# 33. EmperorPenguin Incremental

## Overview

Three cooperating pieces give didChange-driven consumers (the LSP, the `--incremental` CLI mode) a compile that reuses work across edits:

- **`GlobalIndex`** (`EmperorPenguin/src/bound/GlobalIndex.penguin`) — the global merge-layer reverse index over one finished compilation, rebuilt at the end of every `SemanticModel.bind()`.
- **`IncrementalSession`** (`EmperorPenguin/src/bound/IncrementalSession.penguin`) — the session envelope around `EmperorPenguinCompiler.compile_sources`: per-file snapshots, the epoch counter, and the two-lane didChange transaction.
- **`LspCompileActor`** (`MagellanicPenguin/LspServer/LspCompileActor.penguin`) — the LSP's single compile actor that serializes every incremental transaction and navigation query (see below).

The full-compile path is untouched: the bootstrap and the plain CLI keep calling `compile_sources` directly. The session only owns the cross-transaction state a didChange consumer needs.

## The GlobalIndex

`GlobalIndex.rebuild(model, result)` runs as the last step of `bind()`. It builds five tables plus the file list and the per-file/global fingerprints. All tables are `StrMap<string>` whose values are compact `a|b|c` encodings (identifiers cannot contain `|`, so the encoding is injective for names):

| Table | Key | Value |
|---|---|---|
| `symbols` | symbol full_name | `kind\|owner_file` — kind: `var`\|`fun`\|`type`\|`enum_member`\|`namespace` |
| `types` | type full_name | `kind\|owner_file\|specialized\|name` — kind: `primitive`\|`class`\|`enum`\|`interface` |
| `instances` | specialization full_name | `kind\|template_file\|def_name\|site` — kind: `class`\|`enum`\|`interface`\|`fun`. `template_file` is the TEMPLATE's file (the specialization-attribution rule: an instance belongs to the file that owns the template). `site` is the B1 request-site record from `MonomorphizePass.b1_adjudicated` — which request path adjudicated the instance (`fixpoint-type`, `fixpoint-func`, `call-*`, `new`, `member-*`, `coroutine-ensure`, `generator-ensure`, `intrinsic`, `meta-type`, `resolve-cascade`, `nested-sig`, `late-complete`, `sig-enum`); libmeta pre-specialized instances that never pass through B1 carry `""` |
| `ifaces` | `"iface \|> for_type"` | `owner_file\|origin` — origin: `body`\|`impl_for`\|`iface`\|`primitive`. Plus flat row lists in append order (the map is the dedup/lookup view, the rows are the ordered view the fingerprint consumes). Edges living only on a SPECIALIZATION (injected by `#specializing` / box forwarding) are registered with the template file as owner |
| `users` | target full_name | the list of definitions referencing it (the reverse-lookup side of invalidation) |

The **fingerprints** are stable hashes (djb2-style) folded over each file's exported surface: exports ∪ impl edges ∪ owned instances, with `"len:part"`-prefixed concatenation so the fold is unambiguous. The per-file fingerprint is the fast-lane adjudication key (below); the global fingerprint summarizes the whole compilation.

The **`TypeInterner`** (`SemanticTypeInterner.penguin`) is fed during the rebuild: it interns types by a canonical string key that folds the template+args representation and the specialized-def representation of the same instantiation into one slot — exactly the pairs `is_same_type` unifies (mutability is not part of identity; class and enum fold, the two interface representations stay distinct). It is a pure query/statistics layer today; no resolve path consults it yet.

## The IncrementalSession

### FileSnapshot

One frozen per-file view of a finished compilation:

- `text_hash` — the stable hash of the file's text;
- the **AST-signature fingerprint** — the fast-lane adjudication key: a hash over the file's symbolic surface (the definitions' signatures), insensitive to function-body changes;
- the GlobalIndex per-file view: exports, impl edges, owned instances, referenced targets, errors;
- the def-segment range into `result.definitions` (which slice of the merged definition list belongs to this file).

Snapshots are built once at the end of a compile and never mutated afterwards (**freeze discipline**: "publish-then-readonly"). The Bound\* object tree stays in `last_result`, owned by the session and replaced wholesale at each slow-lane boundary (or surgically updated in place on the fast lane); a snapshot holds only copied view data — plain lists of names — so no snapshot field aliases mutable model state. The one deliberate exception: `errors_kept` holds read-only SemanticError object references.

### The didChange transaction: short-circuit, fast lane, slow lane

A didChange (one or more files' new texts) is adjudicated in three tiers:

1. **Short-circuit** — every changed file's text hash still matches the published snapshot (and the CLI defines fingerprint held): the compilation cannot have changed; return `last_result` as-is.
2. **FAST lane** — every changed file's AST-signature fingerprint is UNCHANGED (bodies differ, symbolic surface identical) and the session is fast-eligible globally (no meta surface on the changed files, no RTL surface anywhere, no suspension, no mt scheduler). The session REPLAYS passes 3-9 on the RETAINED model + result tree:
   - the changed files' AST defs are swapped in place;
   - their bound defs' processing stamps are rolled back so passes 4/5 short-circuit (signatures identical) and passes 8/9 rebind only the changed bodies;
   - dead specializations are evicted by reconciling against the B1 request ledger;
   - per-file synthetic-name counters are reset;
   - errors and `users` entries are REPLAYED from the snapshots, so the merged output is byte-identical to a fresh full compile.
3. **SLOW lane** — anything else (a structural change, meta surface, first compile): the unchanged files' cached ASTs are reused (the parse skip), then a full fresh pipeline runs on a new model. The output is identical to a plain full compile by construction.

The **epoch counter** implements the B-boundary switch: readers hold an epoch reference, a committing transaction atomically installs epoch+1 with its new result.

The **CLI defines fingerprint** (`--define K=V` set, hashed) is invalidation key: a changed defines set invalidates every file's view, forcing the slow lane.

### CLI flags

- `--incremental` — route the compilation through an `IncrementalSession` (per-file snapshots + parse cache). Behaviorally identical to the plain full compile; not supported together with `--meta-src` (the plain full compile runs instead, with a notice on stderr).
- `--edit=<file>=<replacement>` — with `--incremental`: after the first compile, replace `<file>`'s text with `<replacement>`'s content and re-compile the session. Repeatable; edits apply in order. This is how the test suite drives two-compile transactions from one invocation.

## LSP Integration: LspCompileActor

The LSP's compile topology is a SINGLE actor. `LspMain`'s document router forwards traffic to `LspCompileActor` as `LspCompileMsg` values through one FIFO:

- **OPEN / EDIT messages are coalesced**: the actor drains the FIFO backlog non-blockingly and folds a consecutive non-query segment into one transaction per affected session. Same-uri writes keep only the latest text — the editor's current text always wins, stale intermediates are never compiled.
- **Queries are never dropped nor merged**: a navigation query is answered only after every edit that arrived before it has been compiled (strict interleave order).
- **CLOSE** retires a document: single-file sessions are dropped entirely; project-mode sessions stay (the file set is stable) and the file's input falls back to its on-disk text.

The `emperor.IncrementalSession` objects live only inside the actor, one per routing key (project plan identity, or the document path in single-file mode) — one serialized loop also serializes the lib-metadata reads the compile performs.

**Publication protocol** (workspace diagnostics): every OPEN document of a touched session receives the per-file filtered diagnostics of the fresh compile; a project file that is NOT open but previously received non-empty diagnostics receives an empty set once its errors are gone, so stale client-side squiggles on unopened files disappear.

**Panic guard**: each transaction is sjlj-guarded. If the compiler raises a runtime error, the crashing session is marked POISONED — its next transaction discards the object and rebuilds from a full compile; diagnostics are still published for the triggering document and the server survives (the last-ok result keeps serving queries).

## Tests

- `Tests/IncrementalTest/` (10 cases, Apply To Pass1/2/3) — two-lane sentinels driven by `--incremental --edit=...`: adding a function (slow lane), editing a body (fast lane), cross-file cascades, generic-instance swaps, lambda drops, dead-specialization stale errors, meta-class surfaces, specialization rebuilds, verification rounds.
- `Tests/LspTest/IncrementalDidChangeDiagnostic.md` — the LSP answers a didChange through the session and publishes the broken document's diagnostics (byte-parity frames).
- `Tests/LspTest/LargeFrameDeliveredWhole.md` / `FdWakeWithSecondWaiterNotStranded.md` — stdio robustness sentinels: a >64KB response frame arrives whole, and an fd-woken coroutine runs even while another fd waiter stays parked.
