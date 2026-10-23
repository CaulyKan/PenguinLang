# 编译器热点修复（pass2 perf profile 结论落地）

分支：`feature/compiler-perf-hotspots`（自 feature 分支点起，逐项提交）

背景：pass2 perf profile（2026-10-20，36K 样本/35s）四大热点：
1. `find_function_def_in_defs` 13.3%（try_infer_generic_call 每次泛型推断全树线性扫描）
2. `List.at` Option 装箱 ~9%（每次元素访问一次 GC 堆分配，同时推高 GC 至 23%）
3. `LLVMEmitter.find_string_lit` 2.7%（字符串字面量查重线性扫 → O(n²)）
4. `Lexer.match_keyword` 5.2%（每标识符位置串行试 62 个关键字）

## 修复设计

### 1. full_name → def 哈希索引（SemanticBindExpressions + BoundCompilationUnit）
- `BoundCompilationUnit` 加字段：`function_def_index: StrMap`（full_name → flat 下标）、
  `function_defs_flat: List<BoundDefinition>`、`function_defs_indexed_upto: i64`。
- `BindExpressionsPass.find_function_def_fast(unit, name)`：首次全量前序树遍历建索引
  （first-wins，与原线性扫描语义一致）；此后每次调用前把 `[upto, definitions.size())`
  的**顶层**新 def（monomorphize/catch_up 特化 push 进来的，不嵌套）增量注册。
  索引 miss = 确定不存在，不再线性兜底（原版 miss 也要全扫）。
- `try_infer_generic_call`（:2163）与 `find_function_return_type`（:4731）改走 fast 版；
  原两个线性函数若无其余调用者则删除。

### 2. find_string_lit 哈希查重（LLVMEmitter）
- 加 `string_lit_index: StrMap`（value → string_lits 下标）；两处 push 点（:1024/:1045）
  同步 set；find 改索引查询。push 顺序不变 → .ll 输出不变。

### 3. `List.at_unchecked`（utils.penguin）
- 加裸访问：无界检、无 Option 装箱（`__builtin.__array_get` 直取）。
- 热路径内部循环替换：StrMap 的 get/set、本次触及的收集循环。
- 不做大面积调用方替换（风险控制）。

### 4. match_keyword 首字符守卫（Lexer.tokenize）
- 62 个 match_keyword if 链外包 `starts_any_keyword(当前字符)` 守卫（switch 首字符集合）。
  非关键字首字符位置一次比较跳过整链。守卫集合失同步会被 bootstrap 收敛 + 测试套件当场抓住。

## 验证
1. 每步后 pass1 发射冒烟（BabyPenguin --backend=cs 编译 EP 全源）。
2. 全部完成：`make bootstrap`（收敛 = pass4 md5 == pass5 md5）。
3. **输出一致性**：新 pass2 产出的 pass3.ll 与改动前 build/bootstrap/pass3.ll 逐字节比对
   （优化语义等价的铁证）。
4. `make test`（babypenguin 快回路 + 全矩阵抽查）、`dotnet test`。
5. 性能复测：新 pass2 单轮编译计时 vs 37s 基线 + perf 复采样对比。

预期：三轮大编译（pass2/pass3-lib/pass4-lib）每轮省 5~8s，全 bootstrap 省 15~25s；
release 编译器同步受益。
