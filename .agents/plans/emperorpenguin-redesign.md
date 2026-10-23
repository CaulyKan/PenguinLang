# EmperorPenguin 重构架构设计（v2 · 终态规格）

v2 变更：① 并行执行模型改为 async + mt scheduler（clock-model 形态，Pass 10 封闭分析
决定可偷性），废弃 folk；② `#if/#define` 采用 C 语言式**文件级作用域** + 命令行 `-D`
全局注入（语义变更，替代现状的全局 option 表）；③ 只描述最终状态，不含迁移里程碑。
评审报告（emperorpenguin-redesign-review.md）的有效结论已吸收：特化归属模板文件、
失效依赖类型清单、规范序 emit、MetaHost 完整指纹、DefArena 补位置字段、StrMap 泛型化。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
0. 设计目标与原则
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
① LSP 可用性：大项目（EP 自身 16.6k 行）改函数体 20-50ms 出诊断；meta 路径无秒级税；
   project 模式无死锁。② 自举吞吐与确定性：pass4==pass5 md5 收敛；增量会话与全量会话
   产出逐字节一致。③ 语言纪律：数据结构优先值类型与不可变；并行只通过 async 语法
   （无线程概念暴露）；`#if` 分支只在执行原语层。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
1. 总体架构
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Session（进程级：LSP 与编译 CLI 共享）
    GlobalIndex     全局归并层（delta 边界重建：symbols/types/instances/ifaces/users）
    TypeInterner    全局类型 intern 表（含 template-args 表示与 specialized-def 表示的
                    canonical 合并——复刻 is_same_type 的 unification 规则）
    MetaHost        #fun JIT 宿主：整种子进程内 memo + ORC 种子驻留
    LibEnvironment  libmeta 反序列化产物 + lib 预特化实例注册（一次构建全程复用）
  FileSnapshot（不可变 per-file 快照，带版本链）
    DefArena / ExprArena（值类型 slot 节点）
  管线
    阶段 A per-file（async fan-out，delta 内并行）
    阶段 B 全局（delta 边界，单线程确定序）
    阶段 C emit（LSP 不跑）

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
2. 并行执行模型（async + clock-model 调度器）
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
编译器进程以 `--enable-multithread-scheduler --enable-clock-model` 运行（EMPEROR_THREADS=N）。
语言层无线程概念：并行只通过 `async` 创建点，Pass 10 封闭分析在**编译编译器时**判定
每个 async 可偷/固定（共享格 Shareable = Sync ∨ Imm ∨ ValueShare ∨ Own；可达调用闭包
须无全局写、extern 白名单内）。

  fan-out 形状（阶段 A）：
    for file in batch:
        futures.push(async compile_file(file_id, cli_defines_fp))   # 可偷：参数 u64 Imm、
                                                                    # 闭包只写本任务 arena
    for f in futures: wait f          # 结果以 Ownership<FileSnapshot> 移交（Own 格）
  归并（阶段 B）在 delta 边界单线程进行——clock-model 的边界即"全员停靠"屏障，引用交接
  构造上安全；Fifo/Signal 延迟构件的边界提交序列化跨线程交付。

约束与配合：
  - 决策 A（已启动协程不换线程）：compile_file 是纯 CPU 任务，一次性跑完不停靠，天然满足。
  - 决策 D（线程间不搬对象）：任务只接收 Imm 上下文（file_id + defines 指纹 u64），
    产出 Ownership 移交；中间态全部留在任务本地。
  - 分配走 glock（单锁）：DefArena/ExprArena 值类型 SoA 把任务内分配压到少数几个大数组，
    分配竞争与 GC 交接成本随 arena 化消解；GC 回收只发生在 delta 边界（边界 STW），
    恰好与阶段 B 重合——归并时无并行任务存活。
  - 串行退化 = 同一 async 表面在 classic（N=1）下逐个执行，无需 #if。
  - `#if PARALLEL/SERIAL` 只用于：阶段 A 的驱动函数是否走 async 批量发射（SERIAL 版
    顺序 await），以及自举链早期（cs 后端无 async 支持的 pass1/pass2 载体）。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
3. 条件编译语义（C 语言式 · 文件级）
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  #define NAME VALUE   从该行起至文件尾有效（不跨文件；重定义 = E_DUPLICATE）
  #if / #elif / #else 表达式：defined(NAME)、NAME == "v"、布尔组合；只看本文件已生效
                       的 #define 与命令行注入
  命令行 -D NAME=VALUE 编译期全局注入（等价于每个文件的隐式前缀；对所有文件可见）
  #option（全局 option 表）废除，由上述机制取代
  #fun 同名跨文件 = E_DUPLICATE（函数定义语义，进符号表/exports，不再按文件序隐式胜出）
  快照缓存键 = hash(源文本) ⊕ hash(CLI defines 集)。文件独立成立——一个文件的 #define
  变化不使其他文件失效，只有 CLI defines 集变化才全量失效。
  自举链：BP 侧实现同一套 splice 语义（def 级 + 语句级）；两实现以双跑 diff 测试锁定。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
4. 核心数据结构
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  DefArena（per-file，SoA + slot，冻结）
    kinds/parents/name_off/flags/locs: array<...>   # locs = 行列打包 u32（LSP 跳转/悬停）
    func_pool/class_pool/enum_pool/impl_pool        # payload 按类分池
    pool: StringPool
  ExprArena：kinds/types/operands/lits + 符号表旁表与 scope 旁表（slot 间接，冻结发布）
  FileSnapshot：file_id、text_hash、fingerprint（exports ∪ impl 边哈希）、exports、refs、
    instances_owned、errors、global_epoch、arena/exprs；**版本链**——B1 分发特化时 owner
    文本未变，产出"同 text_hash 的新快照版本"（追加槽的 copy-on-write），旧版本仍服务
    在途查询。
  GlobalIndex（每 delta 边界重建）：symbols、types（TypeInterner 视图）、instances、
    ifaces、users（symbol/类型/接口 → 引用文件集反查）、files（文件序）。
  StrMap 升级为 StrMap<V>：泛型值、动态桶（按负载扩容）、keys() 确定序迭代——
    GlobalIndex 重建预算以 EP 规模实测后定（评审指出 256 桶链长 64，原估算乐观）。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
5. Pass 管线与数据结构操作
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
阶段 A（per-file async；输入=文本+GlobalIndex(E) 视图+defines 指纹；输出=新快照）
  A0 Splice       #if/#define 文本级处理（文件内 + CLI defines）→ 展开源文本
  A1 Parse        → 可变 DefArena/ExprArena
  A2 BuildScopes  文件内 scope、export 位、exports+impl 边 → fingerprint
  A3 ResolveTypes 类型引用：文件内查 arena，跨文件查 symbols(E)；TypeInterner canonical
                  申请（template-args 与 specialized-def 表示合到同一 slot）；
                  记录 refs（外部符号命中）；投机解析窗口（suppress 错误）在此
  A4 CollectInst  扫泛型使用点（含对特化体的重扫：读模板文件 ExprArena + 特化 scope
                  视图）→ 实例化请求（值参模板 JIT 请求转交 MetaHost，非同步副作用）
  A5 BindBodies   原单遍 bind（符号/表达式/构造器合成/impl 登记/值类型分类/控制流），
                  写 ExprArena 与 types；分派到的特化体在此 bind；迟到分类补建对齐
                  catch_up 语义（值类漏分 → E_SIZE_CYCLE 的防线保留）
  A6 Diagnose     errors 打包 → 冻结发布 FileSnapshot
阶段 B（delta 边界，单线程，文件序）
  B1 InstanceAdjudicate  请求按 (文件序,请求序) 裁定；归属 = **模板所在文件**
                  （特化挂模板文件的 scope，body 解析看到模板兄弟符号——与现状一致，
                  不需要跨文件 scope 链重建）；#specializing/box-forwarding 注入随模板
                  文件的快照版本链走；轮次上限与超限行为对齐现状（静默停止改诊断）
  B2 IfaceLayout  impl 边合成 → vtable 布局（接口量小，毫秒级）
  B3 MergeIndex   重建 GlobalIndex，epoch+1，产出 exports/impl/实例三类 diff
阶段 C（emit；async fan-out 同 A）
  C1 per-file emit 冻结快照+全局 layout → 函数级片段；needs_* flag 与字符串 id 由
                  B 层预收集（并集/全局重编号），emitter 无跨函数可变态
  C2 规范序拼接   按（符号名字典序, 文件序）拼接——输出与编译路径无关，
                  增量会话与全量会话逐字节一致；新旧对比用规范化 diff

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
6. 增量协议与失效依赖类型清单
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  didChange(X): text_hash 未变 → done；A0..A3(X)；fingerprint 未变 → A4..A6(X)+B3；
  变了 → diff 驱动精确失效，fan-out 重编 dirty，B1/B2 迭代至稳定，B3。
  失效依赖类型（每类一条判定，缺一不可）：
    ① 符号引用      refs ∩ changed_exports ≠ ∅
    ② impl 反向     impl 边变化 → 经 users 反查把引用该类型/接口的文件拉入 dirty
                    （含 primitive_impl 表与 ICopy/IRef 分类翻转）
    ③ 模板注入      模板文件 fingerprint 变 → 其全部已裁定点失效；实例归属表变化 →
                    使用该实例的文件失效
    ④ CLI defines   defines 集变化 → 全部快照失效（缓存键的一部分）
    ⑤ 计数器命名    合成名计数器文件局部化（__lambda_<file>_<n>），消除该依赖类
  确定性：dirty 处理序=文件序；B 全文件序；C 规范序 → 同输入必同输出（含增量路径）。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
7. MetaHost（#fun / #specializing 的宿主）
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  种子指纹 = hash(全部种子源文本[exe-relative 4271 行] ⊕ 全部 #fun 文本 ⊕ 全部 #class
  build_text ⊕ --meta-src 集 ⊕ host_coroutine flag ⊕ extern 声明面)——任何一项变化都
  换新种子（漏项 = 陈旧种子 = 错误编译，故指纹必须保守完整）。
  会话内整种子 memo（不拆"种子驻留+本体小模块"——跨模块 ORC 解析有 SEGV 前科，除非
  另行专项验证）；active-model 单例的切换只在 B 边界进行（单线程），多文档交错由
  单编译 actor 序列化；compiling_meta_names 的重入语义（gate 自编译期求值 false）
  原样保留；unit B 递归重入（种子编译内嵌完整管线）在 Session 各缓存上登记重入深度。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
8. Lib 消费
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  LibEnvironment：libmeta 反序列化 + 声明 defs（虚拟文件快照）+ 预特化 instances 注册，
  进程内一次构建；ast-zip 对齐等价物随 DefArena 重建（libmeta 注入槽位）。
  verbatim 模板与 stdlib 源文件 = 普通 FileSnapshot（text_hash 命中即复用）。
  用户实例化请求先查 GlobalIndex.instances：命中 → 引用 lib 实现（declare-not-define），
  未命中 → B1 归属裁定的本地特化。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
9. LSP 集成
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  单编译 actor：全部增量事务（读 epoch → fan-out → 归并 → 写 epoch+1 → 发布）在
  一个 actor 内串行化；文档 actor 只持编辑器状态与查询。诊断按打开文档集过滤
  （未打开的受影响文档发空集清空）。跳转/悬停/rename 经 DefArena.locs + users 反查。
  workspace 全量诊断 = 一次 B3 归并的副产物（各文件快照的 errors）。

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
10. 自举链
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  pass1（BP cs 后端，无 async）：`-D SERIAL`——驱动函数走顺序执行分支
  pass2（原生，classic 单线程）：`-D SERIAL`（或 PARALLEL classic N=1，行为一致）
  pass3 起 PARALLEL + clock-model（链接 mt runtime；pass2 能 bind/emit async 语法——
  async 降级为 __SpawnCtx 类 + 调度器调用，无状态机 lowering）
  收敛轮 pass4/pass5 同 defines、同线程数（EMPEROR_THREADS 固定）→ md5 收敛成立；
  规范序 emit 之下增量/全量等价，收敛与"新旧对比"都由规范化 diff 兜底。
