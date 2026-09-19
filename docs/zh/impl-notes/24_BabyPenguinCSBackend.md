# 24. BabyPenguin CS 后端

C# 后端（`--backend=cs`）把共享 IR（见 [PenguinLang IR](./22_PenguinLangIR.md)）下降为 C# 源码，用 `dotnet build` 编译并运行结果——进程内或独立可执行文件皆可。它存在的理由有二：它是自举的第一个原生阶段（pass1：BabyPenguin 的 C# 后端把 EmperorPenguin 源码直接编译成能输出 LLVM IR 的机器码）；它也在跨编译器测试套件中充当相对解释器基准的差异探测器（`Tests/*.md` 中的 `BabyPenguin CS` 行）。

全部位于 `BabyPenguin/CSharpBackend/`。

## 入口与模式

CLI（`BabyPenguin/Program.cs`）：`--backend vm|cs`（默认 vm，23 行）、`--keep-cs`、`--run-only <dll>`、`--cs-out <path>`（26–33 行）；分派在 114–117 行。

**`CSharpBackendRunner.cs`**（85 行）——三种模式：

1. **进程内**（默认）：下降 → 编译 → 加载 → 反射调用 `BabyPenguinCompiled.Generated.__builtin__main`，经 `GlobalState` 共享同一 `RuntimeGlobal`，使 I/O 与退出码和解释器逐字节一致。
2. **`--cs-out <path>`**：构建独立 exe（加复制的 BabyPenguin 运行时依赖）。
3. **`--run-only <dll>`**：重跑先前编译的程序集（缓存命中路径）。

**`DiskCompiler.cs`**（242 行）：写临时 `.csproj`（`OutputType Library|Exe`，以 HintPath 引用 `BabyPenguin.dll`）→ `dotnet build -c Release` → `/tmp/bp_cs_cache` 下的 SHA256 缓存（源哈希 + BabyPenguin.dll 时间戳）→ 加载进**可回收 AssemblyLoadContext**。

## 下降流水线

**`CSharpBackend.cs`**（459 行）——`CSharpBackend.Lower(model, standalone)`：

1. 用 `IRGenerator` 重新生成 `IRModule`（设置 `BP_DUMP_IR` 时转储到 `/tmp/bp_ir.txt`）。
2. 从入口计算**可达性**：从 `__builtin__main` 前奏提取的命名空间构造器（`ExtractNamespaceConstructors`，311 行）、`IRModule.EntryFunctions`（initial 例程），加**所有虚表实现槽**（类、枚举与基元类型虚表——58–98 行，因为函数指针分派对调用图不可见）。在 `CalleesOf`（378 行：直接调用、方法引用 RDMBR、函数指针常量）上做 BFS。
3. 收集被调用 extern 及调用点签名（`ExternCallsOf`，325 行，跟踪函数指针来源）与 IR 类型根。
4. 输出一个 `Generated.cs`：类型声明（`TypeLowerer`）、`public static class G { ... }` 全局、含下降后函数的 `public static class Generated`、生成的 extern（`ExternLowerer`）、`__InitVtables()`（反射式虚表注册——`GlobalState.RegisterVtable(typeof(T), "iface.method", MethodInfo)`，194–266 行），以及同步运行命名空间初始化与 initial 例程的**快速路径 `__builtin__main`**——绕过协程调度器，对不含 wait/emit/yield 的程序（如编译器自身；类文档 19–24 行）是正确的。可选的独立 `Program.Main` 接线 `RuntimeGlobal` + `ProgramExitException` 处理（281–299 行）。

### FunctionLowerer

**`FunctionLowerer.cs`**（405 行）：一个 `IRFunction` → 一个带局部 `r_<registerIndex>` 的 `public static` C# 方法；`LowerInstruction`（128 行）映射：

- CONST → 赋值（函数指针常量记录为来源）；ASSIGN（非 `IsAliasChain` 时值复制）；CAST（`CastExpr`，364 行：to-string 经 ToString/`__ToName`/bool 三元；接口 → 具体类下转）。
- BINOP（字符串关系比较经 `string.CompareOrdinal`，171–176 行）；UNARYOP；RDMBR（字段读；函数指针读变成 `MethodRef` 或 `VirtualMethodRef` 记录）；WRMBR（字段写、载荷复制）。
- BR/BR_COND → `goto`；RET/RET_VOID；CALL/CALL_VOID → 直接静态调用。
- `CALL_FUNC_PTR` → `EmitFuncPtrCall`（282 行）：MethodRef → 前置接收者的直接静态调用；VirtualMethodRef → `GlobalState.InvokeVirtual` 反射分派（含基元上 `ICopy<T>.copy` 的特例，309–319 行）；FuncRef → 具名调用。
- NEW → `new T()`；NEW_ENUM → 带 struct 载荷复制的 `_value`/`_containing_value`；ISENUM；RDENUM；GLOBAL_LOAD/GLOBAL_STORE → `G.<name>`；ISINSTANCE → `obj is IHasMeta m && m.__meta.Is("TypeId")`；其余 → `NotImplementedException`。

### TypeLowerer / ExternLowerer / CSharpEmitter

- **`TypeLowerer.cs`**（136 行）：类 → 带 `__meta`（`Meta` + `InterfaceMapEntry[]`）的 `public sealed class X : IHasMeta[, IValueSemantics]`；枚举 → 含 meta/标签/载荷的结构体；输出特化类型的传递闭包（遍历 `GenericInstances`）。
- **`ExternLowerer.cs`**（192 行）：标量 extern 体的静态表（print/exit/string_*/file_*/exec_cmd/args/shift，27–62 行）经 `GlobalState.Global` 路由；集合 extern（List/Queue/StringBuilder/AtomicI64）经 `__impl.__backing`；未知 extern 得到参数数正确的桩使程序集可编译。
- **`CSharpEmitter.cs`**（152 行）：IR 类型 → C# 类型表（i8→sbyte……f64→double；`ref<X>`/`struct<X>`/`enum<X>` 解包；接口 → `object`）、`Normalize`（剥 `!mut`）、标识符改名、操作数渲染（`r_N`、`G.x`、`L_x`）、字面量渲染、双目/单目映射。
- **`NameMangler.cs`**（47）、**`RuntimeSupport.cs`**（168，命名空间 `BabyPenguin.CSharpBackend.Runtime`）：`GlobalState`（共享 `RuntimeGlobal`/args、`Clone`、`CopyValueSemantics`、虚表字典 + `InvokeVirtual`、`ExecCmd`）、`IHasMeta`、`IValueSemantics`、`Meta`/`InterfaceMapEntry`（EmperorPenguin 元指针的精简 C# 对应物）、`RoutineYield`（状态镜像 ReturnStatus）。

## 在自举中的角色

Makefile 的 pass1 阶段用 `BabyPenguin --backend=cs` 编译 `EmperorPenguin/EmperorPenguinPass1.penguins`（Makefile 约 202–213、350–356 行）——输出的 C# 被构建为能产出 pass2 `.ll` 的原生能力发射器。快速路径 main（无调度器）在那里是合法的，因为编译器源码不含挂起点。

## 测试

- `BabyPenguin.Tests/CSharpBackendBenchTest.cs`——`FibCompiledVsInterpreter` 下降 fib(28)、经 `DiskCompiler` 编译、对编译版与解释器计时，断言 ≥5× 加速（91 行）。
- 跨编译器套件把 `dotnet BabyPenguin.dll -q --backend=cs <src>` 作为 `BabyPenguin CS` 编译器种类运行（`Tests/Program.cs` 约 1480 行）——与解释器相同的逐字节期望，用于发现分歧。
