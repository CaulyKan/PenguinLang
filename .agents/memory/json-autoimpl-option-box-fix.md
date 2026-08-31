# json auto-impl Option/Box 字段修复 + 限定泛型静态调用（2026-08-31/09-01）

分支 `feature/lsp-project-config`。用户报告：`#impl_json_serializable()` 类含 Option/Box 字段编译失败。

## 根因
- 生成的 deserialize 对 Option/Box 字段读 `__builtin.Option<i64>.json_deserialize(...)`（`json_type_spelling` 输出**限定**拼写，enum def 的 `name` 是限定名 "__builtin.Option"）。
- `SemanticBindExpressions.bind_member_access` 的 namespace 分支（含链式 namespace 分支）用 `ns_scope.lookup_symbol(member)` 解析 `Option`，**丢弃 `expr.generic_args`** → base 绑定为裸模板类型（display "Option"，无参数）。
- 后续 `.json_deserialize` 成员查找：模板 scope 无注入 impl、`resolve_specialized_container_def` 因 generic_args==0 直接返回模板 def、模板 vtables 为空 → `E_RESOLVE_SYMBOL: Type 'Option' has no member 'json_deserialize'`。
- 裸名拼写 `Option<i64>.json_deserialize(...)` 走 `bind_identifier` 的 generic-type 分支（mangle→`Option__i64`→查特化符号）所以一直正常。

## 修复
`bind_namespaced_generic_type_member`（SemanticBindExpressions.penguin，`lookup_specialized_scope_member` 旁）：namespace 成员带 generic args 时解析参数→从模板 def full_name mangle→优先特化类型符号→否则回退 `with_generic_args` 的模板+参数类型。接入单级+链式两个 namespace 分支。与 `bind_identifier` ~L1310 分支同契约。

## 测试（4 个新 md）
- `StdlibTest/MetaJsonOptionBoxFieldAutoImpl.md`（pass2+3，回归锁；注意 auto-impl 类需要**无参构造**——生成的 `new Cls()`）
- `MetaProgramming/MetaGenericStaticCallQualified.md`（pass2+3，纯语言：`__builtin.Box<i64>.tag()` 限定拼写）
- `InterfaceTest/ImplNoForAtFileLevel.md`（**红 sentinel**：文件级无 `for` 的 impl 被 EmperorPenguin 静默接受并丢弃——.ll 只有悬空 call 无 define，链接才炸；BabyPenguin 参考文法 parse 就报 `expecting 'for'`。独立既有 bug，与 Option/Box 无关）
- `BuiltinTest/PrimitiveToStringNotDefined.md`（负向守卫：`i64.to_string()` 不存在，EP 报 E_INTERNAL 而非 E_RESOLVE_SYMBOL——诊断质量问题，行为一致）

## 调查方法备忘
- pass3 只发 `.ll`，"编译通过"≠能跑：hand 展开"通过"实为静默坏代码，必须 `./EmperorPenguin/emperor link x.ll -o x.exe && ./x.exe` 验证。
- `-vvv` trace 里 unit B（meta JIT，`using emperor`+编译器自身源）与主单元的 pass banner 交错；错误归属看最后 `Done: N definitions`。
- json 调试可拷 json.penguin 加 `__builtin.eprintln` 打印生成文本。
- pass1 无 meta JIT（`penguin_jit_create failed`），json.penguin 只能 pass2/3 编译。
- auto-impl 缺 key 的字段保持默认构造值（Option 零值是 some(0) 不是 none）——已知语义，未处理。
