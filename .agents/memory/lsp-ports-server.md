# EmperorPenguin LSP 服务器（port 语法重写 MagellanicPenguin LSP，2026-08-26）

分支 `feature/ep-lsp-ports`，计划 `.agents/plans/emperorpenguin-lsp-ports.md`（L1–L5）。
架构与构建见 `MagellanicPenguin/LspServer/README.md`；本条记踩坑与关键事实。

## 关键事实
- **构建**：`make lsp`（= `build/pass3 --enable-coroutine MagellanicPenguin/LspServer/LspServer.penguins -o build/lsp`，链接 build/libemperorpenguin.penguin-lib，分钟级；两级内容寻址缓存）。e2e 测试走 runner 的 **Run LSP / Prebuilt backend**（`Apply To: Prebuilt` + `Run Args: build/lsp`，run 阶段只是 `cp` 到 workdir）——不重编。
- **stdlib 定位**（LspCompilationUnit.load_stdlib_text）：cwd 优先，然后从 exe 目录向上走最多 12 层找 `<parent>/EmperorPenguin/std/penguin/`。runner 把 exe 拷进 `build/testruns/<ts>/prebuilt/...` 深层 workdir 也能找到；`make publish` 发布时把 stdlib 打包到 `vscode/server/linux/EmperorPenguin/std/penguin`（exe_dir 直接子路径候选命中）。
- **位置基准**：EP lexer 行列都 **1 基准**（line=1, col=1 起）；BP/ANTLR 是行 1 列 0。EP→LSP 一律 `line-1, col-1`（LspQuery 与 diagnostics 均如此）。
- **exit 时序**：exit 通知 → `out_exit(code)` 控制事务进共享 hub → JsonOutputParser 顺序转发为 `!LSP-EXIT:<code>` 哨兵 → StdioStream 写完之前的帧后 `exit(code)`；stdin EOF（无 exit）→ 静止退出 0。
- C# LSP 的怪癖不搬：references 假广告、shutdown 后 1s 强杀、logMessage 刷屏。EOF 静止退出 0 是新语义（更干净）。

## 踩坑（按代价排序）
1. **GC 扫描未对齐 live_lo 跨 mmap 边界 SIGSEGV**（gc.c）：调度器 `sp_park = &marker`（char 局部）通常非 8 对齐（LSP 的 8 协程链 ≡7 mod 8）；`_emperor_gc_scan_set_live` 原样存，collect 扫描 `(void**)p` 从非对齐低端步进，最后一次读可从 region 内起却伸出 `base+bytes` 7 字节 → 跨 32MB 栈 mmap 边界崩。修复 = set_live 与 collect 的 raw-sp 截断处都向下圆整到指针对齐。**崩不崩取决于停驻帧链总尺寸的对齐运气**——最小复现不可稳定构造，真正的红→绿锁是 SessionLifecycle.md e2e（pre-fix 首次 publishDiagnostics 即崩，post-fix byte-exact 绿）；`Tests/BasicTest/GcCollectWhileFdParked.md` 是路径压力哨兵。排障技巧：gdb `-ex "p $_siginfo._sifields._sigfault.si_addr"` + dump `_emperor_gc_scan_regions[0]@count`，故障地址恰等于某 region base+bytes 即此类越界。
2. **vtable/byval 传参丢 enum 载荷**（既有编译器 bug，L3 暴露）：`emit_call_virt/emit_args_with_first/emit_args_coerced` 对 >16 字节聚合参数只写 `"ptr"` 没写 `"ptr byval(type)"` → `ISink<Fifo>.write(enum带string)` 里 string 按位当指针解引用崩在 string_length。
3. **spawn_type_spec `<global>.` 前缀**：全局域 full_name 标记不是可解析命名空间，namespace 下 enum 载荷端口的 wait 报 E_RESOLVE_TYPE——须剥离再解析。
4. **catch_up_def_before_bodies 的 pass_index 污染**：pass8 内调用时子 pass 会把 def 标到 8 → 动态 body 循环 skip → `__SpawnCtx.__enter` 的 this 不注册。重置回 7（"分类前全做，body 未做"）。
5. **EP connect 汇端只支持 input 端口/MultiInput**（偏差 F）：多写者扇入/动态接线都经构造器传 ISink/Fifo 视图。
6. **端口载荷只用 string**：输出端口注入的 `_Fanout<T>` 按需特化对 enum/class 载荷未分类（E_SIZE_CYCLE），RED 哨兵 `Tests/PortTest/PortPayloadEnumChannelCycle.md`。富类型走类字段类型的显式 Fifo（pass3 fixpoint 收集）。
7. **解析器静默恢复**：`let x = ;` 这类语法错误 BP/EP 都不进语义错误表（独立编译器同样不报）→ LSP 空 diagnostics 是忠实转发，不是 LSP bug。
8. **类方法必须显式 `this` 参数**；`initial` 里 while(true) 后仍需 return（E_RETURN_MISSING）。

## 测试资产
- `Tests/LspTest/`：FdEchoChunks / FdTimerCoexist / FramerSplitFrames（Pass3，BP+Pass3）/ StdioStreamEcho / SessionLifecycle（Prebuilt）/ DocumentSymbol / GotoDefinition / Completion（Prebuilt）。
- runner Prebuilt backend：`--compilers prebuilt`；测试必须 `Compile.Args: <exe 路径>`（parse 时强制）；不在 AllCompilers（--probe 无意义）。
