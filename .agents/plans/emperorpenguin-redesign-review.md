# EmperorPenguin 重构架构设计（v1）独立评审报告

评审对象：`.agents/plans/emperorpenguin-redesign.md`（2026-10-21，独立 agent 对照源码逐条验证）

## 问题清单（按严重程度排序）

### 1. [严重] folk 并行原语在语言中不存在，M3 的"8 核加速"假设无根基
- BabyPenguin/Parser/PenguinLang.g4 无 folk 产生式（CLAUDE.md 声称支持与语法不符）；
  scheduler.penguin 全部是 ucontext 协程 extern + "no-ucontext 平台的顺序 fallback"，
  运行时无任何 OS 线程原语。
- 两种解释都麻烦：folk=协程 → CPU 密集阶段 A 无让出点，fan-out 零并行收益，M3 落空；
  folk=OS 线程 → green-tea GC（单线程假设）、penguin_meta_set_active 模块级单例
  （SemanticMetaRewrite.penguin:848-852）、StrMap/List 全无锁——改造面完全未预算。
- 缓解：M0 前先做 folk 语义决策并单独评审运行时/GC 成本；M3 承诺按协程语义重写
  （收益≈IO/等待重叠）或列出 GC 线程安全化子里程碑。

### 2. [严重] refs 失效协议捕捉不到 impl 边的反向影响
- impl 块是反向依赖：文件 A 的 `impl I for T` 不引用 B 的符号却改变 B 的行为。
- 证据：SemanticModel.penguin:80-83 primitive_impl 全局平行表（任何文件的 k.hash() 解析
  与 `x is IHash` 静态折叠由它决定）；SemanticClassifyValueTypes 按 impl 集合翻转
  ICopy/IRef 传值语义；resolve_impl_base_type 的 lookup_type_anywhere 全局扫描。
- 删掉 impl IStringOps for MyType 时 exports 不变 → fingerprint 不变 → 漏判（陈旧诊断）。
- 缓解：GlobalIndex 增加 `type -> impl 边提供文件集` 反查表；fingerprint 计入本文件
  impl 边哈希。

### 3. [严重] #define/#if 具有跨文件全局顺序语义，per-file 阶段 A 与 prepass 冲突
- SemanticMetaRewrite.penguin:34-49 run_prepass 按文件序遍历所有 defs：#fun 注册序
  决定同名赢家；函数体内 #define 语句按序写全局 option 表；meta_option_keys 是全局态。
- 文件 B 的 #if 分支取决于文件 A 是否先 #define——refs/fingerprint 双双漏判；
  快照复用条件不是单文件 text_hash（还有 CLI -D 与其他文件 option 注入）。
- 缓解：option 表构建独立为全局第 0 步（预扫描 #define + CLI defines，产出 option 表
  指纹）；快照缓存键 = text_hash + option 表指纹 + #fun 集指纹。

### 4. [严重] 特化"使用点归属"与现状"模板文件 scope 归属"语义不同；#specializing 跨文件隐式注入
- 现状特化永远挂模板文件的 scope（SemanticMonomorphize.penguin:1717-1737：
  scope_parent = 模板 ns，body 解析看到模板文件兄弟符号）。使用点归属需在使用文件
  快照里重建模板文件的 scope 链——设计未展开。
- #specializing 块（core_builtin.penguin:471/490/500）在任何 List<X> 特化创建时全局
  匹配注入；改模板文件的 #specializing 文本影响所有文件的特化形状，但不是 export
  符号——漏失效。
- 即时特化点散布（resolve_type_specifier 路由 JIT、resolve_intrinsic_boundtype_ge
  按需 ensure+catch_up），"A4 只发请求"的改写面比设计大。
- 缓解：归属保留"模板文件拥有"（贴近现状、scope 链天然正确）；#specializing 文本与
  box-forwarding 规则纳入模板文件 fingerprint。

### 5. [严重] 全局命名计数器 + def 收集序：增量与全量会话产出不同 .ll
- SemanticModel.penguin:258-266 的 spawn/lambda/generator_site_counter（合成名
  __lambda_<n> 等直接进 .ll）、connect/variable_net 计数器——分配序取决于处理序。
- LLVMEmitter 按 module.functions 顺序发射；字符串 id 按首次遇到序；增量 vs 全量的
  def 序不同 → 同一程序两种路径 .ll 字节不同（语义等价）。
- md5 收敛（pass4==pass5 同路径）可保住；但"新旧 .ll 逐字节对比"兜底失效。
- TypeId 泄漏进 .ll 目前不存在（接口 id 是名字常量）——真正泄漏在计数器命名与发射序。
- 缓解：合成名计数器改"文件局部编号 + 文件 id 前缀"；emit 改规范序（符号名排序）。

### 6. [严重] MetaHost 种子指纹不完整 = 错误编译；ORC 拆模块低估
- 种子 = exe-relative 源集（4271 行）+ 当前编译的全部 #fun + 全部 #class build_text +
  --meta-src + host_coroutine 时 scheduler + extern 面（MetaEngine.penguin:397-558）。
  指纹漏一项是陈旧种子 → 错误编译（非性能退化）。
- 现状每个 #fun 是自包含 ORC 模块（互调靠合进同一 unit B）；拆"种子驻留+本体小模块"
  需跨模块符号解析，已有记录在案的跨模块 SEGV（MetaEngine.penguin:550-557）。
- penguin_meta_set_active 模块级单例回调：LSP 多文档交错时 active-model 切换协议空白；
  compiling_meta_names 的重入语义（#specializing gate 自编译期求值 false）须原样保留。
- 缓解：指纹 = hash(全部种子源 + #fun/#class 合成文本 + --meta-src + flag + extern 面)；
  M0 只做进程内整种子 memo（保守正确），拆模块推迟。

### 7. [中] BP 无 #if 是全新功能；pass2 编译 PARALLEL 分支需并发语法完整 emit
- PenguinLang.g4 无 #if 产生式；EP 侧对应实现 859 行——双实现须逐字节等价 splice，
  否则 pass1/2 与 pass3+ 对同一 #if 选不同分支，收敛链断。
- PARALLEL 分支含并发语法 → pass2（SERIAL 构建）必须能 bind+emit 之；emitter 不生成
  协程状态机 → folk 的 lowering 必须是"无状态机的运行时调用"（channel+spawn+join）。
- 缓解：folk 最小语义定为运行时调用；先验证"SERIAL 构建的编译器能正确 emit folk 程序"；
  BP #if 单独立里程碑 + 双实现 diff 测试。

### 8. [中] 数据结构能力缺口
- StrMap：值固定 i64、256 桶硬编码、无迭代——16k 符号链长 ~64，GlobalIndex 重建
  5-10ms 估算可能乐观一个数量级；StrMap<bool>/<IfaceLayout>/<i64set> 不存在，需先写
  泛型版 + 动态桶 + 迭代。
- DefArena 无 location 字段——LSP 跳转/悬停/rename 需要 per-def 源位置，快照里只有
  errors 有位置。加 locs: array<u32>（行列打包）。
- 冻结纪律与 B1 分发矛盾：owner 文件无 didChange 却要追加特化槽 → 需要快照版本链/
  copy-on-write，设计未写。

### 9. [中] LSP 集成细节
- 诊断过滤是现状已有行为（LspCompilationUnit.penguin:117-121）——设计重复现状。
- Session 级 GlobalIndex 的多文档并发：两个文档的增量事务必须原子（单编译 actor
  串行化），设计没提。
- workspace 诊断发布协议（未打开文档要发空清空）未定义。

### 10. [轻] per-file emit 全局耦合：30+ needs_* flags、全 unit layout 闭包、字符串 id
  重编号、has_suspension 聚合——C2 的隐藏工作。迁移期 .ll 对比改规范化 diff。

### 11. [轻] fixpoint 行为等价：10 轮上限静默停止、AST 双源收集（特化类重扫模板 AST 体 +
  特化 scope）、歧义方法跳过留 pass 8——B1 循环须对齐这些行为；特化体归属 owner 后
  模板 AST 在别的文件（owner 的 A4 要读模板文件 ExprArena + 特化 scope 视图）。

## 被低估的实现细节
1. catch_up_def 三变体状态机（临时改写 current_pass_index 精确重放；漏分类 → emitter
   默认值类 → E_SIZE_CYCLE）
2. ast-zip 对齐 + libmeta 注入槽位（SemanticModel.penguin:143-185；dynlib 逆序 splice）
3. 投机解析窗口 suppress_report_errors（A4 不复刻会大量误报 E_RESOLVE_TYPE）
4. #fun JIT 在类型解析期执行且有副作用（splice、active 槽切换）——A3"纯函数式追加"不成立
5. LLVMEmitter 单遍 2553 行、30+ 跨函数可变态——per-file 化等于重写发射骨架
6. ExprArena 4 个数组装不下现状绑定结果（scope 引用、符号对象、字面量文本）
7. unit B 自嵌套递归重入（compile_meta_function 内 new EmperorPenguinCompiler 跑全管线）
   ——Session 级缓存都要定义重入语义
8. TypeInterner 必须把"template+args 表示 vs specialized def 表示"canonical 化到同一 slot
   （is_same_type 的 unification 规则，BoundType.penguin:561-604）

## 设计中合理的部分
- 分层方向（快照+全局小步+epoch 协议）正确；MetaHost 对症（M0 先行正确）；
  stdlib/verbatim 快照复用无语义风险；实例命中与现状 is_shipped_instance 路由方向一致；
  "#if 纪律 + B 阶段串行确定序"原则成立；值类型化分阶段降耦合明智。

**总评**：性能诊断与 M0/M1 收益可信；M3 建立在幻影原语上；增量失效协议在 impl 边/
#define/#specializing 三处有正确性漏洞；MetaHost 指纹完整性与确定性保障需显式设计。
建议先补"失效依赖类型清单"（符号引用/impl 反向/option 顺序/特化注入/计数器命名）
和 folk 语义决策，再冻结 M1 之后的里程碑。
