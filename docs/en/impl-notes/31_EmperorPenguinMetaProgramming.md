# 31. EmperorPenguin MetaProgramming

EmperorPenguin's metaprogramming (see the [Meta Programming spec](../specifications/11_MetaProgramming.md)) is implemented as a compiler-inside-the-compiler: a "unit B" that is compiled and JIT-executed during the compilation of the user program ("unit A"). Language-level rules are in the spec; this page documents where each piece lives and how values cross the boundary.

## The MetaEngine

**`EmperorPenguin/src/meta/MetaEngine.penguin`** (1916 lines), `class MetaEngine` (line 13). State (lines 14–90): the LLVM ORC JIT session handle (`jit_ctx`), compiled-function and caller-stub caches (`compiled_names`/`compiled_ptrs`, `stub_keys`/`stub_ptrs`), the file-scoped define-view key `option_view_file` (the engine keeps NO store of its own — `has_option`/`get_option`/`set_option` bridge to the owning SemanticModel's unified store under the #fun's DEFINING file, so a `#fun` sees its own file's defines plus the CLI seeds, never another file's), the type-token registry (`type_token_keys`/`values`, `next_type_token`), string/double token registries, AST token tables, `specializing_active_slots`, and a re-entrancy guard (`compiling_meta_names`).

- `init()` (line 104): lazily calls `__builtin.penguin_jit_create()`; failure throws a catchable runtime error (never `exit(1)`) so the LSP survives broken documents.
- `seed_meta_engine` (`src/bound/SemanticMetaRewrite.penguin` line 1225): one-shot wiring at prepass start — verbose flag, `--meta-src` inputs, `type_registry`, `global_scope`, the full `#fun` set, `#class` sources, then `penguin_meta_set_active(...)` + `owner_model`.

### JIT execution of #fun

The JIT is LLVM ORC behind a small C API: `std/c/penguin_jit.h` (`_emperor_penguin_jit_create/_add_module/_lookup/_destroy` + fixed-arity trampolines `call_i64_0`, `i64_i64`, ...). It is linked only into binaries built with `-enable-meta` (`src/project/CompilerConfig.penguin` lines 39–41, 180–181) — the same flag the Makefile uses for pass2+ binaries and the LSP.

`compile_meta_function(bmf)` (MetaEngine.penguin line 390) — the unit-B pipeline:

1. Synthesize a plain `FunctionDefinition` from the `BoundMetaFunctionDefinition` (parameter-kind mapping: `type` → `emperor.BoundType`, `ast` → `i64`, `unstructured_ast` → `string`, value types verbatim, untyped `object` → `i64`; return mapping right after).
2. Build unit-B source text (lines 483–523): `using emperor;` + the synthesized `#fun` (+ every other `#fun`, so `#fun`→`#fun` plain-name calls resolve; optional bare-name forwarder under `--meta-src`).
3. Compile unit B with a nested `EmperorPenguinCompiler` (`top_level_in_global = true`, `is_unit_b = true`, lines 566–567) over `base_meta_sources()` (line 192): `core_builtin.penguin`, `utils.penguin`, `meta_runtime.penguin`, the whole bound layer (BoundType/BoundDefinition/BoundSymbol/BoundScope/...), the AST layer, `ErrorCode.penguin` — self-contained. Weak-dedup in the JIT means the re-emitted definitions resolve to the host's strong exports (`emperor_BoundType_fields`, …) — this is what makes reflection real-pointer reuse. User `--meta-src` files are appended verbatim (lines 524–540).
4. `IRGenerator` + `LLVMEmitter` → LLVM IR text (lines 583–598; dumped to `/tmp/unit_b.ll` at verbose ≥ 2), then `penguin_jit_add_module` + `penguin_jit_lookup` (600–612).

Calls go through `call_meta_function_stub` (line 783): a lazily compiled **nullary caller-stub** `__stub_<sanitized>_<idx>` whose LLVM IR bakes the arguments as constants — `type` args materialized via `declare ptr @emperor_penguin_meta_get_type(i64)`, strings/doubles via their token getters, object args as 1-based HANDLES resolved back to live references via `penguin_meta_get_object` (a registry lookup in MetaHost — no raw addresses), bool as i8, rest as i64. Return marshalling: i64 direct; bool zext; double/type/string stored to module globals `@emperor_active_double_result` / `@emperor_active_type_result` / `@emperor_active_string_result` with `ret i64 0`; object returns are registered into the handle registry from INSIDE the stub (`call i64 @emperor_penguin_meta_register_object(ptr %r)`), so the reference never becomes an integer on the host side and the GC-tracked registry (`List<IReferenceType>`) keeps it alive — no pinning.

## MetaHost — the Host Responders

**`src/meta/MetaHost.penguin`** (468 lines, namespace `emperor`): module-global `active_meta: mut MetaEngine` (line 19) + `active_model`; the extern surface unit B sees (`emperor.penguin_meta_*`, declared in `meta_extern_decls()`, MetaEngine.penguin lines 130–170):

- `penguin_meta_get_type(token)` (line 123) — token → live BoundType; the single remaining reflection bridge.
- `penguin_meta_create_expression/_definition/_parse_arguments` (153/164/225) — run the real Lexer+Parser at meta-runtime on a code string; register nodes as AST tokens. Definition parsing accepts class-member shapes and multi-definition groups.
- `penguin_meta_error/warn/info` (295–307) — compile-time diagnostics routed to the owning SemanticModel (`E_META`, synthetic `<meta>` location).
- `penguin_meta_specialize(name)` (278) — sets the `pending_specialize_name/args` side channel consumed by `active_model.ensure_specialized_type(...)`.
- `penguin_meta_activate_impl(slot)` (310) — pushes into `specializing_active_slots` for `#specializing` impl injection.
- `penguin_meta_get_current_scope()` (458) — the enclosing class during class-member rewrites.

**`src/meta/meta_runtime.penguin`** (71 lines): `interface ICompiler` + `class CompilerContext` — compiled only into unit B; every method is a one-line forwarder to a `penguin_meta_*` responder. This is what `#compiler()` returns.

## Where Each Directive Is Processed

The lexer only produces a `Hash` token (Lexer.penguin line 684) — there are no lexer-level directives.

| Construct | Processed at | Code |
|---|---|---|
| `#template(...)` | parser (skips the template parens, re-parses as a normal def) | Parser.penguin ~2252–2290 |
| `#fun` / `#class` / `#if` / `#for` / `#while` / `#specializing` / def-position `#call` | parser → AST `Meta*` nodes | Parser.penguin ~1130–1800 |
| `#define` / `#if` / `#elif` / `#else` / `#while` (def + statement level) | MetaRewriter prepass — hardcoded folding against the FILE-scoped define store (a code `#define` is visible from its line to the end of its file; CLI `--define` seeds every file); conditions fold from literals, `#defined("K")` and bare define-name comparisons (`eval_meta_string` 272, `eval_meta_bool` 287); same-file redefinition is `E_DUPLICATE_SYMBOL`; `#while` unroll cap 10000 | SemanticMetaRewrite.penguin (1333 lines; def-level `rewrite_meta_definitions` 569, statement-level `rewrite_meta_statements_inplace` 888) |
| `#fun` registration, `#class` placeholders, `#specializing` blocks | Pass 1 (build_scopes) | SemanticBuildScopes.penguin ~316–474 |
| meta calls in **type positions** | Pass 2 (resolve_types) — `try_resolve_meta_type_specifier` | SemanticResolveTypes.penguin 924 |
| `#specializing` execution per instantiation | Pass 3 (monomorphize) — `run_specializing_for_spec` / gates JIT-called; activated impl slots read back and injected | SemanticMonomorphizeSpecializing.penguin 28 |
| `#fun` call splicing, `#sizeof`, template-instantiation routing | Pass 8c (BindMetaCallsPass) | SemanticBindMetaCalls.penguin |
| `#defined`/`#define`/`#typeof`/`#compiler()`/`#error` inside `#fun` bodies | MetaEngine's unit-B rewrite `rewrite_meta_calls_in_expr` — `#defined(K)` folds to a BoolLiteral against the #fun's defining-file define view, `#define(K,V)` writes it; `#typeof`/`#compiler`/unknown passthrough (shadowable by user `#fun`s) | MetaEngine.penguin 1441–1750 |

`#for` is parsed (`MetaForDefinition`) but the prepass only splices `#if`/`#while`/`#define`; collection iteration is not implemented. `MetaRewriter.run_prepass` (SemanticMetaRewrite.penguin line 168) runs before the 9 passes: collect `#fun` → collect `#class` → seed the engine → def-level rewrite (in place, keeping definition indices aligned) → statement-level rewrite.

## Reflection — Real-Pointer Reuse

Unit B compiles the compiler's real `emperor.BoundType`, `BoundClassFieldDefinition`, `BoundFunctionDefinition`, `BoundEnumMemberDefinition` classes; weak-dedup resolves them to the host's strong exports. `t.fields()`/`t.methods()`/`t.variants()`/`t.is_class()`/`t.display_name()` inside a `#fun` are direct method calls on live objects — there is no per-op responder protocol. Type tokens are interned by name (`get_or_assign_type_token`, MetaEngine.penguin 1096); `#typeof` comparisons are pointer-equality on the interned pointers. Resolution for computed names: `resolve_type_by_name` (1113) → host registry → `global_scope.lookup_type_anywhere` → **AST fallback** `resolve_type_from_ast` (1152) which builds partial BoundTypes from unbound AST class definitions so reflection works at def-splice time (before Pass 1) — the mechanism behind `json.penguin`'s `#impl_json_serializable`.

## What Runs Where

Two build variants of the same compiler sources decide availability (`meta_runtime_available()`):

* `EmperorPenguinPass1.penguins` includes `src/meta/MetaConfigStub.penguin` → returns false; pass1 and BabyPenguin-compiled builds parse `#template` but never JIT (calling a `penguin_jit_*` builtin there throws).
* `EmperorPenguinPass2.penguins` (bootstrap pass2/pass3, the release compiler, the LSP) swaps in `std/penguin/metaconfig.penguin` → returns true; built with `-enable-meta`, so `libpenguin_jit` is linked and every JIT path works.

`#define`/`#if`/`#while` folding is hardcoded and works in any EmperorPenguin build (BabyPenguin splices the same constructs with its own C#-side `MetaSplicer`). The meta test suite is `Tests/MetaProgramming/` (116 cases, Apply To Pass2/Pass3; the `#define`/`#if` cases also run on BabyPenguin).
