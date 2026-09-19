# 31. EmperorPenguin MetaProgramming

EmperorPenguin's metaprogramming (see the [Meta Programming spec](../specifications/11_MetaProgramming.md)) is implemented as a compiler-inside-the-compiler: a "unit B" that is compiled and JIT-executed during the compilation of the user program ("unit A"). Language-level rules are in the spec; this page documents where each piece lives and how values cross the boundary.

## The MetaEngine

**`EmperorPenguin/src/meta/MetaEngine.penguin`** (1814 lines), `class MetaEngine` (line 13). State (lines 14–78): the LLVM ORC JIT session handle (`jit_ctx`), compiled-function and caller-stub caches (`compiled_names`/`compiled_ptrs`, `stub_keys`/`stub_ptrs`), the option store, the type-token registry (`type_token_keys`/`values`, `next_type_token`), string/double token registries, AST token tables, `specializing_active_slots`, and a re-entrancy guard (`compiling_meta_names`).

- `init()` (line 89): lazily calls `__builtin.penguin_jit_create()`; failure throws a catchable runtime error (never `exit(1)`) so the LSP survives broken documents.
- `seed_meta_engine` (`src/bound/SemanticMetaRewrite.penguin` lines 818–855): one-shot wiring at prepass start — verbose flag, `--meta-src` inputs, `type_registry`, `global_scope`, the full `#fun` set, `#class` sources, then `penguin_meta_set_active(...)` + `owner_model`.

### JIT execution of #fun

The JIT is LLVM ORC behind a small C API: `std/c/penguin_jit.h` (`_emperor_penguin_jit_create/_add_module/_lookup/_destroy` + fixed-arity trampolines `call_i64_0`, `i64_i64`, ...). It is linked only into binaries built with `-enable-meta` (`src/project/CompilerConfig.penguin` lines 37–39, 113–114) — the same flag the Makefile uses for pass2+ binaries and the LSP.

`compile_meta_function(bmf)` (MetaEngine.penguin line 373) — the unit-B pipeline:

1. Synthesize a plain `FunctionDefinition` from the `BoundMetaFunctionDefinition` (parameter-kind mapping at lines 380–422: `type` → `emperor.BoundType`, `ast` → `i64`, `unstructured_ast` → `string`, value types verbatim, untyped `object` → `i64`; return mapping at 428–450).
2. Build unit-B source text (lines 457–491): `using emperor;` + the synthesized `#fun` (+ every other `#fun`, so `#fun`→`#fun` plain-name calls resolve; optional bare-name forwarder under `--meta-src`).
3. Compile unit B with a nested `EmperorPenguinCompiler` (`top_level_in_global = true`, `is_unit_b = true`, lines 534–542) over `base_meta_sources()` (line 172): `core_builtin.penguin`, `utils.penguin`, `meta_runtime.penguin`, the whole bound layer (BoundType/BoundDefinition/BoundSymbol/BoundScope/...), the AST layer, `ErrorCode.penguin` — self-contained (~4271 lines). Weak-dedup in the JIT means the re-emitted definitions resolve to the host's strong exports (`emperor_BoundType_fields`, …) — this is what makes reflection real-pointer reuse. User `--meta-src` files are appended verbatim (lines 500–514).
4. `IRGenerator` + `LLVMEmitter` → LLVM IR text (lines 556–568; dumped to `/tmp/unit_b.ll` at verbose ≥ 2), then `penguin_jit_add_module` + `penguin_jit_lookup` (571–585).

Calls go through `call_meta_function_stub` (line 732): a lazily compiled **nullary caller-stub** `__stub_<sanitized>_<idx>` whose LLVM IR bakes the arguments as constants — `type` args materialized via `declare ptr @emperor_penguin_meta_get_type(i64)`, strings/doubles via their token getters, reference args via `get_object` (+ `gc_pin_object`), bool as i8, rest as i64 (lines 788–860). Return marshalling (861–920): i64 direct; bool zext; double/type/string stored to module globals `@emperor_active_double_result` / `@emperor_active_type_result` / `@emperor_active_string_result` with `ret i64 0`; reference returns ptrtoint.

## MetaHost — the Host Responders

**`src/meta/MetaHost.penguin`** (366 lines, namespace `emperor`): module-global `active_meta: mut MetaEngine` (line 19) + `active_model`; the extern surface unit B sees (`emperor.penguin_meta_*`, declared in `meta_extern_decls()`, MetaEngine.penguin lines 115–155):

- `penguin_meta_get_type(token)` (line 31) — token → live BoundType; the single remaining reflection bridge.
- `penguin_meta_create_expression/_definition/_parse_arguments` (53/64/125) — run the real Lexer+Parser at meta-runtime on a code string; register nodes as AST tokens. Definition parsing accepts class-member shapes and multi-definition groups.
- `penguin_meta_error/warn/info` (189–205) — compile-time diagnostics routed to the owning SemanticModel (`E_META`, synthetic `<meta>` location).
- `penguin_meta_specialize(name)` (178) — sets the `pending_specialize_name/args` side channel consumed by `active_model.ensure_specialized_type(...)`.
- `penguin_meta_activate_impl(slot)` (210) — pushes into `specializing_active_slots` for `#specializing` impl injection.
- `penguin_meta_get_current_scope()` (356) — the enclosing class during class-member rewrites.

**`src/meta/meta_runtime.penguin`** (68 lines): `interface ICompiler` + `class CompilerContext` — compiled only into unit B; every method is a one-line forwarder to a `penguin_meta_*` responder. This is what `#compiler()` returns.

## Where Each Directive Is Processed

The lexer only produces a `Hash` token (Lexer.penguin line 678) — there are no lexer-level directives.

| Construct | Processed at | Code |
|---|---|---|
| `#template(...)` | parser (skips the template parens, re-parses as a normal def) | Parser.penguin ~2200–2232 |
| `#fun` / `#class` / `#if` / `#for` / `#while` / `#specializing` / def-position `#call` | parser → AST `Meta*` nodes | Parser.penguin 2200–2600 |
| `#define` / `#if` / `#elif` / `#else` / `#while` (def + statement level) | MetaRewriter prepass — hardcoded folding; conditions fold from literals/`#defined`/`#option` comparisons (`eval_meta_bool` 172, `eval_meta_string` 147); `#while` unroll cap 10000 | SemanticMetaRewrite.penguin 260–704 |
| `#fun` registration, `#class` placeholders, `#specializing` blocks | Pass 1 (build_scopes) | SemanticBuildScopes.penguin ~405–474 |
| meta calls in **type positions** | Pass 2 (resolve_types) — `try_resolve_meta_type_specifier` | SemanticResolveTypes.penguin 856 |
| `#specializing` execution per instantiation | Pass 3 (monomorphize) — `run_specializing_for_spec` / gates JIT-called; activated impl slots read back and injected | SemanticMonomorphize.penguin 450–555 |
| `#fun` call splicing, `#sizeof`, `#__address_of`/`#__load`/`#__store`, template-instantiation routing | Pass 8c (BindMetaCallsPass) | SemanticBindMetaCalls.penguin |
| `#defined`/`#option`/`#define`/`#typeof`/`#compiler()`/`#error` inside `#fun` bodies | MetaEngine dispatch (shadowable by user `#fun`s) | MetaEngine.penguin 1541–1647 |

`#for` is parsed (`MetaForDefinition`) but the prepass only splices `#if`/`#while`/`#define`; collection iteration is not implemented. `MetaRewriter.run_prepass` (SemanticMetaRewrite.penguin lines 34–49) runs before the 9 passes: collect `#fun` → collect `#class` → seed the engine → def-level rewrite (in place, keeping definition indices aligned) → statement-level rewrite.

## Reflection — Real-Pointer Reuse

Unit B compiles the compiler's real `emperor.BoundType`, `BoundClassFieldDefinition`, `BoundFunctionDefinition`, `BoundEnumMemberDefinition` classes; weak-dedup resolves them to the host's strong exports. `t.fields()`/`t.methods()`/`t.variants()`/`t.is_class()`/`t.display_name()` inside a `#fun` are direct method calls on live objects — there is no per-op responder protocol. Type tokens are interned by name (`get_or_assign_type_token`, MetaEngine.penguin 1044); `#typeof` comparisons are pointer-equality on the interned pointers. Resolution for computed names: `resolve_type_by_name` (1061) → host registry → `global_scope.lookup_type_anywhere` → **AST fallback** `resolve_type_from_ast` (1100) which builds partial BoundTypes from unbound AST class definitions so reflection works at def-splice time (before Pass 1) — the mechanism behind `json.penguin`'s `#impl_json_serializable`.

## What Runs Where

Two build variants of the same compiler sources decide availability (`meta_runtime_available()`):

* `EmperorPenguinPass1.penguins` includes `src/meta/MetaConfigStub.penguin` → returns false; pass1 and BabyPenguin-compiled builds parse `#template` but never JIT (calling a `penguin_jit_*` builtin there throws).
* `EmperorPenguinPass2.penguins` (bootstrap pass2/pass3, the release compiler, the LSP) swaps in `std/penguin/metaconfig.penguin` → returns true; built with `-enable-meta`, so `libpenguin_jit` is linked and every JIT path works.

`#define`/`#if`/`#while` folding is hardcoded and works in any EmperorPenguin build. The meta test suite is `Tests/MetaProgramming/` (100+ cases, Apply To Pass2/Pass3).
