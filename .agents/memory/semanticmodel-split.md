# SemanticModel 拆分重构（2026-08-16/17）

分支 `feature/semantic-model-split`（执行方式：opencode-coder 委派，opencode-go/deepseek-v4-flash，里程碑 M0-M11）。

## 结构
- 原 `SemanticModel.penguin` 11,474 行单文件 → 共享核心 ~705 行 + 每 pass 一个协作类文件（详见 CLAUDE.md bound/ 表）
- pass 类模式：`model: mut Option<SemanticModel>` 回引（破除类字段默认构造环，MetaEngine.owner_model 先例）+ 唯一 `run()` 入口；per-def 处理器保留原名供核心 `catch_up_def` 重放
- 新增 `src/bound/*.penguin` 文件必须同时加入 `EmperorPenguin.penguins` 与 `EmperorPenguinFull.penguins`（显式列表，漏加断 bootstrap）

## 语言/编译器陷阱（写 PenguinLang 代码必读）
1. **兄弟作用域同名推断类型 for 循环变量 = 编译器 bug**：BabyPenguin 编译期 `E_TYPE_INFERENCE`（SemanticModel.cs:238），EP pass1 编译过但产物段错误。规避：显式标注类型或唯一命名。哨兵用例 `Tests/FlowControlTest/ForInSiblingSameNameInferred.md`
2. **类字段默认值不能引用 this**；哑默认 + ctor 重赋值是惯例
3. **类类型字段互相引用会触发 BabyPenguin eager 默认构造无限递归**（栈溢出）——用 Option 包一层
4. **`Option<T>` 载荷取出是 !mut**：调 `mut this` 方法需先 `let m: mut SemanticModel = this.model.some;`
5. **`FunctionCallArguments` 包装类没有 `iter()`**，不可 for-in
6. 循环现代化守则：纯遍历→for-in（链表 O(n²)→O(n)）；`.set()`/迭代中 push/索引参与外层逻辑→保留手动索引

## 既有 bug（与本次重构无关，重构前即红）
- `BasicTest/NamespaceTest`（pass1）：LLVMEmitter 对 void 类型命名空间全局变量生成 `load void`
- `InterfaceTest` 7 个用例（pass1 native）：运行时段错误/乱码（InterfaceUpcastDowncast 等）
