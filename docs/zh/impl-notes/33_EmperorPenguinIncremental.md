# 33. EmperorPenguin 增量编译

## 概述

三个协作部件为 didChange 驱动的消费者（LSP、`--incremental` CLI 模式）提供能跨编辑复用工作的编译：

- **`GlobalIndex`**（`EmperorPenguin/src/bound/GlobalIndex.penguin`）——一次完成编译之上的全局归并层反向索引，在每次 `SemanticModel.bind()` 末尾重建。
- **`IncrementalSession`**（`EmperorPenguin/src/bound/IncrementalSession.penguin`）——包在 `EmperorPenguinCompiler.compile_sources` 外面的会话层：per-file 快照、epoch 计数器、两车道 didChange 事务。
- **`LspCompileActor`**（`MagellanicPenguin/LspServer/LspCompileActor.penguin`）——LSP 的单一编译 actor，串行化每个增量事务与导航查询（见下文）。

全量编译路径不受影响：自举链与普通 CLI 仍直接调用 `compile_sources`。会话只持有 didChange 消费者需要的跨事务状态。

## GlobalIndex

`GlobalIndex.rebuild(model, result)` 作为 `bind()` 的最后一步运行，构建五张表加文件列表与 per-file/全局指纹。所有表都是 `StrMap<string>`，值是紧凑的 `a|b|c` 编码（标识符不能含 `|`，所以编码对名字是单射的）：

| 表 | 键 | 值 |
|---|---|---|
| `symbols` | 符号全名 | `kind\|owner_file`——kind：`var`\|`fun`\|`type`\|`enum_member`\|`namespace` |
| `types` | 类型全名 | `kind\|owner_file\|specialized\|name`——kind：`primitive`\|`class`\|`enum`\|`interface` |
| `instances` | 特化全名 | `kind\|template_file\|def_name\|site`——kind：`class`\|`enum`\|`interface`\|`fun`。`template_file` 是**模板**所在文件（特化归属规则：实例归属于拥有模板的文件）。`site` 是来自 `MonomorphizePass.b1_adjudicated` 的 B1 请求站点记录——该实例由哪条请求路径裁定（`fixpoint-type`、`fixpoint-func`、`call-*`、`new`、`member-*`、`coroutine-ensure`、`generator-ensure`、`intrinsic`、`meta-type`、`resolve-cascade`、`nested-sig`、`late-complete`、`sig-enum`）；从未经过 B1 的 libmeta 预特化实例记为 `""` |
| `ifaces` | `"iface \|> for_type"` | `owner_file\|origin`——origin：`body`\|`impl_for`\|`iface`\|`primitive`。另有按追加序的平坦行列表（map 是去重/查找视图，行列表是指纹消费的有序视图）。只存在于某个**特化**上的边（由 `#specializing`/装箱转发注入）以模板文件为 owner 登记 |
| `users` | 目标全名 | 引用它的定义列表（失效判定的反向查找侧） |

**指纹**是对每个文件导出面的稳定哈希（djb2 风格）：exports ∪ impl 边 ∪ 拥有的实例，以 `"len:part"` 前缀拼接折叠保证无歧义。per-file 指纹是快速车道的裁定键（见下）；全局指纹汇总整个编译。

**`TypeInterner`**（`SemanticTypeInterner.penguin`）在重建期间喂数：它按规范化字符串键驻留类型，把同一实例化的模板+实参表示与特化 def 表示折进同一槽——恰好是 `is_same_type` 统一的配对（可变性不参与身份；class 与 enum 折叠，接口的两种表示保持相异）。它目前是纯查询/统计层，尚无解析路径查询它。

## IncrementalSession

### FileSnapshot

一次完成编译的冻结 per-file 视图：

- `text_hash`——文件文本的稳定哈希；
- **AST 签名指纹**——快速车道裁定键：对该文件符号面（定义的签名）的哈希，对函数体变化不敏感；
- GlobalIndex 的 per-file 视图：exports、impl 边、拥有的实例、引用的目标、错误；
- 到 `result.definitions` 的 def 段范围（合并定义列表中属于该文件的切片）。

快照在编译结束时构建一次，之后从不修改（**冻结纪律**："publish-then-readonly"）。Bound\* 对象树留在 `last_result`，由会话拥有，在每个慢车道边界整体替换（快速车道则原位手术式更新）；快照只持有复制出的视图数据——纯名字列表——因此没有任何快照字段别名可变的 model 状态。唯一有意的例外：`errors_kept` 持有只读的 SemanticError 对象引用。

### didChange 事务：短路、快速车道、慢车道

一次 didChange（一个或多个文件的新文本）按三层裁定：

1. **短路**——每个变更文件的 text hash 仍匹配已发布快照（且 CLI defines 指纹未变）：编译不可能变；原样返回 `last_result`。
2. **快速车道**——每个变更文件的 AST 签名指纹**未变**（函数体不同、符号面相同）且会话全局 fast-eligible（变更文件无 meta 面、全局无 RTL 面、无 suspension、非 mt 调度器）。会话在**保留的** model + result 树上重放 pass 3-9：
   - 变更文件的 AST def 原位换入；
   - 其 bound def 的处理戳回滚，使 pass 4/5 短路（签名相同）而 pass 8/9 只重绑变更的函数体；
   - 死特化按 B1 请求台账对账后逐出；
   - per-file 合成名计数器重置；
   - errors 与 `users` 条目从快照**重放**，合并输出与全新全量编译逐字节一致。
3. **慢车道**——其它一切（结构性变更、meta 面、首次编译）：未变更文件的 AST 缓存被复用（跳过解析），然后在全新 model 上跑完整新流水线。输出按构造与普通全量编译一致。

**epoch 计数器**实现 B 边界切换：读侧持有 epoch 引用，提交中的事务以新结果原子安装 epoch+1。

**CLI defines 指纹**（`--define K=V` 集合的哈希）是失效键：defines 集变化使每个文件的视图失效，强制慢车道。

### CLI 标志

- `--incremental`——让编译经 `IncrementalSession` 走（per-file 快照 + 解析缓存）。行为与普通全量编译一致；与 `--meta-src` 组合不受支持（改为运行普通全量编译并在 stderr 提示）。
- `--edit=<file>=<replacement>`——配合 `--incremental`：首次编译后，把 `<file>` 的文本替换为 `<replacement>` 的内容并重新编译会话。可重复；编辑按顺序应用。测试套件以此从一次调用驱动两段编译事务。

## LSP 集成：LspCompileActor

LSP 的编译拓扑是**单一 actor**。`LspMain` 的文档路由把流量以 `LspCompileMsg` 值经一个 FIFO 转发给 `LspCompileActor`：

- **OPEN / EDIT 消息会被合并**：actor 非阻塞地排水 FIFO 积压，把连续的非查询段折叠为每个受影响会话一个事务。同 uri 的写入只保留最新文本——编辑器的当前文本永远赢，过期的中间态从不被编译。
- **查询永不丢弃也不合并**：导航查询只在先于它到达的每个编辑都被编译后才被回答（严格交错序）。
- **CLOSE** 退休文档：单文件会话整个丢弃；project 模式会话保留（文件集稳定），该文件的输入回退到磁盘文本。

`emperor.IncrementalSession` 对象只活在 actor 内部，每个路由键一个（project plan 身份，或单文件模式的文档路径）——一个串行循环同时把编译执行的 lib 元数据读取也串行化。

**发布协议**（workspace 诊断）：被触碰会话的每个 OPEN 文档收到新编译的 per-file 过滤诊断；项目里未 OPEN 但先前收到过非空诊断的文件，在错误消失后收到空集，未打开文件上过时的客户端波浪线随之消失。

**panic 守卫**：每个事务有 sjlj 守卫。编译器抛运行时错误时，崩溃的会话标记 POISONED——其下一个事务丢弃对象、从全量编译重建；触发文档的诊断照常发布，服务器存活（last-ok 结果继续服务查询）。

## 测试

- `Tests/IncrementalTest/`（10 例，Apply To Pass1/2/3）——由 `--incremental --edit=...` 驱动的两车道哨兵：新增函数（慢车道）、改函数体（快速车道）、跨文件级联、泛型实例交换、lambda 删除、死特化陈旧错误、meta 类面、特化重建、验证轮。
- `Tests/LspTest/IncrementalDidChangeDiagnostic.md`——LSP 经会话应答 didChange 并发布坏文档的诊断（逐字节一致的帧）。
- `Tests/LspTest/LargeFrameDeliveredWhole.md` / `FdWakeWithSecondWaiterNotStranded.md`——stdio 健壮性哨兵：>64KB 的响应帧完整送达；fd 唤醒的协程在另一个 fd 等待者仍停靠时也会运行。
