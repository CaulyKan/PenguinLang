# SemanticModel 拆分重构计划（2026-08-16）

分支：`feature/semantic-model-split`。执行方式：opencode-coder 委派（opencode-go/deepseek-v4-flash），每里程碑 审查+验证+commit。

## 决策（已确认）
- 每 pass 一个协作类（`model: mut SemanticModel` 字段 + `run(mut this, unit, result)` 唯一入口），pass 类实例由 SemanticModel 持有
- Pass 8 拆 3 个文件：Bodies（入口+语句）/ Expressions（表达式族）/ MetaCalls（meta 调用）

## 目标文件（src/bound/，全部 namespace emperor）
| 文件 | 类 | 来源（原 SemanticModel.penguin 行号） |
|---|---|---|
| SemanticModel.penguin | SemanticModel 核心 ~900 行 | 字段、ctor、bind()、catch_up_def(移入)、report_*、resolve_type_specifier 家族(3244-3468)、共享查找、trace() |
| SemanticShared.penguin | 自由函数+数据类 | 3-185（FunctionInstantiation、SpecializingBlockInfo、file_ns_name、set_def_source_file、ast_def_filename、top_level_def_scope、def_lookup_scope） |
| SemanticMetaRewrite.penguin | MetaRewriter | 355-1064 + seed_meta_engine(9263) |
| SemanticBuildScopes.penguin | BuildScopesPass | pass 1: 1120-2113 |
| SemanticResolveTypes.penguin | ResolveTypesPass | pass 2: 2338-3242 |
| SemanticMonomorphize.penguin | MonomorphizePass | pass 3: 3469-5032 + 尾部 10902-11471 |
| SemanticBindSymbols.penguin | BindSymbolsPass | pass 4: 2181-2337 |
| SemanticConstructors.penguin | ConstructorsPass | pass 5: 5033-5183 |
| SemanticInterfaces.penguin | InterfacesPass | pass 6: 5184-5781 |
| SemanticClassifyValueTypes.penguin | ClassifyValueTypesPass | pass 7: 5782-6257 |
| SemanticBindBodies.penguin | BindBodiesPass | pass 8a: 6258-6842 + 10032-10607 |
| SemanticBindExpressions.penguin | BindExpressionsPass | pass 8b: 6843-10031（表达式族） |
| SemanticBindMetaCalls.penguin | BindMetaCallsPass | pass 8c: 8414-8974 + trampoline 9143-9253 |
| SemanticValidateControlFlow.penguin | ValidateControlFlowPass | pass 9: 10608-10901 |

**每新增文件同步加入 EmperorPenguin.penguins 与 EmperorPenguinFull.penguins（显式列表）。**

## 关键耦合（语义逐字保留）
- catch_up_def(3608) 重放 pass 4-8 per-def 处理器 → 处理器保留原名与签名
- current_pass_index 水位线守卫：11 处（9 处 `>=`，2 处 `>`@5222/5810）→ M0 引入 already_processed/processed_beyond
- current_unit：pass 8 设置，pass 3 JIT 路径读取
- 外部消费者：EmperorPenguinCompiler.compile_sources 与 MetaHost.active_model（report_*、ensure_specialized_type）——API 不变

## 去重清单（M9）
低：删别名 mangle_generic_name_for_ir(4215)/is_stmt_condition_literal_true(10897)/find_ast_def_by_name_from_scope(4210)；合并 get_field_mutability(10859 vs 7274)；substitute_generic_args 分支合并(2152)；7 个线性查找收敛。
中：pass3 三连块(3650-3693)、bind_body_for_specialized_def 四连块(6287-6328)、class/enum 镜像对(4123/4143, 5363/5398)、eval_meta_arg_i64(3217) vs bind_meta_arg_value(8631)、meta splicer 三对、generic-arg 解析器 4 变体(4639/4664/9847/9184)。
高(stretch 可放弃)：两套 AST walker(4385-4688 vs 10907-11160) 统一。
不改：set_def_source_file/ast_def_filename 20 分支（无反射，相邻放置+注释）。

## := / for 现代化（随迁移同步）
- 纯遍历 → `for (let item in list)`；需索引 → `for (let i : i64 in range(0, n))`
- Option 检查 → `if (let x := opt.some)`
- 排除：迭代中改集合、索引参与外层算术、需 else 语义

## -vvv（M10）
核心 trace()（verbose>=3）；v1 加 pass 后 def/错误计数；v3：monomorphize 轮次/实例化/去重/特化/catch_up、vtable 映射、作用域查找失败、隐式 cast、meta splice。保留 trace_enter。

## 里程碑验证
每步 `dotnet test EmperorPenguin.Tests | tee /tmp/test.log`；M3/M7/M8 后加 `--compilers babypenguin,pass1` 抽查；M11 `./penguin -b` + 全量矩阵 `--baseline` + 更新 AGENTS.md bound/ 表。
