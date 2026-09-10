# String 表示重构：`metaptr + length + data + '\0'` 头前缀结构

状态：已完成（分支 `refactor/string-repr`）。bootstrap 一次收敛（pass4==pass5）；全量矩阵 1365 PASS / 1 FAIL / 0 ERROR（唯一失败为 LspTest/ProjectDiscovery 在 27 路并行重载下的一次偶发 `spawn object does not implement __ICoroutineEntry`——隔离与 4 轮 LSP 全并行重跑均绿，与本重构无因果路径：协程 spawn/对象 metadata 路径未动，string 对 GC 不透明；属分支自带的协程/LSP 代码在内存压力下的预存竞态，待后续观察）；dotnet 测试 468+56 全绿；C 运行时过 ASan 单元测试；回归测试 `Tests/StringTest/StringHeaderOps.md` + `Tests/NamespaceTest/LibcExternStringArg.md` 已落库。

## 背景与现状（已勘察确认）

- 现在 string 是裸 `char*`（NUL 结尾，无长度字段），字面量是 `[N x i8] c"...\\00"` 全局常量，运行期串是 `_emperor_gc_alloc(len+1, 1)` 的不透明块。`string_length` 就是 `strlen`——GC/lexer/json 三次 O(n²) 事故（含 LSP 卡死第一层）的根源。
- **只有一个 .ll 发射器**：`--backend=cs` 是把 EmperorPenguin 编译器转 C# 进程内运行，pass2.ll 仍由 `LLVMEmitter.penguin` 自己发出。**BabyPenguin（VM 用 C# string）零改动**。
- `Documentation/23_EmperorPenguinLLVM.md` §2.4 已写明目标布局（`ptr metadata / i64 length / data[]`）——本重构让文档成为事实。
- GC：`_emperor_gc_alloc(size, is_string=1)` 的块**从不扫描、从不 finalize**（gc.c:375/428），头前缀里的 metaptr 对 GC 完全不可见，天然安全。
- 现存裸 libc extern 仅 `abs(i32)`（Tests/NamespaceTest/GlobalExternLibcTest.md，无 string 跨界）；所有 stdlib string extern 都映射到我们自己的 C 函数（`__builtin.*`→`_emperor_*`，`std.io.*`→`std_io_*`）。
- **不可变性已确认**（与 C# 同语义）：string 是 primitive，无成员字段，IR 无写路径；全部 string 内建每次分配新串；赋值/传参/值类 memcpy 共享指针（不可变所以安全）；`==` 是内容比较。唯一实现层"作弊"：StringBuilder 的 `data: string` 字段是 C 侧原地增长的缓冲（`append` 原地 memcpy），用户可见串都经 `to_string()` 复制，stdlib 内仅 `get_unique_name()` 瞬态直读 `data`（core_builtin.penguin:403-422）。

## 目标内存布局

```
string 值 = ptr ──► ┌──────────────┬──────────────┬────────────────┬─────┐
                    │ metaptr (8B) │ length (8B)  │ data[length]   │ '\0'│
                    └──────────────┴──────────────┴────────────────┴─────┘
                     指向 _emperor_string_metadata    ▲ to_cstring = +16 (=128bit)
                                                        from_cstring = -16
```

## Phase 1 — C 运行时（主体工作量）

**新头文件 `EmperorPenguin/std/include/emperor_string.h`**：

```c
typedef struct _emperor_string {
    void*   metaptr;   /* -> _emperor_string_metadata（见下） */
    int64_t length;    /* 字节长度，不含结尾 NUL */
    char    data[];    /* length 字节 + data[length] = '\0'（C 互操作便利） */
} _emperor_string;

static inline char* _emperor_string_to_cstring(_emperor_string* s)
    { return (char*)s + 16; }
static inline _emperor_string* _emperor_string_from_cstring(char* c)
    { return (_emperor_string*)(c - 16); }
```

- `_emperor_string* _emperor_string_alloc(int64_t len)`：`_emperor_gc_alloc(16+len+1, 1)`，盖 metaptr 戳、写 length、`data[len]='\0'`。**保持 `is_string=1`**（对 GC 不透明，metaptr 永不被解引用）。
- `_emperor_string* _emperor_string_adopt_cstring(const char* c)`：对外来 C 串（libc 返回值）strlen + alloc + memcpy——`from_cstring` 只对"已知是 `_emperor_string` 的 data 指针"合法，外来串必须走 adopt。
- `_emperor_string_metadata`：一个 `EmperorClassMetadata` 全局（`name="string"`, `interface_count=0`, 无 vtable/dispose），定义在 core_builtin.c、随 exe `-rdynamic` 导出。副作用收益：`x is IFace` 误用于 string 从 UB 变成干净的 `false`。
- GC 侧无需改动（is_string=1 路径不变）；对齐：GC user 指针 8 对齐，struct 只需 8。

**改写全部 string 相关 C 函数**（签名 `const char*` → `_emperor_string*`，返回值同理；产出全部走 `_emperor_string_alloc`，读取全部 length 驱动）：

| 组 | 函数（core_builtin.c 行号） | 要点 |
|---|---|---|
| string 助手 | concat:191, equal:212, length:247, find:252, find_from:259, substring:268, char_at:287, char_code:300, starts_with_at:310, char_code_at:321, slice:332, to_int:346, to_double:351 | equal 先比 length 再 memcmp；find 用 memmem；char_code_at 越界(≥length)返回 -1 |
| 转换 | int/i64/bool/double_to_string :173/:182/:218/:226 | snprintf 进 data（天然有界） |
| std_io_* | :922–1053 + io_read_line/all_stream :874/:901 | 参数 `_emperor_string*`，fopen/remove 等 libc 边界内部 `to_cstring`；读结果 alloc 构造 |
| StringBuilder | :1076–1130 | 内部缓冲区本身改为 `_emperor_string` 分配（append 时同步 header 的 length 字段 + NUL，容量=分配大小，满则重分配）；**PenguinLang 类字段布局不变**（`data: mut string; len: i32; cap: i32`）；to_string 复制 [0,len) 新串。保住 `get_unique_name` 直读 `data` 的路径 |
| print 族 | :82–104 | `fwrite(data, 1, length, 流)`（内嵌 NUL 安全） |
| JIT 包装 | meta_stubs.c:27/:34/:43, penguin_jit.cpp:87/:132/:154 | add_module/lookup 参数 `_emperor_string*`（StringRef(data,length)）；**get_error 返回 adopt 拷贝**（原返回 `last_error.c_str()` 是临时 C 串） |
| 其它 | args_get:381, getenv:408(adopt), exec_cmd:398, file_read/write_text:421/:445, read/write_fd:489/:519, file_size:557, file_read_range:566, exe_path:595, mkdir:617, create_temp_dir:636, dir_get_entries:729 | file_read_range 现在能正确携带内嵌 NUL |
| scheduler.c | throw_runtime_error:789, throw_get_msg:814 | msg 为 `_emperor_string*`，静态槽存指针（GC root 不变），fprintf 改 fwrite(len) |
| 头文件 | emperor_builtin.h 全部 string 原型 | 同步签名 |

**有界化审计**（strcpy→长度驱动）：
- `strcpy`:221（改 memcpy 字面量定长）；
- concat/substring/args_get/getenv/create_temp_dir(:652/:674/:692)/win pattern(:745)/dir_get_entries(:798/:850)/StringBuilder_append(:1088-1101) 等 memcpy 全部改 length 驱动；
- io_read_line/all_stream(:891/:913) 已 length 跟踪；现有 snprintf 已有界保留；
- `_emperor_write_fd`:525/:541、`_emperor_file_write_text` 等按 length 写。
- `penguinlang_interop.c` 的 interface_id/class_id 参数**保持 `const char*`**（那是 type-id 内部常量 `[N x i8]`，不是 Penguin string）。

## Phase 2 — LLVMEmitter.penguin（.ll 可见变更仅两处）

1. **字面量全局**（`emit_global_strings` :711-727）：
   ```
   @str_N = private constant { ptr, i64, [len+1 x i8] }
       { ptr @_emperor_string_metadata, i64 <len>, c"...\\00" }
   ```
   并在存在字面量时发一行 `@_emperor_string_metadata = external global i8`（链接期由 libcore_builtin.a/exe 解析；JIT unit B 经进程符号表解析——`-rdynamic` 已保证）。字面量去重、`emit_const`(:2697)/`emit_main`(:1235-1242) 引用方式不变（值 = `ptr @str_N`，现在指向 header）。
2. **裸 libc extern 编组**（前瞻性，防回归）：`llvm_func_name`(:5448) 判定"裸顶层 extern"（不在 `__builtin/_utils` extern map(:574-605)、原名无命名空间点）时，string 实参在 `emit_args_coerced`(:4742) 发 `getelementptr i8, ptr %X, i64 16`（+16），string 返回值包一层 `call ptr @_emperor_string_adopt_cstring(...)`。我们的 C 函数（`_emperor_*`/`std_io_*`/命名空间 extern）一律原生 `_emperor_string*` ABI，不编组。
3. BINOP/CAST/ISINSTANCE/BOX/全局变量/root 注册：**零改动**（LLVM 层都是 `ptr` 进出，语义变化全部在 C 侧内部）。值类 memcpy 共享 string 指针——不可变性已确认，安全。
4. 预期 golden 冲击 ≈ 0（EmperorPenguin.Tests/LLVMTest.cs 38 例中无 `@str_` 字面量断言；`@_emperor_string_concat(ptr,ptr)` 等 declare 文本不变）。`string_length` v1 保持 extern 调用（C 侧已是 O(1) 读 length）；内联 `load i64, +8` 列为后续优化不进本次。

## Phase 3 — 原子重建 bootstrap 链

`make bootstrap`：pass2.ll 由 cs 后端跑**新** LLVMEmitter 对**新** C runtime 发出——原子性由构造保证。迭代修到 **pass4==pass5 md5 收敛**。注意 `.penguin-lib` 消费者与 exe 必须同批重建（混用新旧 ABI = 崩溃），`make lsp`/`make publish` 一并跑。

## Phase 4 — 验证

- 全量 `make test`（md 用例 × 适用编译器）+ `dotnet test`（EmperorPenguin.Tests/BabyPenguin.Tests）+ LspTest 套件。
- 新增回归测试：
  - `Tests/StringTest/StringHeaderOps.md`：StringBuilder 循环构建 ~100KB 串，断言 length/substring/char_code_at/equal/concat 精确输出（守住 header 偏移算术，全编译器）。
  - `Tests/NamespaceTest/LibcExternStringArg.md`：`extern fun strlen(s: string) -> i64;` 断言 `strlen("hello")==5`（守住 +16 编组；Pass2/Pass3）。
- 性能对比：bootstrap 墙钟 + summary.html 的 RSS/时长基线对比（预期 string_length/find/equal 全 O(1)/O(len) 后编译器自身提速；小串堆占用 +16B/串，观察 GC 压力）。

## Phase 5 — 文档与提交

- `Documentation/23_EmperorPenguinLLVM.md` §2.4 校准为实际命名（`@_emperor_string_metadata`、`@str_N` 结构）；`03_DataTypes.md` :20/:25/:32 的值/引用语义矛盾顺手澄清（string：指针传递、不可变、内容共享，与 C# 同）。
- 分支 `refactor/string-repr`，按 Phase 里程碑提交。

## 风险与对策

| 风险 | 对策 |
|---|---|
| 小串 +16B → 编译器自身 GC 压力/堆增长 | GC 阈值自适应（2x live）；Phase 4 实测 bootstrap 时间与 RSS，若回归再评估 |
| 混用新旧 ABI（部署副本/lib 与 exe 不同批） | bootstrap/lsp/publish 全链同批重建；vscode 部署对同步更新 |
| 自举收敛失败 | 表示在 .ll 中确定（结构体字面量），无非确定性来源；逐 pass 对比 .ll 定位 |
| JIT unit B 外部符号 | `_emperor_string_metadata` 经 `-rdynamic` 进程导出，ORC DynamicLibrarySearchGenerator 可解析（与现有 `_emperor_*` 同路径） |
| 内嵌 NUL 行为变化 | file_read_range 等现在带正确 length——DynlibStub 的 footer 扫描只依赖尾部，不受影响；全量测试兜底 |
| StringBuilder `get_unique_name` 直读 `data` | 缓冲始终保持合法 `_emperor_string`（header 同步），读取路径无需改 |
