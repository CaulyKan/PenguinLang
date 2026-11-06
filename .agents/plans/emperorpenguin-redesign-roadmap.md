# EmperorPenguin 重构实施路线（roadmap · 对 v2 终态规格的评估与拆解）

评估基准：`.agents/plans/emperorpenguin-redesign.md`（v2 终态规格，已吸收独立评审全部有效结论）。
本文件的主轴与 v2 不同：**以可读性、可扩展性为第一优先**，性能/并行降级为数据驱动的
可选叠加。每批次独立可验证、可合并，行为锁定贯穿全程。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
0. 对 v2 的评估结论
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## 0.1 保留（v2 的正确骨架）

- **阶段 A（per-file）/ B（delta 边界单线程确定序）/ C（emit）的三段管线**——同时是
  性能与可读性的基础：文件内逻辑看快照，全局逻辑看边界，二者不缠绕。
- **A/B 边界化的实质是删除三个最晦涩的机制**，应作为可读性硬验收而非副产品：
  ① catch_up_def / catch_up_def_before_bodies 的 current_pass_index 临时改写重放
  （SemanticModel.penguin:1048-1078）；② 散布的即时特化点（resolve_type_specifier
  路由 JIT、intrinsic 按需 ensure+catch_up）；③ #fun 同名按文件序隐式胜出。
- **#define/#if 改 C 式文件级作用域 + -D 注入，废除 #option**——唯一 breaking change，
  正确性收益明确（消灭跨文件顺序依赖，这是 per-file 快照成立的前提）。
- **特化归属 = 模板所在文件**（贴近现状 scope 语义）；**规范序 emit**；**MetaHost 整种子
  memo + 完整指纹**（保守正确）；**StrMap<V> 泛型化**；**DefArena 补 locs**。

## 0.2 裁剪（为性能付出的复杂度，与可读性目标错位）

| v2 节 | 处置 | 理由 |
| --- | --- | --- |
| §2 async + clock-model 并行模型 | 降级为批次 6（可选） | glock/边界 STW/Pass 10 可偷性分析是运行时包袱；阶段 A 的 per-file 形状天然并行友好，实现上单线程先行，并行可后叠加且不改语义层代码形状 |
| §4 DefArena/ExprArena SoA 化 | 降级为批次 6（可选） | 对象图→平行数组槽位是可读性净损失；评审已证 ExprArena 装不下现状绑定结果（=重写 A5）。快照语义只需"冻结纪律 + 指纹"，对象图即可达成 |
| §4 FileSnapshot 版本链（per-file COW） | 简化为 epoch 整体切换 | 版本链动机是"B1 追加特化槽 + 在途查询旧版"；B 边界本就单线程，读侧持 epoch 引用、B 提交原子换新已覆盖 LSP 查询一致性 |
| §10 自举链 SERIAL/PARALLEL 双分支 | 随并行降级而消失 | 全链 SERIAL 语义；pass1..pass5 同语义，收敛链更简 |

## 0.3 补缺（v2 未覆盖、可读性最直接的工作）

1. **巨型文件拆分**（新增批次 0）：实测 47,871 行（AGENTS.md 的 "~16,000" 严重过时，
   bound 层 25k/占 52%）。Top3：LLVMEmitter 8391 行 242 方法、SemanticBindExpressions
   6830 行 151 方法、SemanticMonomorphize 3147 行——合计 38%。零语义风险、立即可做，
   且缩小后续每批的 diff 与 review 面（先拆再重排，避免在 6830 行文件里做管线手术）。
2. **emitter 可变态上提**（前移为批次 1）：v2 C1 一句"B 层预收集"带过，实际是
   109 处 needs_ 引用、30+ 跨函数可变态、单遍 8391 行发射骨架——emit 侧最大可读性
   障碍，完全独立于快照架构，可先行。
3. **迁移验证纪律**（v2 自述"不含迁移里程碑"）：每批验收 = `make bootstrap`
   （pass4==pass5 md5 收敛）+ `make test`（全用例）+ `.ll` 逐字节/规范化对比
   （批次 1 落地后用规范化对比）。批次间保持 rebase 纪律（GC/调度器工作并行推进）。

## 0.4 简明版终态命题

裁剪后的核心命题一句话：**每个文件编译为不可变快照（指纹 = exports ∪ impl 边哈希，
缓存键 = text_hash ⊕ CLI defines）；全局层在每个边界按文件序做确定性归并（特化裁定、
接口布局、索引重建）；发射消费预收集的 EmitPlan 按规范序拼接；增量 = 脏文件重出快照 +
一次归并。** 并行与 SoA 是这个形状上的可选加速，不进核心规格。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
1. 批次拆解
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
顺序原则：先机械后语义；独立组件先行；唯一 breaking（批次 2）趁迁移面小（实测
#option/#define/#if 共 39 处、集中 7 个文件，多在 meta/ 与 config）尽早做，避免新代码
继续堆积旧语义。规模感为单人专注估算，含验证。

## 批次 0 · 文件拆分与基础设施（机械，零语义风险，~1 周）
- 0.1 拆 LLVMEmitter：Layout（FieldLayout/ClassLayout/EnumLayout/vtable 构造）、
      Function（函数体发射主干）、Global（字符串/全局变量/typeid）三模块 + 主类作驱动。
- 0.2 拆 SemanticBindExpressions：按表达式类别（字面量/标识符、调用与重载、成员访问、
      控制流表达式、cast/new/try-bind）。
- 0.3 拆 SemanticMonomorphize：实例收集 / 命名 / #specializing 注入三块。
- 0.4 StrMap<V>：泛型值 + 动态桶（按负载扩容）+ keys() 确定序。
- 注意：新文件同步加进 EmperorPenguinPass1/Pass2/Lib 三个 .penguins。
- 验收：bootstrap 收敛；测试全绿；固定输入 .ll 逐字节不变。

## 批次 1 · 发射确定性（独立于语义层，~1-2 周）
- 1.1 EmitPlan：needs_*、字符串表、layout 闭包在发射前单遍预收集为显式计划；
      发射主干变为纯消费 EmitPlan（无跨函数可变态）。
- 1.2 规范序：函数按（符号名字典序, 文件序）拼接；字符串 id 全局重编号。
- 1.3 合成名计数器文件局部化：__lambda_<file>_<n> 等（消除失效依赖类⑤）。
- 验收：同一输入不同处理路径产出相同 .ll；bootstrap 收敛不破坏。
  本批同时为批次 3 提供 .ll 规范化对比这一验收工具。

## 批次 2 · 条件编译语义变更（唯一 breaking，~2 周）
- 2.1 EP 侧：#define 文件级作用域（该行起至文件尾、不跨文件、重定义 = E_DUPLICATE）；
      #if/#elif/#else 只看本文件 #define 与 -D 注入；快照缓存键 += defines 指纹。
- 2.2 BP 侧同一套 splice 语义（def 级 + 语句级）——全新功能（语法 + prepass），
      本批最大新增面；双跑 diff 测试进 CI，任何 splice 不一致都会断收敛链。
- 2.3 #option 表废除；EP 自身源码 39 处迁移（std/dynlib 与测试若涉及一并迁）。
- 验收：双实现 diff = 0；迁移后 bootstrap 收敛 + 测试全绿。

## 批次 3 · 管线 A/B 重排（架构核心、最大风险，~4-8 周，四个可合并子步）
- 3.1 GlobalIndex 数据结构先行（symbols/types/instances/ifaces/users；fingerprint =
      exports ∪ impl 边哈希，含 primitive_impl 表）——只加不改执行流，为后续对账。
- 3.2 Monomorphize 收编为 B1 集中裁定：请求收集（A4）与裁定/注入（B1）分离；
      归属 = 模板所在文件；即时特化点全部收编；轮次上限行为对齐（静默停止改诊断）。
- 3.3 pass 4-9 per-file 化并合并为 A5 单遍；B2 IfaceLayout 从 InterfacesPass/
      ClassifyValueTypes 提炼；**catch_up 系机制与 current_pass_index 临时改写物理删除**。
      风险控制：可先"per-file 化保持 pass 结构"、再"pass 合并"，两子步各自验收。
- 3.4 行为对齐清单：投机解析窗口 suppress、迟到分类补建（E_SIZE_CYCLE 防线保留）、
      AST 双源收集（特化体读模板文件体 + 特化 scope 视图）、TypeInterner canonical 化
      （template-args 与 specialized-def 表示同一 slot，对齐 is_same_type unification）。
- 验收：测试全绿；.ll 规范化 diff 等价；catch_up 系代码删除；TypeInterner 单元测试。

## 批次 4 · 快照 + 增量 + LSP（收益兑现，~3-4 周）
- 4.1 FileSnapshot：保留 Bound* 对象树 + 冻结纪律（发布后只读）+ text_hash 缓存复用
      （stdlib verbatim 源天然命中）。不做 SoA、不做 per-file 版本链。
- 4.2 B 边界整体 epoch 切换：读侧持 epoch 引用，B 提交原子换新。
- 4.3 失效判定四类落地（第五类已由批次 1.3 消除）：①refs∩changed_exports；
      ②impl 反向（users 反查，含 ICopy/IRef 分类翻转）；③模板注入（#specializing/
      box-forwarding 计入模板文件 fingerprint）；④CLI defines 集变化全量失效。
- 4.4 LSP 单编译 actor（增量事务串行化）+ workspace 诊断协议（未打开的受影响文档
      发空集清空）+ 跳转/悬停/rename（DefArena.locs + users 反查）。
- 验收：EP 自身规模（48k 行）改函数体 20-50ms 出诊断；project 模式无死锁；
  增量与全量会话诊断一致。

## 批次 5 · MetaHost 整种子 memo（独立小件，~1 周，位置灵活）
- 完整指纹 = hash(种子源集 ⊕ #fun 全文 ⊕ #class build_text ⊕ --meta-src ⊕
  host_coroutine flag ⊕ extern 声明面)；进程内整种子 memo（不拆 ORC 模块）；
  active-model 单例切换只在 B 边界；unit B 递归重入在 Session 缓存登记重入深度；
  compiling_meta_names gate 语义原样保留。
- 验收：构造指纹扰动用例（改任一成分必须换种子）；LSP meta 路径无秒级税。

## 批次 6 ·（可选，数据驱动）并行与 arena 化
- 触发条件：批次 4 后 profile 显示阶段 A 串行为吞吐瓶颈，且 clock-model 调度器成熟。
  async fan-out 驱动函数 + EMPEROR_THREADS；此批不改语义层代码形状（fan-out 点已预留）。
- DefArena/ExprArena SoA 化：仅当 GC/分配 profile 证明必要。
- 自举链恢复 PARALLEL 分支时的要求对齐 v2 §10。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
2. 全程验收与风险
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
- 每批次出口：`make bootstrap`（md5 收敛）+ `make test`（全用例）+ `.ll` 对比
  （批次 1 前逐字节、之后规范化）+ 该批专属验收项。任一不绿不合并。
- 最大风险集中在批次 3（bound 层 25k 行重排）：以 3.1→3.4 四子步渐进 + GlobalIndex
  对账 + 规范化 diff 兜底；每子步独立可合并，中断不弃盘。
- 批次 2 的 BP 侧 #if 是从零实现，双跑 diff 是唯一等价性锁，必须先于 EP 源码迁移可用。
- 顺手修正：AGENTS.md 的 "~16,000 lines" 已过时（实际 47,871），批次 0 时更新。
