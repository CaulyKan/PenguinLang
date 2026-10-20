# ISync 实现者审计表（gc-two-tier-heap M0 交付）

日期：2026-10-19 · 全仓 `impl ISync` 共 **40 处**（core_builtin 5 + scheduler_mt 15 +
scheduler_free 15 + others/libpenguin-esl 6），去重后 **21 种类型**。

## 审计判据

1. **字段纪律**：引用字段只在同步原语保护下变更（`_sync_begin/_sync_end`、原子操作、
   或构造后只读）；
2. **载荷规则**（P1/§10.12）：写入通道/结果容器的值必须是 Shareable
   （Imm / 值可共享 / Sync / Ownership）——已由 `--strict-handoff` 检查器强制（stdlib 豁免，
   由本表人工背书）；
3. **跨线程实际路径**：`_sync_*` 临界区内的 registry / channel 事务，或 spawn 协程的
   结果容器交付——全部经过 §10.3 移交点（写点提升已接线：`write/try_write/push/complete`）。

## 主仓 34 处（19 种）

| 类型 | 文件 | 字段/载荷 | 跨线程路径 | 判定 |
|---|---|---|---|---|
| `Ownership<T>` | core_builtin | payload 由构造点全图新鲜性保证 | spawn 参数/结果 | ✅ Own 档 |
| `Mutex<T>` | core_builtin | payload 一切变更在 pthread 锁内（with/set）；构造/set 走写点提升 | 任意 | ✅ 本计划 §2.12 |
| `AtomicI64/U64/Bool` | core_builtin | cell 在 GC 堆外（malloc），原子访问 | 任意 | ✅ 标量 |
| `_TimerWait` | mt/free | 只读 result_tick + 构造期填充 | spawn 结果等待 | ✅ 只读 |
| `__SpawnFuture<R>` | mt/free | result: Option<R>，只在 `complete()`（锁内 + 写点提升）写 | 跨线程 complete | ✅ 移交点已接线 |
| `__SpawnUnitFuture` | mt/free | 无载荷（done 标志原子） | 同上 | ✅ |
| `Signal` | mt/free | 订阅表在 `_sync_*` 临界区内 | 信号分发 | ✅ 临界区 |
| `_SyncList<T>`（mt） | scheduler_mt | 链表在 `_sync_*` 内变更 | registry | ✅ 临界区 |
| `AsyncQueue`（free） | scheduler_free | 同 _SyncList 角色 | registry | ✅ 临界区 |
| `Fifo<T>` | mt/free | 队列/pending 在锁内事务；元素经 write/try_write（写点提升） | 通道 | ✅ 载荷规则由检查器强制（用户侧） |
| `LatestChannel<T>` | mt/free | committed/pending 在锁内；元素同上 | 通道 | ✅ |
| `MergeChannel<A,B>` | mt/free | 委托内部通道 | 通道 | ✅ |
| `_NeverSource/_LateSource/ConstantSource` | mt/free | 构造后只读 | 通道源 | ✅ 只读 |
| `_Fanout` | mt/free | wires 在临界区内 | 分发 | ✅ 临界区 |
| `MultiInput` | mt/free | sources 构造后只读 | 聚合 | ✅ 只读 |
| `_EventSubscription` | mt/free | sid 只读；ev 引用在临界区内 | 事件 | ✅ |
| `Event<T>` | mt/free | subscribers 在临界区内 | 事件 | ✅ 临界区 |

**结论**：主仓 34 处全部合规——同步容器的字段变更都在锁/临界区内，载荷流转全部
经过已接线的移交点（M2 的 `__gc_promote` 覆盖 `write/try_write/push/complete`）。
通道元素类型参数 T 在**用户侧**由 P1 检查器强制 Shareable；scheduler 内部事务
载荷（如 `RoutineState` 枚举）在临界区内整值写读，符合"值语义档"。

## others/libpenguin-esl 6 处（6 种，不在强制范围）

`Bus`、`Clock`、`Rom`、`Ram`、`Pipe`、`Reg`——ESL 仿真模型库（others/ 实验区）。
共享基于仿真 tick 的确定性协议（同周期读、延迟写），非 pthread 临界区。
**声明**：`others/` 目录不进发布编译（不在任何 .penguins 源集），封闭性声明不覆盖它；
若将来纳入 std，需按上表判据复审（特别是 `Ram`/`Reg` 的可变载荷——tick 协议是否
等价于"锁内"需要单独立项）。

## 与 P1 检查器的衔接

- 检查器（SemanticConcurrency.check_handoff_payload）对 stdlib 命名空间豁免
  （`scheduler`/`_utils`/`__builtin` 子串匹配），由本表替代背书；
- 用户代码的 `write/try_write/push/complete` 载荷：默认 `-Wconcurrency` 警告，
  `--strict-handoff` 升 `E_HANDOFF_NOT_SHAREABLE`（负向/正向用例：
  Tests/ConcurrencyTest/HandoffNotShareableRejected.md / HandoffImmAccepted.md）。
