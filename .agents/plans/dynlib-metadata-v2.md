# Dynlib 元数据 v1（结构化符号表）：export 激活 + 消费端直接构建 bound 结构

状态：Phase A/B/C 完成、阻塞项已修复（分支 `refactor/dynlib-metadata`，基于 `refactor/string-repr`）

## 实施结果（2026-09-02，2026-09-03 补充阻塞项修复）

- **Phase A 全绿**：bootstrap 收敛（pass4==pass5，exe+lib md5）、DynamicLinkTest 15/15、LspTest 23/23（含 SelfHost lib-chain）、EmperorPenguin.Tests 468/468、BabyPenguin.Tests 56/56。元数据 **2.08MB → 386KB**（76KB 模板源码 + 310KB 符号表；闭包保留 211 class/23 enum/3 iface/13 fun/8 global）。
- **Phase B 全绿**：`--libmeta=direct`（默认）注入器三阶段（①骨架注册+ns 符号 ②签名回填+预构建 vtable+resolved_return_type ③全局初始化器文本重解析绑定），按 decl-slot 拼接保持 def 顺序；pass 2/6/8 zip 感知注入区间（libmeta_is_injected/libmeta_ast_index）。对拍门：glib 迷你库与编译器 lib 消费程序 text/direct **.ll 逐字节一致**；`--libmeta=text` 为排障路径（LibMetaTextDirectParity 回归）。
- **P0 归因（A 文本路径）**：消费者编译 ~14s 中 Monomorphize ~10s（模板源重单态化占主导），decl 文本 parse ~1s，其余语义 pass ~2s，emit ~2s —— Phase B 省的是 parse+bind 的 ~1-2s；didChange 增量收益需另行立项（与计划风险表一致）。
- **关键修复**（实施中发现）：native 字符串 `>=` 比较错误编译（哨兵 StringRelationalGeNative，扫描器改用 char_code 整数）；参数 mutability 属于类型拼写（`x: mut T`，`mut x: T` 非法）；global 的文件归属走 ast_source（symbol.location 未填充）；出海源文件需全 body 闭包（walk_full）；模板文件双保险（行首扫描 + def 级 backstop）。
- **Monomorphize 守卫**：shipped-instance 命中但模板既非 lib-source 亦非 `__builtin.*` → E_DUPLICATE_SYMBOL 显式报错。
- **【2026-09-03】bootstrap 末端阻塞项修复**：pass3-exe 阶段 8 个 `E_RESOLVE_TYPE 'SourceInput'/'LibFile'` 的根因是 `SemanticMonomorphize.collect_generic_instantiations_from_ast_impl` 的 namespace 分支按**索引**对齐 `unit.definitions[i] ↔ result.definitions[i]` 取 bound namespace scope —— direct 注入在 decl-slot 位置插入 259 个无 AST 对应的 bound defs，slot 之后所有文件（main.penguin）索引整体错位，namespace emperor 的 scope 回退成 global，其函数体内裸名 `emperor.SourceInput`/`emperor.LibFile`（作为 `List<...>` 泛型实参在 pass 3 收集时解析）无法解析。修复：该分支改用与函数实例收集器一致的**按名** `scope.lookup_namespace(name)`，并删除仅为索引对齐存在的 `bound_defs` 参数（pass 2/6/8 已有注入感知重映射，pass 3 收集器当时被遗漏）。修复后 bootstrap12 收敛（exe c8fbc831…，lib 97e84b49…），原失败阶段 433 defs 0 errors。
- **【2026-09-03】RecursiveDeps（双库链 direct 模式）修复**：消费者 initial 例程体为空（exe 静默退出 0）的根因是 **docs/slots 数量错位**——`std` 库（vector.penguin 构建、无 export 标注）的 libmeta symbols 为空 → `decl_text` 为空 → 不产生 `<libdecls:std>` 伪文件/slot，但 `load_lib_recursive` 无条件 push `meta.doc_json` → docs=2 vs slots=1；`libmeta_direct_inject` 按 `slots[bi]` 取位拼接时 bi=1 越界读垃圾 slot(0) 把 mid 的 defs 插到队首，而区间记录按 slots.size()=1 配 batches[0]（std，count=0）→ 注入区间为空 → pass 2/6/8 zip 把索引 ≥ ast_count(4)+0 的 defs（含消费者 initial）当"超出 ast"跳过 → initial 永不绑定。修复：decl 伪文件与注入 doc 是同一物的两个视图——`LibMeta.has_decls` 门控（无 decl 文件则不 push doc，docs/slots 恒 1:1）+ 注入器入口 docs/slots 数量防御校验。
- **【2026-09-03】字符串/char 关系运算与 char 字面量三端修复**（复核 DLT/哨兵时发现，与 dynlib 无关但同批验证）：① native 把字符串 `< > <= >=` 降级为对两个 `_emperor_string*` 的裸指针 `icmp`（比较地址，内容无关的垃圾值；eq/ne 早已走 `_emperor_string_equal`）→ 新增 strcmp 风格 `_emperor_string_compare` 运行时助手 + LLVMEmitter 路由（同 eq/ne 模式）；② BabyPenguin VM 的 `BinCmpLt/Gt/Le/Ge` 无 `TypeEnum.String`/`Char` 分支，静默返回 false（原哨兵"baby 应绿"的假设从未成立）→ 补 `string.CompareOrdinal`/Char 分支；③ C# 后端生成 `string < string`（C# 编译不过）→ 特判为 `string.CompareOrdinal(a,b) OP 0`；④ char 字面量：VM `MakeValue` 取 token 文本首字符（=引号 39，`cast<i64>('a')`→39）→ 解引号+解转义；EP `bind_constant` 把 `'a'` 原文当 i64 字面量下发（LLVM 里出现 `add i32 0, 'a'` 非法；`'e'` 还会被 float 形状测试误判）→ 引号字面量先于 float 检测解转义为码点绑定。哨兵 StringRelationalGeNative 转绿（baby/pass1/pass3），并加 char 断言。

## 前置结论（勘察已证实）

- **直接从 JSON 构建 bound 结构：可行且推荐。** JsonValue 全 API 纯编译期可遍历（json.penguin:123-185）；编译器 lib 公共签名零默认参数值/零 fun 型参数/零变长泛型（序列化无需表达式）；AST 依赖审计：非泛型 def 仅 pass 1（作用域）/pass 2（类型）/pass 6（vtable 从 AST impl）依赖 AST，pass 4/5/7/8/9 与 IR/LLVM 层只读 bound；pass 6 用预构建 vtable 表 + `already_processed` 跳过机制（SemanticInterfaces.penguin:268）化解。
- **不做向后兼容**：新格式 `emperor-libmeta v1` 取代旧 files[] 源码内嵌，旧 reader 代码删除。
- 两阶段落地：A（格式+序列化器+表→声明文本物化）→ B（直接反序列化 tables→bound）。A 的格式与序列化器在 B 全部复用。

## 元数据格式（结构化符号表，C#-metadata 风格）

```json
{
  "format": "emperor-libmeta", "version": 1,
  "name": "...", "deps": ["<lib 名>"], "instances": ["<mangled full_name>", ...],
  "symbols": [
    {"kind":"namespace","name":"std"},
    {"kind":"class","name":"BoundType","ns":"bound",
     "fields":[{"name":"kind","type":<T>,"mut":false}],
     "methods":[{"name":"display_name","this_mut":true,"params":[...],"ret":<T>}],
     "impls":[{"iface":<T>,"vtable":["<method full_name>", ...]}]},
    {"kind":"fun","name":"compile_sources","ns":"emperor","params":[...],"ret":<T>},
    {"kind":"enum","name":"BoundDefinition","ns":"bound",
     "members":[{"name":"class_def","payload":<T>|null,"value":3}],
     "impls":[...], "vtables":[...]},
    {"kind":"interface","name":"ICopy","ns":"__builtin","tparams":["T"],
     "methods":[{"name":"copy","params":[...],"ret":<T>}]},
    {"kind":"alias","name":"...","target":<T>},
    {"kind":"source","name":"std.Vector","text":"<#template def 原文>"},
    {"kind":"global","name":"verbose_level","ns":"_utils","type":<T>,"init":"<初始化表达式原文>"}
  ]
}
```

- **类型编码 `<T>` 结构化**：`{"base":"<full_name>","args":[<T>...],"mut":...}` 递归——不用 display_name（带可变性前缀，BoundType.penguin:777 明言不可作身份键）。原语用注册表名。
- **vtable 预构建**（发布方 pass 6 产物）：`impls[].vtable` 按槽位序列出 method full_name；消费端直接采纳 + pass_index 盖章跳过 pass 6。分类（pass 7）消费端 bound-only 确定性重算。
- **`kind:"source"`**：模板（任何 `#template`，含值模板）、`#fun`/`#class`、`#specializing` 所在 def——整 def 原文出海（build_text 逐 def 抽取）。编译器 lib 仅 json/hashmap/array/vector/utils 五文件命中（~1600 行）。
- **globals**：初始化表达式原文小片段出海（exe 端 GOT interposition 重放需要；数量极少）。
- **shipped instances**：具体符号条目出海（字段/签名具体类型 + mangled full_name）——消费端直接采纳，不再"重实例化再声明"。

## 内容裁剪（export 激活）

- 保留集 = `export`（namespace 上的 export 级联成员）∪ 闭包 ∪ globals ∪ 模板/meta source 条目。
- 闭包（保守过近似，构建期从 bound 结果算，迭代至不动点）：① 保留 def 签名/字段/impl/enum payload 引用的类型；② 模板/meta 函数体与字段初始化器引用的符号；③ 接口 impl 边。漏报 = 消费端干净 E_RESOLVE_SYMBOL + 链接期 undefined-symbol 兜底。
- `is_exported` 仅 lib 构建过滤期消费，普通编译零影响。
- **守卫**：Monomorphize 命中 shipped-instance 名却无模板条目 → 显式编译错误（今天静默产坏 IR）。

## Phase A — 格式 + 序列化器 + 文本物化消费

1. `std/penguin/dynlib.penguin`：`serialize_symbols(BoundCompilationUnit) -> string`；`read_meta` 读符号表 → 表→声明文本生成器（bodyless `fun ...;`，不触发 extern→libc 映射）+ source 条目拼伪文件 → 既有管线照常（is_lib、lib_def 区间、check_lib_redefinition 兼容）。
2. main.penguin lib 构建切换新序列化器；export 过滤 + 闭包。
3. 编译器 lib 标注 export（main.penguin + LSP 引用面为种子，bootstrap 收敛 + LspTest 绿迭代）。
4. 验证门：bootstrap 收敛（pass4==pass5 含 lib md5）、DynamicLinkTest 12 例、LspTest 全绿；记录元数据体积（预期 2.08MB → <0.3MB）。

## Phase B — 直接反序列化（tables→bound）

1. `EmperorPenguinCompiler` 增加 `prebuilt_lib_symbols` 注入，反序列两阶段：①骨架注册（BoundScope 树片段 + BoundTypeSymbol + def 进 definitions 前段，is_lib_export 预置）；②签名回填（结构化 `<T>` → registry + scope 解析）；③vtable 采纳 + pass_index 盖章（跳过 1/2/6 区间，4/5/7 照跑）。
2. shipped instances 采纳 + Monomorphize 对接（新鲜实例命中 shipped 名 → 标记 lib-export，行为不变）。
3. `--libmeta=text` 调试开关（强制走 A 文本路径，排障对拍）。
4. 验证门：同 A + 对拍门（text 与 direct 编译同一消费程序，.ll 逐字节一致）。

## Phase C — 测量与收尾

- P0 归因（实施第一步）：LSP didChange 重编译采样（read_meta/parse/BuildScopes/Monomorphize/Interfaces 分布），A/B 各复测。
- 新 md 回归：`LibMetaDeclOnly`、`LibTemplateSourceShipped`、`LibVtableFromTable`。
- 文档：格式规范入 Documentation + AGENTS.md dynlib 段；按 Phase 提交。

## 风险与对策

| 风险 | 对策 |
|---|---|
| 反序列化器与 bound 模型漂移 | 自举收敛守门 + `--libmeta=text` 对拍 + 直接采纳发布方 vtable |
| 类型名往返失真 | `<T>` 结构化编码（base+args 递归） |
| 闭包漏标 | 干净编译错误 + 链接兜底；保守过近似 |
| shipped instance 无模板 → 坏 IR | 显式错误守卫 |
| LSP 收益不达预期（stdlib 重绑/单态化占大头时） | Phase C 实测；didChange 增量另行立项 |

**范围外**（独立成案）：JIT unit B base sources（~4300 行）改从 lib 已编译符号解析。
