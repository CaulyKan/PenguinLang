# IStringOps：primitive string 的标准字符串方法面（2026-09-14）

分支 `feature/istring-ops`。提交 9d8b9577（BabyPenguin 双后端 + 镜像 core_builtin + 3 个测试）。

## 交付物

- `__builtin.IStringOps` 接口（29 个方法）+ `StringSplitIterator`（惰性 split 迭代器）+ `impl IStringOps for string`，**同一份文本手工镜像**在 `BabyPenguin/Builtin.penguin`（string extern 之后）和 `EmperorPenguin/std/penguin/core_builtin.penguin`（同位置）——改一处必须同步另一处。
- 全部纯 PenguinLang 实现于 11 个 `string_*` extern 之上（无新 C/VM 内置）；大小写用字母表 find 映射（无 code→char 内置，且两运行时字节级一致）；trim 空白集 `" \t\n\r"`。
- 语义约定：`char_at` 越界 → `""`；`find_last("")`/`count("")` → -1/0（空针定义不明，统一按"未找到"）；`replace(from="")` → 原串；`repeat(n<=0)` → `""`；pad 的 `ch` 取首单元、空则空格；split 尾部分隔符产出一个空 piece（Python 式），空 `sep` 整串产出一次。
- 单位语义：native 是字节、BabyPenguin VM 是 UTF-16 单元（ASCII 下一致）——跨编译器字节精确测试只用 ASCII。

## BabyPenguin：首个带方法体的 primitive 接口 impl（踩坑记录）

- **BasicTypeNode 不在 Namespaces 语义树里**（`05_InterfaceImplementation` 特意 concat 它们），`FindAll` 看不见挂在 string 类型节点上的 VTable → 全局 pass 漏处理其函数。新增 `SemanticModel.FindAllIncludingBasicTypeVTables()`，在 **07_CodeGeneration、09_CheckReturnValue、IRGenerator.GenerateAllFunctions、BabyPenguinVM.BuildCodeContainerIndex** 四处替换（漏 BuildCodeContainerIndex 时症状是运行时 `No code container found for 'string_vtable-__builtin-IStringOps_length'`）。
- **接口/impl 方法的 `this` 必须显式写 `this: string`**：`03_SymbolElaborate.cs:204` 直接取 `param.TypeSpecifier!.Name`，无类型标注的 `this` 会 NRE（EP 侧 `fun hash(this)` 无标注能过是因为 EP 前端自己推断；显式标注两边都兼容）。
- **C# 后端（--backend=cs）**：可达性 implSeeds 要补 basic-type vtable 的非 extern slot；`__InitVtables` 用 `emitter.CsType(basicName)` 注册到 CLR 类型（string→typeof(string)），InvokeVirtual 按 `obj.GetType()` 命中。**ICopy 的 slot 是 extern 接口函数，不能注册**（方法不存在，GetMethod→null→init 崩），靠 FunctionLowerer 既有的 ICopy 特例分发。
- 顺手修了 `ExternLowerer.string_char_at` 越界崩溃（原来直接索引，VM/native 都返回 ""）。
- 测试跑器用 **Release** 目录的 BabyPenguin.dll——改 Builtin.penguin 后必须 `dotnet build -c Release` 刷新，否则假失败 `Cant resolve symbol 'length'`。

## EmperorPenguin 侧（零编译器改动，IHash 先例）

- `bind_impl_for_def` 把方法改名 `<name>$$string` 注册进 primitive_impl_* registry；`s.method()` 经 `bind_member_access` 的 primitive 路径直接调用；LLVMEmitter 按 `__builtin.length$$string` 发射（`$` 在 LLVM 标识符合法）。
- **不要对 string 做接口类型化使用**：`cast<IStringOps>(s)`/`s is IStringOps` 不支持（`_emperor_string_metadata` interface_count=0，emit_box 无 primitive 路径）——直接方法调用才是契约。

## 测试

- `Tests/StringTest/IStringOps{Query,Transform,CompareSplit}.md`：Apply To = 全部五编译器（BabyPenguin、BabyPenguin CS、Pass1/2/3），15/15 验证通过。
- LSP `Tests/LspTest/Completion.md` 黄金文件已随 core_builtin 新符号更新（+32 项：IStringOps、29 方法、StringSplitIterator、source/sep 字段，插在 `string_to_double` 与 `Result` 之间；`new`/`next`/`pos`/`done` 按名去重不新增）。注意 ESCAPE 语义是 C 单趟反转义（`\\n`→`\n` 保留 JSON 转义），手工重放时别把 JSON 内部 `\n` 也转成真实换行。

## 已知问题：IStringOps 暴露了 pass3 代际 GC 的潜在损坏（未修，独立课题）

**症状**：新 core_builtin 下 `pass3` 编译 json+hashmap+vector 的 meta 测试（MetaJsonContainers / MetaJsonVectorOfSerializable）约 10-30% 概率失败——GC abort `CORRUPT REF-MAP (index out of bounds) ... type <乱码> node=1`（exit 134）或段错误（139），偶发以语义错误面貌出现（`error[E_RETURN_TYPE_MISMATCH]: expected <error> but got void in next at hashmap.penguin:266`——绑定期读到已损坏状态）。

**取证链**（2026-09-14，coredumpctl 抓到 SIGSEGV core，gdb post-mortem）：
- 崩溃栈：`BindMetaCallsPass.try_splice_meta_fun_call → MetaEngine_compile_meta_function → LLVMEmitter_emit_function → _emperor_gc_poll → gc_collect_generational → gc_scan_regions_mark → _emperor_gc_mark_object → gc_mark_drain (gc.c:1666 读 meta->refmap)`。
- 损坏对象 `obj=0x…d58` 的 body 首字 = **0x51（标量）**，不是 metadata 指针——GC 把值布局堆块当 metadata 对象走查（typing/reuse 漏洞）。
- 开关矩阵：`EMPEROR_GC_DISABLE=1` 干净；`EMPEROR_GC_NOGEN=1` 干净（10/10，锁定代际 evacuate/promote 路径）；`GC_VERIFY=1`、极小/极大 `EMPEROR_GC_YOUNG`、gdb attach 均因时序扰动而干净（Heisenbug）；tcache 关闭仍失败；`EMPEROR_GC_REGION_PINS=1` 6/6 干净（样本不足）。
- 内容变体：旧 core_builtin 8/8、旧+345 行死代码 12/12、29 个一行 $$方法 10/10、镜像签名简单体 10/10、3 个复杂体 $$方法 10/10、完整 IStringOps ~10-30% 失败——**非语义因果，是分配形态/布局重掷骰子**；同一算法内容放普通函数干净。
- 结论：预先存在的 gc.c（或 emitter pin 侧）潜在漏洞，IStringOps 只是金丝雀。**修复属独立课题**：方向——typed scan region 的 elem_map walk / promote 期间块 typing、扫描区注销时序（gc.c 注释里的 "missed StringBuilder-data edge family" 同族）。复现命令：循环 `build/bootstrap/pass3 EmperorPenguin/std/penguin/{json,hashmap,vector}.penguin build/testruns/<run>/pass3/StdlibTest/MetaJsonContainers/source.penguin -o /tmp/t.exe`。
- gc.c 改动会直接进所有自举产物（需 make bootstrap 重链）；调查时 `EMPEROR_GC_NOGEN=1` 可作临时规避。
