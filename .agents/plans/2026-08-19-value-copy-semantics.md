# 值类型真正拷贝语义改造（mut = 纯编译期权限）

日期: 2026-08-19
状态: ✅ 全部完成 — Phase 1–4 已提交；四编译器全矩阵 1130 PASS / 0 FAIL（含自举收敛）

## 执行进度（2026-08-21 00:05 完成更新）

### Phase 1 — BabyPenguin ✅ 已提交 `6cac3ce`
VM/C# 后端/语义检查全部落地；CS 后端 179/181 + xunit 单测 56/56 通过；新 sentinel 测试
（MutBindingAliasValueClass / ClassFieldInlineReadAlias / EnumPayloadExtractSnapshot /
ExplicitCastValueAlias / ImplicitValueToInterfaceBoxes）已入库。

### Phase 2 — EmperorPenguin emitter 🔄 主体完成，未提交
由编码 subagent 实现（2026-08-20 02:36–04:29，因模型配额中断）。改动全在工作区：
`LLVMEmitter.penguin`（+948/−333）、`SemanticBindBodies.penguin`（+46）、
`SemanticBindExpressions.penguin`（+9）、`IRGenerator.penguin`（−29）、`BoundType.penguin`（布局注释）。

已落地（对照上表逐项）：
- `field_type_to_llvm` / `BoundType.byte_size_rec`：mut 不再影响布局 ✅
- **`scan_write_chains`（新）**：函数级写链预扫描 — 种子 = WRMBR owner + 方法 receiver
  （`FuncParamTypes.first_param_is_this` 识别 CALL arg0；CALL_VIRT obj）；沿定义链传播，
  ASSIGN → `alias_assign_regs`（别名而非拷贝）；unbox 惯用法绑定（`let self: mut Self =
  cast<mut Self>(this)`）的 ASSIGN 也入 alias 集；仅单定义 temp 寄存器；GLOBAL_LOAD 终止链
  （emitter 直接给全局地址）✅
- `emit_rdmbr`：内联 struct 字段读 → 拷贝（`<reg>.f` entry-alloca + memcpy）；写链命中 →
  字段槽 GEP 直通 ✅
- `emit_rdenum`：payload 提取 → 拷贝（`.f` alloca）；写链命中 → 变体自身
  `EnumVariantLayout.payload_llvm_type` 决定内联槽别名 / ptr load / primitive load ✅
- `emit_assign`：值类绑定 → 新鲜 `.val` alloca + memcpy；alias_assign 例外 ✅
- callee 入口：值类参数（除 `this`）拷贝到 `%<param>.copy`（`value_param_copies`），`emit_arg`
  路由到副本 ✅
- `emit_load_ptr`/`emit_store_ptr`/`emit_new_enum`：mut 分支与 `mut_load_ptr_names` 全删，
  槽位一律内联 ✅
- 全局变量：值类全局内联 struct 存储；GC root 改为 `_emperor_gc_scan_add` 保守扫描区 ✅
- `IRGenerator.lower_assignment`：强制 mut 提取 hack 已删（普通 RDENUM+WRMBR，写链由预扫描识别）✅
- **unbox 改为别名视图**（box 才是拷贝）— vtable 存根写穿的关键 ✅
- binder：非左值链写检查（E_MUTABILITY，`chain_root_base` 到 call/cast/new/enum_variant）✅；
  隐式 值类→接口 的绑定/实参转换插入 box cast（`SemanticBindBodies` let-decl +
  `SemanticBindExpressions` call args）✅
- temp/named 显示名冲突（用户变量 `%t2` vs temp#2 `%t2`）：scan_mutable_regs 按 (name, kind)
  区分，mixed-kind 名排除出 struct-alloca；拷贝存储加 `.f`/`.val` 后缀防撞 ✅

### 测试状态（pass1 = EP on BabyPenguin VM）
- **full2**（03:49）：216/228 = 9 预期翻转 + 3 真回归（GenericCascadeTest / GenericTest /
  TryBindInterfaceClass）→ 根因已定位：emit_cast ptr-identity 分支未注册类型；temp/named 撞名
- **collision-fix 批次后 full3**（04:02 启动）：215/228 = 8 个预期翻转正确（含
  MutBindingAliasValueClass 9→1、EnumPayloadExtractSnapshot 9→1、ExplicitCastValueAlias 9→1、
  EnumVariantAssignToMutableVariableTest 99→0、EnumVariantPassToFunctionTest 55→0、
  ListFor* 1112→12 ×2）+ **5 个待修**：
  - ListVectorOps / ListValueElements / ClassMethodWithReturnedOwnerTest / EnumValueClassPayloadInline：
    同一根因 — RDENUM/RDMBR 拷贝结果 `<reg>.f`（alloca 指针）被 sret/temp store 直接当 struct 值用
    （`store %class.P %t5.f, ptr %_sret_result`，clang: "'%t5.f' defined with type 'ptr' but
    expected '%class.P'"）→ 消费路径需 coerce_operand ptr→struct load
  - ClassFieldInlineReadAlias：输出 0，应翻转（绑定拷贝后 a.x 读到旧值）为 1
- ⚠️ LLVMEmitter 最后一次编辑在 04:16（full3 启动之后），full3 结果部分过时；当前树重跑验证中

### 待办（剩余）
1. ~~修完上述 5 个收尾回归~~ ✅ 4 个编译失败已被 subagent 最后一轮编辑（04:16）修复，重跑
   全部通过；ClassFieldInlineReadAlias 实为 Phase-3 翻转测试（BabyPenguin 参考输出同为 "0"，
   计划中"9→1"系笔误，正确翻转 9→0）
2. ~~Phase 3：翻转 9 个测试期望值 + 新增测试~~ ✅ 提交 `5e39705`：9 个翻转
   （含两个 sentinel 转绿：EnumPayloadExtractSnapshot、ExplicitCastValueAlias）+
   新增 4 测试（ClassFieldChainWriteThrough 链写 42、MutParamCopyDirect 参数拷贝 1、
   ListValueElemSetWriteback set() 写回 1112、NonLvalueChainWriteRejected E_MUTABILITY 编译失败）
3. ~~Phase 4：文档~~ ✅ 提交 `49920cf`（03_DataTypes.md 增加 "Value-Copy Semantics in
   Practice" 小节 + mut 参数/receiver 区分 + List 元素拷贝说明）
4. ~~pass2/pass3 全矩阵~~ ✅ `./penguin -b` 自举完整通过（pass2→pass3→pass4 收敛，新 emitter
   成功编译 EP 自身）；全矩阵四编译器 `PenguinTestRunner`：
   **1130 PASS / 0 FAIL / 0 ERROR / 213 SKIP**（SKIP = pass1 条件跳过），vs 基线 +6 新通过、
   0 新失败、0 时间/内存回归（tmp/testruns/20260820-234840）

### 提交记录
- Phase 1 `6cac3ce` vm: value types copy on binding/param/extract（BabyPenguin）
- Phase 2 `c1e51db` compiler(EP): value-copy semantics in emitter（含本计划文件）
- Phase 3 `5e39705` tests: flip value-copy expectations; add copy/chain-write coverage
- Phase 4 `49920cf` docs: value-copy semantics in 03_DataTypes

### 验证记录
- 全量 `babypenguin,pass1`：**500/500 PASS**（tmp/testruns/20260820-231440，1352s）

## 背景

文档（`Documentation/03_DataTypes.md`）明确规定值类型赋值 **"Always copied"**、mutability 是
**编译期权限系统**。但实现（BabyPenguin 的共享 VM + EmperorPenguin 的 `mut → 指针布局`）把 `mut`
从"权限"偷换成了"存储身份"，导致值类型在绑定/参数/提取三个入口全部失去拷贝语义：

```
let mut b = a; b.x = 9;    // 现在改到 a（EP 别名指针；Baby 共享对象）—— 错误
let mut b: i32 = a; b = 9; // 正确：拷贝
```

修复目标：值类型在**绑定/赋值/参数传递/提取**时一律拷贝；`mut` 不参与任何内存布局或存储身份决策。

## 语义规则（最终，已确认）

1. **值类型（ICopy 类、枚举、primitive）绑定/赋值/参数一律拷贝**；`mut` 仅是编译期
   修改权限标志，不影响布局。
2. **`List<mut T>` ≡ `List<T>`（值类型元素）**：元素访问是拷贝（`at()`/循环变量），
   写回必须显式 `set()`。**引用类型元素**：`List<mut T>` 允许 `let mut x` 修改所指对象，
   `List<T>` 由 binder 拒绝（现有 E_TYPE_MISMATCH/E_MUTABILITY 检查已覆盖，无需新增）。
3. **`mut` 参数拷贝**：`fun f(p: mut Point) { p.x = 9; } f(a)` 不写穿 a。
4. **递归值布局 → E_SIZE_CYCLE 报错**，走 `Box<T>`/引用类（已确认现有源码无 mut 破环依赖：
   `_QueueNode` 由递归分类守卫强制为引用类）。
5. **枚举 payload 链式写 `e.a.x = 42` 保持可用**（B 方案，用户已选）：变体 payload 是 e
   存储的一部分，链式写通过**槽位左值寻址**直接写入（不经过拷贝）；绑定提取
   `let q = e.a` 仍拷贝（q 是独立副本）。
6. **新增编译错误：成员写的 owner 链中含调用/转型**（`l.at(0).x = 9`、`f().p.x = 9`、
   `cast<IFoo>(x).v = 9`）→ 拒绝，防止写被丢弃的副本（静默无效）。

## Phase 1 — BabyPenguin（参考 VM，先改，用于验证语义）

| 位置 | 改动 |
|---|---|
| `VirtualMachine/RuntimeFrame.cs:1199` `MaybeCopy` | no-op → 对值类深拷贝（复用 `RuntimeValueCopier.CopyIfValueSemantic`，RuntimeValue.cs:384-425），覆盖 ASSIGN/ARG/RDMBR/GLOBAL_LOAD 四处 Store |
| `VirtualMachine/RuntimeFrame.cs:487` RDENUM | 值 payload 提取拷贝；写链目标（结果被 WRMBR 消费）跳过拷贝 |
| `VirtualMachine/IRGenerator.cs` | 生成 RDMBR/RDENUM 时向前看一条：结果寄存器立即作为 WriteMemberInstruction owner → 指令打写链标志（新增 bool），VM 对应 Store 不拷贝 |
| `VirtualMachine/ExternFunctions.cs:128-133`（AddList） | 删除 `elemIsMut` 共享分支 → 值类元素一律 `CopyIfValueSemantic`（引用类元素共享不变） |
| `VirtualMachine/RuntimeFrame.cs` `CastValue` | 值类→接口显式 cast 改为拷贝（对齐 EP `emit_box`，使 ExplicitCastValueAlias sentinel 转绿） |
| 语义层成员写绑定处 | 新增非左值链写检查（owner 链含调用/转型 → E_* 错误） |

## Phase 2 — EmperorPenguin emitter（核心）

| 位置 | 改动 |
|---|---|
| `LLVMEmitter.field_type_to_llvm:1305,1317,1331` | 删除 `mut → ptr` 两条分支：mut 值类/枚举字段一律内联 struct/enum |
| `BoundType.byte_size_rec:808` | 删除 `mutability Mutable → 8`：mut 值类返回内联大小（保持 #sizeof/List stride 一致） |
| `emit_assign:2529` | 值类 dest：不可变 → fresh alloca + struct 拷贝（不再 reg_map 别名）；可变 → alloca 存 **struct** |
| `scan_mutable_regs:4496` | 值类 ref\<X\> 寄存器的 alloca 统一按 struct 尺寸/类型分配（现在仅 sret-elision 目标如此） |
| `emit_arg:2513` / `emit_function` 入口 | 值类参数在 callee 入口拷贝到本地存储（覆盖 byval 与非 byval、CALL_VIRT/INDIRECT 裸指针传参） |
| `emit_rdenum:4040` | 写链预扫描（函数级，仿 `scan_sret_elision`）：结果被 WRMBR 消费 → 槽位别名（现 mut 分支改由该扫描门控）；否则一律拷贝（现 4172-4178 memcpy 路径对所有值 payload 生效，不再依赖 `mut ref<` 前缀） |
| `emit_load_ptr:3046` / `emit_store_ptr:3079` | 删除 mut 分支及 `mut_load_ptr_names`：槽位一律内联，load=拷贝、store=struct 内联存储 |
| `emit_new_enum:3492` | 删除 mut payload 堆盒分支：值类 payload 一律内联 `%class.X` store；引用类 payload 存指针（不变）；`is_mut_load_ptr_name` 别名保持逻辑删除 |
| 全局变量 | 值类全局内联存储；GC root（ae577bd 的 mut-ref roots）改为覆盖内联结构体的指针字段（复用 metadata `field_is_ptr` 或整体注册） |
| `IRGenerator.bound_type_to_ir_type:1855` | `mut ref<X>` 前缀保留为无害字符串（权限层仍用），emitter 存储决策不再消费它 |
| `IRGenerator.lower_assignment:1707-1721` | 强制 mut 提取 hack 简化为普通 RDENUM（写链由 emitter 预扫描识别） |
| binder `SemanticBindBodies:763-792` 附近 | 新增非左值链写检查（与 BabyPenguin 对称） |
| `is_mut_ref_ir_type`/注释 | 清理 dead code 与过时注释（含 `emit_type_definitions:1699-1703` 陈旧注释） |

## Phase 3 — 测试

**翻转期望（9→1 / 1112→12 等）**：
- `Tests/LetMutTest/MutBindingAliasValueClass.md`（9→1，绑定拷贝）
- `Tests/ClassTest/ClassFieldInlineReadAlias.md`（9→1）
- `Tests/EnumTest/EnumPayloadExtractSnapshot.md`（sentinel 转绿，9→1，重写描述）
- `Tests/InterfaceTest/ExplicitCastValueAlias.md`（sentinel 转绿，9→1，重写描述）
- `Tests/EnumTest/EnumVariantAssignToMutableVariableTest.md`（99→0，绑定拷贝）
- `Tests/EnumTest/EnumVariantPassToFunctionTest.md`（55→0，mut 参数拷贝）
- `Tests/ListForTest/ListForDirectMutableElem.md` / `ListForIterMutableElemAllowed.md` / `ListForImmutableContainerMutableElem.md`（1112→12，值类循环变量拷贝）

**保持绿（B 方案写链）**：`EnumVariantMutableAccess_NonGenericTest`、`EnumVariantMethodCallTest`、
`EnumVariantMethodModifySelfTest`、`EnumVariantNestedAccessTest`、`EnumVariantReplaceValueTest`、
`EnumVariantDirectAssignTest`、`EnumGenericCustomTypeTest`、`EnumMixedPayloadRefVariantWriteThrough`、
`CascadeEnumMutableMemberAssignmentTest`、`ListVectorOps`（已是拷贝语义，含 ref 变体写穿部分）。

**新增测试**：值类绑定拷贝；mut 参数拷贝；类字段链写 `w.p.x = 9` 可用 + 绑定拷贝；
枚举链写 `e.a.x = 42` 可用 + 绑定提取拷贝；`List<mut T>≡List<T>` 值类 set() 写回；
非左值链写编译错误（调用/转型链）；递归 mut 值字段 → E_SIZE_CYCLE（不再豁免）。

**检查项**：`EmperorPenguin.Tests` 的 IR/LLVM 快照测试（BatchIRTest/BatchLLVMTest）是否含
`mut ref<`/布局断言；`BabyPenguin --backend=cs`（CSharpBackend/ExternLowerer.cs:158,172）
的值类拷贝路径；MetaEngine object_ref（M6 ObjectArg，预计不受影响，需回归）。

## Phase 4 — 文档

`Documentation/03_DataTypes.md` 补充：mut 不影响布局；值类型绑定/参数/提取总是拷贝；
枚举 payload 链式写是左值寻址；容器元素访问是拷贝、写回用 set；递归值布局必须走 Box/引用类。

## 验证与提交

1. `dotnet build` + `dotnet test`（两个单测项目）
2. `PenguinTestRunner --compilers babypenguin,pass1`（快速环，Phase 1+2 后全绿）
3. `./penguin -b` 重新 bootstrap → 全矩阵（pass2/pass3）
4. 分里程碑提交：Phase 1 BabyPenguin → Phase 2 EP emitter → binder 检查 + 测试 → 文档
5. 已知风险：EP 可变寄存器 struct-inline 表示与 sret-elision 的交互最繁琐；
   C# backend 的值类路径需回归确认
