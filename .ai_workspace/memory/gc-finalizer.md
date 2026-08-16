# EmperorPenguin GC（2026-08-16 启用 + 优化 + finalizer + 两个深层修复）

分支 `feature/gc-finalizer`，提交：
- `cdcc804` 启用 GC + O(1) 哈希表 + 迭代标记
- `8c36c2b` GC finalizer（sweep 两阶段 + dispose_mem 挂 destructor 槽位）
- （第三个提交）阈值回收新对象悬垂修复 + 扫描区注册表

## 深层教训（排障花最多时间的两个坑）
1. **阈值回收窗口的新对象悬垂**：`_emperor_gc_alloc` 在返回前触发回收，此刻栈上只有 header 基址（header+0）而扫描只认用户指针（header+24）→ 新对象被自己触发的回收扫掉 → 返回悬垂指针。修复必须**有界**：只在真正进入回收时置 `marked=1`、返回后立即清零。第一版无条件预置 marked=1，在回收未触发时泄漏到下一周期 → mark 阶段把 marked 当"已处理"跳过对象体扫描 → 对象引用的字符串全部被误回收（pass2 编译器在解析 SemanticModel.penguin 时段错误的根因，gdb 抓到 `Lexer.is_at_end → string_length(悬垂)`，677,285 字节 = SemanticModel.penguin 精确大小）。
2. **裸缓冲区内部的 GC 引用不可见**：`Vector/HashMap/Array` 元素存储是裸 `_malloc`，保守 GC 扫不到其内部的指针 → `Vector<string>/HashMap<string,JsonValue>` 里仅被缓冲区引用的对象在首次回收即被误杀（jsonrepro 最小复现：`name_missing`；pass4 dynlib 元数据读取失败同根因）。修复 = **扫描区注册表**：`__builtin._gc_scan_add/remove`，容器在分配/换绑/释放时维护；finalizer 因此成为正确性的必要环节。

## 关键事实
- GC 曾被 `EMPEROR_GC_DISABLED`（8TB 阈值）禁用两个月；现默认启用（256KB 初始阈值），诊断用环境变量 `EMPEROR_GC_DISABLE=1`。
- **自举 pass1 产物 tmp/pass2 也是原生二进制**（cs 后端只是宿主，生成物链接同一 std/c runtime）——gc.c 改动会直接影响 pass2 自身，这是重压测试路径。
- 普通 `class`（无 impl）= 值类型（栈上）；要进 GC 堆必须 `impl __builtin.IReferenceType`。
- finalizer：metadata 尾部 destructor 槽位（`void(*)(void*)`）与 `dispose_mem(mut this)` IR 签名精确一致，emitter 直接指向实现函数；仅非值类型 + impl `__builtin.IMemoryDispose` 的类；值类型必须 null（装箱副本 vs 栈原件双重释放）。
- sweep 两阶段（摘链→终析→释放）保证死对象图内部引用在终析期有效；dispose_mem 必须幂等（HashMap 先 dispose 内层 Vector，内层自己的终析会再跑一次）。
- BabyPenguin 的 `--backend=cs`：Penguin IR → C#（NativeAOT 原生 exe），对象由 CLR 管理——与 EmperorPenguin 原生后端不同链路。

## 已知无关 bug（非本次引入）
- ~~`Tests/StdlibTest/ArraySetCallInFunction.md` 红哨兵~~ **已修复**（2026-08-16，分支 feature/fix-array-call）：普通函数体内对局部 `std.Array<T,N>`（值参数模板特化）调方法 → this 寄存器未定义；函数内循环 `new std.Array` 静默不分配。**根因**：`SemanticModel.collect_generic_instantiations_from_ast_impl` 收集泛型特化需求时遍历了 initial_routine 和 class 成员函数体，但**缺顶层 function_def 分支**——普通函数体内的 `new Foo<X>()` 特化需求从不收集 → 特化不挂 bound 树 → `emit_new` 找不到 layout → 分配被静默跳过（方法调用 this 寄存器未定义）。修复：加顶层 function_def 分支，用 `collect_from_ast_expr_safe(body)`（与 class 成员一致）。这是该函数 2026-08 早些时候修"循环体内 new 漏收集"（collect_from_ast_statement_safe 注释）的同类漏网之鱼。注意：pass1（BabyPenguin VM）在特化绑定路径上有既有 E_RUNTIME_INVALID_OP bug，验证以 pass2/3 为准。
- `LambdaTest/FunFieldMemberCall`（pass2/3 编译失败，`<global>.twice` 符号）在 3c95d31 namespace 重构后即存在，与 GC 无关。

## 验证
```
dotnet run --project Tests/PenguinTestRunner.csproj -- --filter AutoDispose --compilers pass3
dotnet run --project Tests/PenguinTestRunner.csproj -- --filter StdVectorGcElements --compilers pass3
./penguin -b && md5sum tmp/pass3 tmp/pass4
```
自举收敛 + DynamicLinkTest 11/11 + 全量套件见最终报告。

# mutability 语义变更（2026-08-16，feature/fix-array-call 分支，未提交）

## 新语义
- `let x = <expr>`：不可变绑定 + **不可变值**（不继承表达式可变性；`new` 表达式本身是 mutable 但不再泄漏）
- `let mut x = <expr>`：可变绑定 + **可变值**（无条件升 mutable，即使函数返回类型不带 mut——json 宏生成的 `let mut _map = _f.as_object()` 依赖此）
- `let x: mut T = a`：显式标注才做 imm→mut 别名检查（IRef）
- 推断不继承的修改点：EmperorPenguin SemanticModel let 推断分支 + BabyPenguin ICodeContainer.InferVariableType（两边同步）

## 关键坑
- 编译器源码/单测/18 个 markdown 测试全要 `let x = new` → `let mut x = new` 迁移（91 个 xunit 期望 `mut ns.A`→`!mut ns.A`）
- json.penguin 宏生成代码模板里的推断式绑定也要迁移（生成文本是 penguin 源码字符串）
- **已知双编译器不一致（遗留）**：`let b: mut List = a`（a 不可变）BabyPenguin 拒绝、EmperorPenguin 接受——根因 EmperorPenguin utils.penguin 的 List 缺 `impl IReferenceType`（BabyPenguin 版有）→ is_iref_type=false 别名检查不触发。修复需给 List 加 IReferenceType 并重新自举（类型分类变化影响面大），测试 LetExplicitMutTypeAliasFromImmutableRejected.md 已锁定 BabyPenguin 行为并注明差异。
- 新增 6 个测试在 Tests/LetMutTest/（正负向覆盖三种声明形态）

## 验证
markdown 全量 1059/0 fail（1264 组合）+ dotnet test 530/530 + 自举收敛 md5 一致。opencode 会话 stage1-3b（deepseek-v4-flash，总成本 ~$0.87 内增量）。

# Mutability 推断语义变更（2026-08-16，分支 feature/fix-array-call）

## 新语义（已落地）
- `let x = <expr>`：不可变绑定 + **不可变值**（不继承表达式可变性；`new` 也不行）
- `let mut x = <expr>`：可变绑定 + 可变值（**无条件升 mutable**，即使表达式返回非 mut——宏生成代码依赖）
- `let x: mut T = <expr>`：不可变绑定 + 可变值；显式标注才做 IRef imm→mut 别名检查
- 落点：EmperorPenguin `SemanticModel.penguin` let 推断分支 + IRef 检查加 `type_spec.is_some()` 前置；BabyPenguin `ICodeContainer.cs InferVariableType` 两分支同步
- 编译器源码 50 处 `let x = new` → `let mut`；18 个 markdown 测试迁移；6 个新 LetMutTest；91 个单测期望修复（opencode 委派完成）
- 验证：自举 md5 收敛、markdown 套件 1059/0、dotnet test 530/530

## 已知遗留：双编译器 List 别名检查不一致
- `let a = new List(); let b: mut List<i64> = a;` BabyPenguin **拒绝**、EmperorPenguin **接受**
- 根因：BabyPenguin Utils.penguin 的 List 有 `impl IReferenceType`，EmperorPenguin utils.penguin 的没有 → `is_iref_type()` false → 别名检查不触发
- 修复方向：给 EmperorPenguin utils.penguin 的 List/Queue 补 `impl __builtin.IReferenceType`（牵动编译器自身值/引用分类，需重新自举）——测试 `LetExplicitMutTypeAliasFromImmutableRejected` 已锁定 BabyPenguin 侧行为
