# 21. BabyPenguin Semantic

BabyPenguin 的语义分析是**一棵可变语义树**上的 9 个编号遍（pass）流水线，就地修改。没有独立的 bound 树数据结构（不同于 EmperorPenguin——见 [EmperorPenguin Bound](./26_EmperorPenguinBound.md)）；节点随遍推进获得信息，逐节点进度用 `PassIndex` 跟踪。

## 驱动器与模型

- **`BabyPenguin/SemanticCompiler.cs`**（64 行）：`AddFile` / `AddSource` / `AddProject(.penguins)`（工程加载在 `BabyPenguin/PenguinProject.cs`，237 行）/ `Compile()`。
- **`BabyPenguin/SemanticModel.cs`**（704 行，`partial class SemanticModel`）：
  - 构造时加载 `BabyPenguin/Builtin.penguin`（1832 行：Penguin 写的 `__builtin` 运行时——`Option`、`IFuture`/`IFutureBase`、`Scheduler`、`RoutineContext`、`ICopy`、Event/Channel 类……）与 `BabyPenguin/Utils.penguin`（188 行）（38–52 行），可经环境变量 `PENGUINLANG_BUILTIN` / `PENGUINLANG_UTILS` 覆盖。
  - **遍流水线**（构造器，54–65 行），按序：
    1. `SemanticScopingPass`（01）
    2. `TypeElaboratePass`（02）
    3. `SymbolElaboratePass`（03）
    4. `ConstructorPass`（04）
    5. `InterfaceImplementationPass`（05）
    6. `SyntaxRewritingPass`（06）
    7. `CodeGenerationPass`（07）
    8. `MainFunctionGenerationPass`（08）
    9. `CheckReturnValuePass`（09）
  - `Compile()`（566–589 行）逐遍运行，然后 `PortTopologyValidator.Validate(this)`（静态端口/连线拓扑审计，位于 `BabyPenguin/PortRegistry.cs`，183 行）。
  - `CatchUp(node)`（547–564 行）：对新创建节点重跑 0..CurrentPassIndex 遍——按需单态化（见下）的引擎。
  - 名字解析（170–543 行）：`ResolveSymbol`/`ResolveShortSymbol`（作用域链走查、导入命名空间、`mut` 自动传播的 `MutableSymbolProxy`、经 `FindClosestVisibleSymbol` 的 ScopeId 遮蔽，290 行）、`ResolveTypeNode`（内建 → `Self` → 函数类型 → 泛型参数 → 类型别名 → 命名空间扫描 → `Specialize`）、`ResolveType`。模型冻结后由 VM 打开只读记忆化缓存 `EnableResolutionCache()`（161 行）——解析曾占自举运行时约 76%。
- **`BabyPenguin/ISemanticPass.cs`**（14 行）：`interface ISemanticPass { Model, Report, PassIndex, Process(), Process(ISemanticNode) }`。逐节点进度用 `obj.PassIndex` 跟踪；每遍跳过已到达或超过其索引的节点。

## 各遍（`BabyPenguin/SemanticPass/`）

| # | 文件 | 行数 | 职责 |
|---|---|---|---|
| 01 | `01_SemanticScoping.cs` | 199 | 从语法创建语义节点（ClassNode/Function/EnumNode/InterfaceNode/InitialRoutine）；重复 → `E_DUPLICATE_SYMBOL`。把类级 initial 例程脱糖为隐藏方法 `fun __initial_<name>(mut this)`、construct 块脱糖为 `__class_construct_<i>(mut this)` / 命名空间级 `__construct_<i>()`——方式是**重新解析合成源码文本**（经 `FunctionDefinition.FromString`，60–70、103–139 行；前缀在 11–23 行）。 |
| 02 | `02_TypeElaborate.cs` | 39 | 占位（标记类型已处理）。 |
| 03 | `03_SymbolElaborate.cs` | 342 | `ElaborateTypeReference`（`type X = Y` 别名 → `TypeReferenceSymbol`）、`ElaborateGlobalSymbol`（命名空间全局、类成员、枚举变体、接口声明、函数符号）、`ElaborateLocalSymbol`（代码容器的局部/临时）。 |
| 04 | `04_Constructor.cs` | 433 | 为类与接口生成 `new` 构造器，含 initial 例程启动前的连线调用。自动生成的类构造器只带 `this`——无字段参数。 |
| 05 | `05_InterfaceImplementation.cs` | 604 | 虚表构建——`BuiltVTable`（143）、`AutoClassifyClass`（246：IValueType 与 IReferenceType）、`AutoAddICopy`（291）、`MergeVTables`（500，传递接口）、`FinishVTable`（534）、`ValidateInterfaceFieldTypes`（57：拒绝无 `Box<T>` 的非 IRef 接口字段/枚举载荷）、`CallInterfaceConstructor`（558）。强制孤儿规则（122–130 行）。 |
| 06 | `06_SyntaxRewriting.cs` | 736 | 脱糖遍：`RegisterVariableNets`（`connect` 的隐式 `_Fanout` 线枢纽）、`IdentifyAsyncFunction`、`RewriteLambdaFunction`（闭包 → 合成 lambda 类：`CreateLambdaClass` 76 行、`CollectClosureSymbols` 99 行）、`RewriteImplicitWait`、`RewriteWaitExpression`、`RewriteAsyncExpression`（async → 调度器作业）、`RewriteGenerator`（yield）。 |
| 07 | `07_CodeGeneration.cs` | 54 | 对每个 `ICodeContainer` 调用 `container.CompileSyntaxStatements()`（跳过未特化泛型内的体）。 |
| 08 | `08_MainFunctionGeneration.cs` | 72 | 合成 `__builtin._main`：调用各命名空间构造器 → 运行顶层 `__construct_*` → 经 `SchedulerAddSimpleJob` 启动每个 initial 例程 → 调用 `__builtin._run()`。 |
| 09 | `09_CheckReturnValue.cs` | 69 | 全路径返回校验。 |

## 节点 / 符号 / 类型模型

- **语义节点**（`BabyPenguin/SemanticNode/`）：`Namespace.cs`（99，含 `MergedNamespace`——同名命名空间跨文件合并）、`ClassNode.cs`（109）、`EnumNode.cs`（147）、`InterfaceNode.cs`（115）、`Function.cs`（121，含 `LambdaFunction`）、`InitialRoutine.cs`（49）、`BasicTypeNode.cs`（240——基元 + 泛型 `Fun`/`AsyncFun` 的 `BasicTypeNodes` 注册表）。
- **接口**（`BabyPenguin/SemanticInterface/`）：`ISemanticScope`（101）、`ISymbolContainer`（184）、`ITypeContainer`（146）、`IRoutineContainer`（22）、`ITypeNode`（113）、`IVTableContainer`（62），以及 **`ICodeContainer.cs`（3207 行）**——语义代码生成主力：`CompileSyntaxStatements()`（13 行）、`AddStatement`（238）、`AddExpression`（2426）、`ResolveExpressionType`（1765）、成员访问绑定（2224）、try/catch 区域（`SemanticCatchRegion`，229 行）、while 循环标签栈、RTL 端口/连线发射辅助（`TryEmitEventWire` 1025、`EmitPortSlotRead` 1316、网络枢纽 1151–1284）。经 `AddInstruction`（3202）追加符号级 IR 指令（见 [PenguinLang IR](./22_PenguinLangIR.md)）。
- **符号**（`BabyPenguin/Symbol/`）：`ISymbol.cs`（39——IsLocal/IsTemp/IsParameter/IsClassMember/IsStatic/IsFunction/IsVariable/IsEnum 标志 + Mutability + TypeInferStatus）、`VariableSymbol`（63）、`FunctionSymbol`（188；其 `TypeInfo` 是特化的 `Fun<Ret,Args...>`；另有函数值类型的 `FunctionVariableSymbol`）、`TypeSymbol`/`TypeReferenceSymbol`（56）、`EnumSymbol`（46）、`MutableSymbolProxy`（55）。
- **类型**（`BabyPenguin/Type/`）：`IType.cs`（97——携带可变性的类型接口与隐式转换规则）、`BasicTypes.cs`（208）、`ClassType`（81）、`InterfaceType`（75）、`EnumType`（75）、`TypeStructure.cs`（66）。

## 泛型 / 单态化

`ITypeNode.Specialize(List<IType>)`（SemanticInterface/ITypeNode.cs 39 行）。对类（`SemanticNode/ClassNode.cs` 5–30 行）：**从同一语法节点创建全新 `ClassNode`**、设置 `GenericArguments`、把实例加入 `GenericInstances`，然后 **`Model.CatchUp(result)` 在实例上重放所有遍**。全名：特化的 `Ns.Name<A,B>`、未特化的 `Ns.Name<?>`。所有体/代码生成遍跳过未特化泛型节点（`IsGeneric && !IsSpecialized` 守卫）。`ResolveTypeNode`（SemanticModel.cs 478–507 行）按实参表找现有实例或创建——单态化**在解析内按需驱动**，不是独立遍（EmperorPenguin 用专门的 `MonomorphizePass`）。

## 虚表

`BabyPenguin/SemanticInterface/IVTableContainer.cs`：`VTable` 类（12–61 行）名为 `vtable-<InterfaceFullName>`，持有 `List<VTableSlot> Slots`，其中 `VTableSlot(ISymbol InterfaceSymbol, ISymbol ImplementationSymbol)`（10 行）；还持有 `Functions`——接口实现方法是 IR 生成可达的真实代码容器。基元类型经 `BasicTypeNode` 实现 `IVTableContainer`，因此存在 `SemanticModel.FindAllIncludingBasicTypeVTables`（SemanticModel.cs 92–101 行）。

## 与 EmperorPenguin 的结构对比

| | BabyPenguin | EmperorPenguin |
|---|---|---|
| 表示 | 一棵可变语义树 | 由遍构建的显式 Bound 树 |
| 遍结构 | 9 个编号遍重遍 `FindAll(...)`，逐节点 `PassIndex` | 9 个具名协作类（`BuildScopesPass`……`ValidateControlFlowPass`）按索引对齐的 AST/bound 对 |
| 单态化 | 解析内按需（`CatchUp` 重放） | 专门的 pass-3 不动点 |
| 错误 | 异常（`BabyPenguinException`，`BabyPenguin/Common.cs` 36 行） | 累积的 `SemanticError` 列表 |
| 元编程 | 无（只解析 `#template`） | 完整元引擎（见 [EmperorPenguin MetaProgramming](./31_EmperorPenguinMetaProgramming.md)） |
| 重写 | 06 遍脱糖 async/lambda/wait/端口网络 | 绑定器脱糖（wait/spawn）+ MetaRewriter 预处理 |
