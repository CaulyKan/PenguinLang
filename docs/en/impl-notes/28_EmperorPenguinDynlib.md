# 28. EmperorPenguin Dynamic Libraries & libmeta v1 Format

A `.penguin-lib` is a native shared object (ELF `.so` / PE `.dll` / Mach-O `.dylib`)
with a JSON metadata blob appended after the binary and an ASCII footer:

```
<original .so bytes>
<JSON metadata document>
PENGUINLIB:<meta_offset>:<meta_size>\n
```

The metadata (`emperor-libmeta` v1) is a C#-metadata-style **structured symbol
table**, not embedded source. Two consumers exist (see
[Consumption](#consumption)); both are byte-identical in emitted `.ll`.

Reference implementation: `EmperorPenguin/std/penguin/dynlib.penguin`
(writer `serialize_symbols`, readers `read_meta` / `libmeta_direct_inject`).
The ANTLR-safe `DynlibStub.penguin` (pass1 builds) disables all of this.

## Top-level document

```json
{"format":"emperor-libmeta","version":1,
 "name":"libemperorpenguin","deps":["libcore","..."],
 "instances":["<global>.std.Vector$2FibnjNpwi...", "..."],
 "symbols":[ ... ]}
```

| Key         | Meaning                                                                              |
| ----------- | ------------------------------------------------------------------------------------ |
| `format`    | Always `"emperor-libmeta"`.                                                           |
| `version`   | `1`. Readers reject other versions.                                                   |
| `name`      | Library name (also stamped as the `.so` SONAME by `link_lib`).                        |
| `deps`      | Names of libraries this one was built against (`--lib` inputs, transitive closure).   |
| `instances` | Mangled `full_name`s of every generic specialization the .so already contains. The   \
              consumer reuses these instead of re-specializing (declare-not-define).                |
| `symbols`   | Ordered symbol/source entries, see below.                                             |

## Symbol entries (`symbols[]`)

| `kind`     | Fields                                                            | Notes                                                                 |
| ---------- | ----------------------------------------------------------------- | --------------------------------------------------------------------- |
| `ns`       | `name`                                                            | Namespace declaration (grouping only).                                 |
| `class`    | `ns`, `name`, `fields[]`, `methods[]`, `impls[]`                  | Fields: `{n, t, mu}`; methods/impls below.                             |
| `iface`    | `ns`, `name`, `methods[]`, `impls[]`                              | Same shape as class without fields.                                    |
| `enum`     | `ns`, `name`, `members[]`, `methods[]`, `impls[]`                 | Members: `{n, p (payload type or null), v (discriminator)}`.           |
| `fun`      | `ns`, `name`, `params[]`, `ret`                                   | Flags `x` (extern), `p` (pure), `nw` (`new` ctor), `st` (static).      |
| `alias`    | `ns`, `name`, `t`                                                 | `type` reference.                                                      |
| `implfor`  | `ns`, `iface`, `for`, `methods[]`                                 | Top-level `impl X for Y` edge (needed for value-type classification).  |
| `global`   | `ns`, `name`, `t`, `m`, `init`                                    | Global variable with verbatim initializer expression text.             |
| `source`   | `name`, `text`                                                    | VERBATIM source of a template/meta file (see Keep set).                |

Functions (both top-level `fun` and nested methods, which use `n` instead of
`kind/ns/name`):

- `params[]`: `{n, t, m?}` — `m` appears only for the untyped `this`
  parameter (`mut this`); ordinary parameters carry mutability **on the type**
  (`x: mut T` — a binding-level `mut x: T` is not valid syntax).
- `ret`: the return type `<T>`.
- `impls[]` / `implfor`: `{iface: <T>, methods: [...], vt: ["<impl method full_name>"|null, ...]}`
  — `vt` is the publisher's **prebuilt vtable** (pass-6 product, slot-ordered);
  direct injection adopts it verbatim and skips pass 6 for lib defs.

## Type encoding `<T>`

```json
{"b":"<base>", "a":[<arg>...], "m":0|1}
```

- `b` — a primitive registry name (`"i32"`, `"string"`, …) or a class/enum/
  interface full name **without** the `<global>.` prefix
  (`"emperor.BoundType"`, `"_utils.List"`). `display_name()` is NOT used: it
  carries mutability prefixes and cannot round-trip.
- `m` — `1` when the type is mutable, omitted when immutable.
- `a` — generic args, present only when non-empty:
  - type arg: `{"t": <T>}`
  - value arg: `{"v": {"k":"i64|bool|string|double|obj", ...}}` with the value
    in `i` / `b` / `s` / `d` (double as decimal string) / `u` (unique name).
- Function types: `{"k":"fun", "a":[<param T>..., <ret T>], "ay":1?}` (`ay` =
  async), no `b`.
- Field mutability is a separate tri-state `mu`: `2` = `mut`, `1` = `!mut`,
  omitted = auto (the type itself is serialized immutable so the materialized
  spelling never doubles).
- Global `m` mirrors the source: `let mut` + non-mut type warns and drops.

## Keep set (export activation)

A lib's metadata carries:

1. every `export`-marked def (an `export namespace` cascades to all members),
   plus the **closure** of types referenced by retained signatures, fields,
   enum payloads, impl edges and global initializers (a referenced type is
   retained even if not itself exported — consumers need the declaration);
2. every global variable — the consumer must re-define and re-initialize them
   for GOT interposition (the exe's copies interpose the .so's through the GOT);
3. every top-level `impl X for Y` edge — value-type (ICopy/IRef)
   classification must match the publisher's;
4. VERBATIM SOURCE for every lib file containing template/meta constructs
   (`#template` / `#fun` / `#specializing` / `#if` …) — consumers re-bind those
   files to monomorphize NEW generic instances locally (shipped instances from
   `instances[]` are reused declare-not-define; a `$`-mangled specialization
   whose template is NOT lib-sourced is an `E_DUPLICATE_SYMBOL` — except
   `<global>.__builtin.*`, which core_builtin ships on both sides).

Everything else stays private to the .so. `is_exported` is consumed ONLY by
lib-build filtering; normal compiles are unaffected.

Ship-source detection is belt-and-braces: a line-start `#`+letter scan of the
file text (`_lib_text_needs_source` — compared via `string_char_code_at`, the
native `>=` string compare mis-compiles, see
`Tests/StringTest/StringRelationalGeNative.md`) plus `upgrade_template_files`
which forces any file containing generics / value-template params / `#fun`
over the side.

## Consumption

Consumers declare-not-define the table's defs, then call into the `.so` for
method bodies. Two paths, selected by `--libmeta=` (default `direct`):

- **text** (`--libmeta=text`, the debug/parity path) — `read_meta`
  materializes the symbol table as bodyless declaration text
  (`fun ...;` — never `extern fun`, so the extern→libc mapping never fires)
  in a pseudo-file `<libdecls:<libname>>`; `source` entries become per-file
  `SourceInput`s unchanged. The materialized file flows through the ordinary
  pipeline marked `SourceInput.is_lib`, so the `is_lib_export` machinery
  (pass-1 range marking, pass-8 body skip, IR declaration emission,
  `check_lib_redefinition`) applies as-is.
- **direct** (`--libmeta=direct`, default) — `libmeta_direct_inject` runs
  between semantic passes 1 and 2 and builds prebuilt bound defs from the
  JSON, splicing them into the def list at the (now empty) decl-slot position:
  1. **skeletons** — namespace scopes via `add_or_merge_namespace` (scope AND
     symbol registered), class/enum/iface/fun/global shells with
     `is_lib_export`/`is_lib_source` set;
  2. **signature backfill** — types via `resolve_type_json` (dotted base
     names resolved segment-wise; parameters NOT pre-filled — pass 4 mirrors
     them, double-fill is an arity error), return types filled (a missing one
     makes calls bind as void), prebuilt vtables adopted (`vt` slots paired
     with implementation method symbols), `implfor` vtables built by method
     name;
  3. **global initializers** — the `init` expression text is lexed, parsed
     (`Parser.parse_expression`) and bound against the global's scope, AFTER
     all signatures exist (an early bind caches stale e.g. `void` returns).

  Passes 2/6/8 skip the injected range and remap AST indices
  (`SemanticModel.libmeta_is_injected` / `libmeta_ast_index`).

Both modes produce byte-identical `.ll` (`Tests/DynamicLinkTest/LibMetaTextDirectParity.md`).

The exe side carries the C runtime + optional JIT; the lib's
`_emperor_*`/`__builtin.*` references bind from the exe via `-rdynamic`, and
`link_exe` adds `-rpath,$ORIGIN` so an exe + `.penguin-lib` pair is relocatable.

## Building a lib

`-o X.penguin-lib` triggers lib mode: the emitter writes `X.ll`, `link-lib`
builds the `.so`, `serialize_symbols` appends the metadata + footer. Building
a compiler lib requires a JIT-capable build (`-enable-meta`). Lib deps resolve
recursively (`deps[]`, cycle-safe via `visited`).
