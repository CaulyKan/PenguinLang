# 24. BabyPenguin CS Backend

The C# backend (`--backend=cs`) lowers the shared IR (see [PenguinLang IR](./22_PenguinLangIR.md)) to C# source, compiles it with `dotnet build`, and runs the result — either in-process or as a standalone executable. It exists for two reasons: it is the first native stage of the bootstrap (pass1: BabyPenguin's C# backend compiles EmperorPenguin's sources straight to LLVM-IR-emitting machine code), and it serves as a divergence finder against the interpreter oracle in the cross-compiler test suite (`BabyPenguin CS` rows in `Tests/*.md`).

All under `BabyPenguin/CSharpBackend/`.

## Entry Points and Modes

CLI (`BabyPenguin/Program.cs`): `--backend vm|cs` (default vm, line 23), `--keep-cs`, `--run-only <dll>`, `--cs-out <path>` (lines 26–33); dispatch at lines 114–117.

**`CSharpBackendRunner.cs`** (85 lines) — three modes:

1. **In-process** (default): lower → compile → load → reflectively invoke `BabyPenguinCompiled.Generated.__builtin__main` against a shared `RuntimeGlobal` wired through `GlobalState`, so I/O and exit codes are byte-identical to the interpreter.
2. **`--cs-out <path>`**: build a standalone exe (plus copied BabyPenguin runtime dependencies).
3. **`--run-only <dll>`**: re-run a previously compiled assembly (cache hit path).

**`DiskCompiler.cs`** (242 lines): writes a temp `.csproj` (`OutputType Library|Exe`, referencing `BabyPenguin.dll` by HintPath) → `dotnet build -c Release` → SHA256 cache (source hash + BabyPenguin.dll timestamp) under `/tmp/bp_cs_cache` → loads into a **collectible `AssemblyLoadContext``.

## Lowering Pipeline

**`CSharpBackend.cs`** (459 lines) — `CSharpBackend.Lower(model, standalone)`:

1. Regenerates the `IRModule` with `IRGenerator` (dumped to `/tmp/bp_ir.txt` when `BP_DUMP_IR` is set).
2. Computes **reachability from entry points**: namespace constructors extracted from `__builtin__main`'s prologue (`ExtractNamespaceConstructors`, line 311), `IRModule.EntryFunctions` (initial routines), plus **all vtable implementation slots** (classes, enums, and basic-type vtables — lines 58–98, because funptr dispatch is invisible to call-graph walking). BFS over `CalleesOf` (line 378: direct calls, method-ref RDMBRs, funptr constants).
3. Collects called externs with call-site signatures (`ExternCallsOf`, line 325, tracking funptr provenance) and IR type roots.
4. Emits one `Generated.cs`: type declarations (`TypeLowerer`), `public static class G { ... }` globals, `public static class Generated` with lowered functions, generated externs (`ExternLowerer`), `__InitVtables()` (reflection-based vtable registration — `GlobalState.RegisterVtable(typeof(T), "iface.method", MethodInfo)`, lines 194–266), and a **fast-path `__builtin__main`** that runs namespace init then initial routines synchronously — bypassing the coroutine scheduler, which is correct for programs with no wait/emit/yield (e.g. the compiler itself; class docstring lines 19–24). Optional standalone `Program.Main` wiring `RuntimeGlobal` + `ProgramExitException` handling (lines 281–299).

### FunctionLowerer

**`FunctionLowerer.cs`** (405 lines): one `IRFunction` → one `public static` C# method with locals `r_<registerIndex>`; `LowerInstruction` (line 128) maps:

- CONST → assignment (funptr constants recorded as provenance); ASSIGN (value-copy unless `IsAliasChain`); CAST (`CastExpr`, line 364: to-string via ToString/`__ToName`/bool-ternary; interface → concrete downcast).
- BINOP (string relationals via `string.CompareOrdinal`, lines 171–176); UNARYOP; RDMBR (field read; funptr reads become `MethodRef` or `VirtualMethodRef` records); WRMBR (field write, payload copy).
- BR/BR_COND → `goto`; RET/RET_VOID; CALL/CALL_VOID → direct static calls.
- `CALL_FUNC_PTR` → `EmitFuncPtrCall` (line 282): MethodRef → direct static call with receiver prepended; VirtualMethodRef → `GlobalState.InvokeVirtual` reflection dispatch (with the `ICopy<T>.copy`-on-primitive special case, lines 309–319); FuncRef → named call.
- NEW → `new T()`; NEW_ENUM → `_value`/`_containing_value` with struct-payload copy; ISENUM; RDENUM; GLOBAL_LOAD/GLOBAL_STORE → `G.<name>`; ISINSTANCE → `obj is IHasMeta m && m.__meta.Is("TypeId")`; anything else → `NotImplementedException`.

### TypeLowerer / ExternLowerer / CSharpEmitter

- **`TypeLowerer.cs`** (136 lines): classes → `public sealed class X : IHasMeta[, IValueSemantics]` with a `__meta` (`Meta` + `InterfaceMapEntry[]`); enums → a struct with meta/tag/payload; emits the transitive closure of specialized types (walks `GenericInstances`).
- **`ExternLowerer.cs`** (192 lines): static table of scalar extern bodies (print/exit/string_*/file_*/exec_cmd/args/shifts, lines 27–62) routed through `GlobalState.Global`; collection externs (List/Queue/StringBuilder/AtomicI64) via `__impl.__backing`; unknown externs get arity-correct stubs so the assembly compiles.
- **`CSharpEmitter.cs`** (152 lines): the IR-type → C#-type table (i8→sbyte … f64→double; `ref<X>`/`struct<X>`/`enum<X>` unwrapping; interfaces → `object`), `Normalize` (strips `!mut`), mangled identifiers, operand rendering (`r_N`, `G.x`, `L_x`), literal renderers, binop/unop maps.
- **`NameMangler.cs`** (47), **`RuntimeSupport.cs`** (168, namespace `BabyPenguin.CSharpBackend.Runtime`): `GlobalState` (shared `RuntimeGlobal`/args, `Clone`, `CopyValueSemantics`, vtable dictionary + `InvokeVirtual`, `ExecCmd`), `IHasMeta`, `IValueSemantics`, `Meta`/`InterfaceMapEntry` (the slim C# equivalent of EmperorPenguin's metaptr), `RoutineYield` (status mirrors ReturnStatus).

## Role in the Bootstrap

The Makefile's pass1 stage compiles `EmperorPenguin/EmperorPenguinPass1.penguins` with `BabyPenguin --backend=cs` (Makefile lines ~202–213, ~350–356) — the emitted C# is built into a native-capable emitter that then produces pass2's `.ll`. The fast-path main (no scheduler) is valid there because the compiler sources contain no suspension points.

## Testing

- `BabyPenguin.Tests/CSharpBackendBenchTest.cs` — `FibCompiledVsInterpreter` lowers fib(28), compiles via `DiskCompiler`, times compiled vs interpreter, and asserts a ≥5× speedup (line 91).
- The cross-compiler suite runs `dotnet BabyPenguin.dll -q --backend=cs <src>` as the `BabyPenguin CS` compiler kind (`Tests/Program.cs` line ~1480) — same byte-exact expectations as the interpreter, used to find divergences.
