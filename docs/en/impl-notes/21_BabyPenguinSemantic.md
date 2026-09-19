# 21. BabyPenguin Semantic

BabyPenguin's semantic analysis is a pipeline of 9 numbered passes over **one mutable semantic tree**, mutated in place. There is no separate bound-tree data structure (unlike EmperorPenguin — see [EmperorPenguin Bound](./26_EmperorPenguinBound.md)); nodes gain information as passes advance, and per-node progress is tracked with a `PassIndex`.

## Driver and Model

- **`BabyPenguin/SemanticCompiler.cs`** (64 lines): `AddFile` / `AddSource` / `AddProject(.penguins)` (project loading in `BabyPenguin/PenguinProject.cs`, 237 lines) / `Compile()`.
- **`BabyPenguin/SemanticModel.cs`** (704 lines, `partial class SemanticModel`):
  - Loads `BabyPenguin/Builtin.penguin` (1832 lines: the `__builtin` runtime in Penguin — `Option`, `IFuture`/`IFutureBase`, `Scheduler`, `RoutineContext`, `ICopy`, Event/Channel classes, …) and `BabyPenguin/Utils.penguin` (188 lines) at construction (lines 38–52), overridable via env `PENGUINLANG_BUILTIN` / `PENGUINLANG_UTILS`.
  - **Pass pipeline** (constructor, lines 54–65), in order:
    1. `SemanticScopingPass` (01)
    2. `TypeElaboratePass` (02)
    3. `SymbolElaboratePass` (03)
    4. `ConstructorPass` (04)
    5. `InterfaceImplementationPass` (05)
    6. `SyntaxRewritingPass` (06)
    7. `CodeGenerationPass` (07)
    8. `MainFunctionGenerationPass` (08)
    9. `CheckReturnValuePass` (09)
  - `Compile()` (lines 566–589) runs each pass, then `PortTopologyValidator.Validate(this)` (static port/wire topology audits in `BabyPenguin/PortRegistry.cs`, 183 lines).
  - `CatchUp(node)` (lines 547–564): re-runs passes 0..CurrentPassIndex on newly created nodes — the engine behind on-demand monomorphization (below).
  - Name resolution (lines 170–543): `ResolveSymbol`/`ResolveShortSymbol` (scope-chain walk, imported namespaces, `MutableSymbolProxy` for `mut`-auto propagation, ScopeId-based shadowing via `FindClosestVisibleSymbol`, line 290), `ResolveTypeNode` (builtins → `Self` → fun-types → generic parameters → type aliases → namespace scan → `Specialize`), `ResolveType`. A read-only memoization cache `EnableResolutionCache()` (line 161) is switched on by the VM after the model freezes — resolution was ~76% of bootstrap runtime.
- **`BabyPenguin/ISemanticPass.cs`** (14 lines): `interface ISemanticPass { Model, Report, PassIndex, Process(), Process(ISemanticNode) }`. Per-node progress is tracked with `obj.PassIndex`; each pass skips nodes already at or after its index.

## The Passes (`BabyPenguin/SemanticPass/`)

| # | File | Lines | What it does |
|---|---|---|---|
| 01 | `01_SemanticScoping.cs` | 199 | Creates semantic nodes from syntax (ClassNode/Function/EnumNode/InterfaceNode/InitialRoutine); duplicates → `E_DUPLICATE_SYMBOL`. Desugars class-level initial routines into hidden methods `fun __initial_<name>(mut this)` and `construct` blocks into `__class_construct_<i>(mut this)` / namespace-level `__construct_<i>()` by **re-parsing synthesized source text** through `FunctionDefinition.FromString` (lines 60–70, 103–139; prefixes at lines 11–23). |
| 02 | `02_TypeElaborate.cs` | 39 | Placeholder (marks types processed). |
| 03 | `03_SymbolElaborate.cs` | 342 | `ElaborateTypeReference` (`type X = Y` aliases → `TypeReferenceSymbol`), `ElaborateGlobalSymbol` (namespace globals, class members, enum variants, interface declarations, function symbols), `ElaborateLocalSymbol` (locals/temps of code containers). |
| 04 | `04_Constructor.cs` | 433 | Generates `new` constructors for classes and interfaces, including wiring invocation before initial-routine spawn. The auto-generated class constructor takes only `this` — no field parameters. |
| 05 | `05_InterfaceImplementation.cs` | 604 | VTable construction — `BuiltVTable` (143), `AutoClassifyClass` (246: IValueType vs IReferenceType), `AutoAddICopy` (291), `MergeVTables` (500, transitive interfaces), `FinishVTable` (534), `ValidateInterfaceFieldTypes` (57: rejects non-IRef interface fields/enum payloads without `Box<T>`), `CallInterfaceConstructor` (558). Enforces the orphan-impl rule (lines 122–130). |
| 06 | `06_SyntaxRewriting.cs` | 736 | The desugaring pass: `RegisterVariableNets` (implicit `_Fanout` wire hubs for `connect`), `IdentifyAsyncFunction`, `RewriteLambdaFunction` (closures → synthetic lambda classes: `CreateLambdaClass` line 76, `CollectClosureSymbols` line 99), `RewriteImplicitWait`, `RewriteWaitExpression`, `RewriteAsyncExpression` (async → scheduler jobs), `RewriteGenerator` (yield). |
| 07 | `07_CodeGeneration.cs` | 54 | Calls `container.CompileSyntaxStatements()` on every `ICodeContainer` (skips bodies inside unspecialized generics). |
| 08 | `08_MainFunctionGeneration.cs` | 72 | Synthesizes `__builtin._main`: call each namespace constructor → run top-level `__construct_*` → spawn every initial routine via `SchedulerAddSimpleJob` → call `__builtin._run()`. |
| 09 | `09_CheckReturnValue.cs` | 69 | All-paths-return validation. |

## Node / Symbol / Type Model

- **Semantic nodes** (`BabyPenguin/SemanticNode/`): `Namespace.cs` (99, incl. `MergedNamespace` — multiple files with the same namespace merge), `ClassNode.cs` (109), `EnumNode.cs` (147), `InterfaceNode.cs` (115), `Function.cs` (121, incl. `LambdaFunction`), `InitialRoutine.cs` (49), `BasicTypeNode.cs` (240 — the `BasicTypeNodes` registry of primitives + generic `Fun`/`AsyncFun`).
- **Interfaces** (`BabyPenguin/SemanticInterface/`): `ISemanticScope` (101), `ISymbolContainer` (184), `ITypeContainer` (146), `IRoutineContainer` (22), `ITypeNode` (113), `IVTableContainer` (62), and **`ICodeContainer.cs` (3207 lines)** — the semantic codegen workhorse: `CompileSyntaxStatements()` (line 13), `AddStatement` (238), `AddExpression` (2426), `ResolveExpressionType` (1765), member-access binding (2224), try/catch regions (`SemanticCatchRegion`, line 229), while-loop label stacks, and the RTL port/wire emission helpers (`TryEmitEventWire` 1025, `EmitPortSlotRead` 1316, net hubs 1151–1284). It appends symbol-level IR instructions (see [PenguinLang IR](./22_PenguinLangIR.md)) via `AddInstruction` (3202).
- **Symbols** (`BabyPenguin/Symbol/`): `ISymbol.cs` (39 — flags IsLocal/IsTemp/IsParameter/IsClassMember/IsStatic/IsFunction/IsVariable/IsEnum + Mutability + TypeInferStatus), `VariableSymbol` (63), `FunctionSymbol` (188; its `TypeInfo` is a specialized `Fun<Ret,Args...>`; plus `FunctionVariableSymbol` for function-typed values), `TypeSymbol`/`TypeReferenceSymbol` (56), `EnumSymbol` (46), `MutableSymbolProxy` (55).
- **Types** (`BabyPenguin/Type/`): `IType.cs` (97 — the mutability-carrying type interface and implicit-cast rules), `BasicTypes.cs` (208), `ClassType` (81), `InterfaceType` (75), `EnumType` (75), `TypeStructure.cs` (66).

## Generics / Monomorphization

`ITypeNode.Specialize(List<IType>)` (SemanticInterface/ITypeNode.cs line 39). For classes (`SemanticNode/ClassNode.cs` lines 5–30): a **fresh `ClassNode` is created from the same syntax node**, `GenericArguments` is set, the instance is appended to `GenericInstances`, and **`Model.CatchUp(result)` replays all passes** on the instance. Full names: specialized `Ns.Name<A,B>`, unspecialized `Ns.Name<?>`. All body/codegen passes skip unspecialized generic nodes (`IsGeneric && !IsSpecialized` guards). `ResolveTypeNode` (SemanticModel.cs 478–507) finds an existing instance by argument list or creates one — monomorphization is **demand-driven inside resolution**, not a dedicated pass (EmperorPenguin instead runs a dedicated `MonomorphizePass`).

## VTables

`BabyPenguin/SemanticInterface/IVTableContainer.cs`: the `VTable` class (lines 12–61) is named `vtable-<InterfaceFullName>` and holds `List<VTableSlot> Slots` where `VTableSlot(ISymbol InterfaceSymbol, ISymbol ImplementationSymbol)` (line 10); it also holds `Functions` — interface impl methods are real code containers reachable by IR generation. Primitive types implement `IVTableContainer` through `BasicTypeNode`, which is why `SemanticModel.FindAllIncludingBasicTypeVTables` exists (SemanticModel.cs lines 92–101).

## Structural Contrast with EmperorPenguin

| | BabyPenguin | EmperorPenguin |
|---|---|---|
| Representation | one mutable semantic tree | explicit immutable-ish Bound Tree built by passes |
| Pass structure | 9 numbered passes re-traversing `FindAll(...)` with per-node `PassIndex` | 9 named collaborator classes (`BuildScopesPass` … `ValidateControlFlowPass`) over index-aligned AST/bound pairs |
| Monomorphization | demand-driven in resolution (`CatchUp` replay) | dedicated pass-3 fixpoint |
| Errors | exceptions (`BabyPenguinException`, `BabyPenguin/Common.cs` line 36) | accumulated `SemanticError` lists |
| Metaprogramming | none (only `#template` parsing) | full meta engine (see [EmperorPenguin MetaProgramming](./31_EmperorPenguinMetaProgramming.md)) |
| Rewriting | pass 06 desugars async/lambda/wait/port nets | binder desugaring (wait/spawn) + MetaRewriter prepass |
