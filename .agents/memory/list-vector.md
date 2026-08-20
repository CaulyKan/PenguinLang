# _utils.List 向量化（2026-08-18）

分支 `feature/list-vector`。`EmperorPenguin/src/utils.penguin` 的 `_utils.List` 从双向链表（每元素 2 个堆对象：80B node + 16B Option box）改为连续缓冲区 vector（每 grow 一次 `_malloc`，每元素仅 1 个 16B box），编译速度提升 ~34-38%（测试矩阵总编译时间 pass2 3217s→1989s，pass3 3911s→2600s）。

## 核心设计：槽位 = Option<T>（不是裸 T）
- 每槽存**完整 `Option<T>`**，`#__load(Option<T>)/#__store(Option<T>, ...)` 整体 load/store（24B，payload 槽指针大小，对所有元素类型布局一致）。
- **为什么不是裸 T**：值类型 class 的值在寄存器里是 ptr（可能指向栈 alloca），`#__store` 直存会**栈逃逸**；且 `at()` 里 `new Option<T>.some(v)` 的 NEW_ENUM 对值类型 class payload 做 alloc+deref **装箱**——若 at() 每次包装则每次调用堆分配 + 拷贝（parser token 热路径不可承受）。
- 装箱语义与旧链表**完全一致**：push/set 时 `new Option<T>.some(v)` 装箱一次，at()/迭代共享同一 box → **值类型 class 元素经 `let e: mut P = list.at(i).some; e.mut_method()` 仍写回列表**（别名保持，探针对拍逐字节一致）。用户预告的"迭代副本"语义变化实际未发生。
- List `impl IReferenceType`（字段全 value-like，不标会被误分类为值类型 → 赋值拷贝 struct 双 List 共享 buffer）+ `impl IMemoryDispose`（GC finalizer 自动 `_gc_scan_remove` + `_mfree`）。

## 迭代器持有 List 引用（非裸 base/end 快照）
GC 链 iterator→list→scan region 使临时列表迭代安全；迭代中 push 触发 realloc 时每次 next() 重读 buf/len，push/pop-during-iteration 语义与链表一致。代价 = 每次 next 一次额外 deref，可忽略。

## 为此做的编译器增强（通用能力，非 List 特设）
1. **泛型 meta 类型参数**（SemanticBindMetaCalls）：`#sizeof(Option<T>)`/`#__load(Option<T>, addr)`/`#__store` 接受 `Ident<...>` 泛型操作数——`meta_type_arg_generic_specs` 提取 `IdentifierExpression.generic_args`（parser 对裸名 `Option<T>` 本就解析进去；`__builtin.Option<T>` 限定名会 parse 成 member_access **不行**，必须裸名），`resolve_intrinsic_boundtype_ge` 走 mangle_generic_name + global_scope.lookup_type_in_scope(mangled) 拿特化符号（与泛型函数调用同一模式）。**前提：该特化已被类型位置引用过**（List 方法签名里的 Option<T> 天然满足）；凭空 #sizeof 未特化的泛型 enum 会 E_INTERNAL（enum layout not found）。
2. **is_same_type 的 enum 特化统一**（BoundType._is_template_args_of 补 enum_def 分支）：template+args（Option<i64>）与特化 def（Option$…，无 args）此前只有 class 分支能互认，enum 缺失 → `#__load(Option<T>)` 返回类型撞方法返回类型报 E_RETURN_TYPE_MISMATCH。
3. E_MUTABILITY 两处报错补真实源位置（原来 SourceLocation("",0,0) 无位置，排障困难）。
4. MetaEngine.base_meta_sources 返回值加 `mut`（List 变 IReferenceType 后 `let x: mut List<X> = f()` 的 imm→mut 规则正确生效——凡 `mut List` 声明配非 new 初始化器，被调函数必须返回 `mut List`）。

## 坑与事实
- **`#sizeof(Ref)` 对值类型 class = 8**（BoundType.is_value_type 走 ref 视角）而 emitter 布局是 24B 栈 struct——两套视角本就不一致；碰巧槽位存 ptr 时自洽。enum 大小端 payload 超 8B 的理论场景对编译器内所有 List 元素类型不成立（payload 槽都是 ptr）。
- **remove 前部删除 O(n)**（移位）vs 链表 O(1)——编译器源码**零调用** `.remove(`（grep 证实），接口保留给外部；10 万次 remove(0) 耗时 51s，勿在热路径前部删除。
- **BabyPenguin 的 `_utils.List` 是另一份**：`BabyPenguin/Utils.penguin` 的 extern C# List（测试矩阵 baby vm/cs 用它，Compile.Args 被忽略）——本次未动，行为与新实现输出一致（ReferenceRuntimeValue 不 clone = 引用语义同 box 别名）。
- pass1（VM 解释 EmperorPenguin 或 cs backend）**能**编译 pointer intrinsics（vector.penguin 测试不含 pass1 只是未展开验证）；bootstrap pass1 显式编译 utils.penguin 走 EmperorPenguin 自身语义层。
- 探针对拍法（同程序新旧 utils.penguin 各编译运行一次 diff 输出）是验证语义等价的最快手段；`#__address_of` 对 at() 结果取的是临时 sret 的地址，探不了元素地址。
