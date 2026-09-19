---
name: penguinlang-doc
description: 编写、更新或翻译 PenguinLang 文档（docs/ 双语站、README、教程/规范/实现笔记）时使用。Use when the user asks to write, update, translate, or review PenguinLang documentation — triggers include "写文档", "新增文档页", "翻译文档", "写教程/规范/实现笔记", "update docs", 以及任何要改 docs/** 或 README*.md 的任务。涵盖：文档结构、mdBook 生成流程、双语翻译规则、三类文档的写作规范。
---

# PenguinLang 文档编写技能

TRIGGER when: 用户要求编写、更新、翻译或评审 PenguinLang 文档——包括 `docs/**` 下的任何页面、根目录 `README.md` / `README.zh-CN.md`，以及"给某功能补文档"这类请求。

DO NOT TRIGGER when: 任务只是写 PenguinLang 代码或改编译器（用 `penguinang-coding`），文档只是顺带提一句。

---

## 1. 文档结构

仓库里是两棵**完全平行**的文档树，文件名与相对路径逐一同名，每个页面在两棵树里一一对应（站点的语言切换就是 URL 的 `/zh/` 前缀互换，靠这种同名映射成立）：

```
docs/
├── lang-switcher.js          # 站点工具栏语言切换脚本（共享，一般不动）
├── en/                       # 英文书：book.toml 的 src → build/book
│   ├── SUMMARY.md            # 英文侧边栏
│   ├── tutorials/            # 教程
│   ├── specifications/       # 规范：01_Overview.md … 11_PortsChannelsEvents.md（两位数前缀命名）
│   ├── impl-notes/           # 实现笔记：20_EmperorPenguinAST.md 起（两位数前缀命名）
│   └── test-report.md        # 跳转到 /test-report/ 的占位页
└── zh/                       # 中文镜像：env 覆盖构建 → build/book-zh
    └── （与 en/ 同名同路径，含自己的 SUMMARY.md）
```

要点：

- **新增页面必须同时落两棵树、同时改两份 `SUMMARY.md`**（`docs/en/SUMMARY.md` 与 `docs/zh/SUMMARY.md`），只改一边会让语言切换出现 404。
- `docs/en/README.md`、`docs/zh/README.md` 是站点首页，由 `make docs-site` 从根目录 `README.md` / `README.zh-CN.md` 生成，**不提交**。
- 站点由 `book.toml`（仓库根，只写英文书 `src = docs/en`）加环境变量覆盖构建中文书；CI（`.github/workflows/dotnet.yml` 的 `deploy-pages`）把英文书发布在站点根、中文书在 `/zh/`，mdbook 固定 0.4.52。

## 2. 生成流程

```bash
make docs-site     # 生成两份首页 README → mdbook build（EN → build/book）→ env 覆盖（ZH → build/book-zh）
```

本地需要 mdbook（CI 固定 0.4.52）。构建后直接打开 `build/book/index.html`（英）和 `build/book-zh/index.html`（中）预览。

**新增/修改文档页的固定流程：**

1. 内容写入 `docs/en/…` 和 `docs/zh/…`（同名文件）。
2. 两份 `SUMMARY.md` 各加一行（标题一英一中，路径相同）。
3. `make docs-site` 构建无警告。
4. 结构校验（写文档任务的验收标准，全部要通过）：
   - 两书内容页集合完全对称（数量相等、相对路径一一对应）；
   - 所有页内链接零死链（相对链接都能落到实际文件）；
   - 每页 `<html lang>` 与所属书一致（en/zh）；
   - 每个内容页都含 `docs/lang-switcher.js` 脚本标签。

**双语产出方式：** 先把一种语言写好，再整篇翻译成另一种。翻译规则：

- 代码块（含围栏标记与信息串）**逐字节保留**；行内代码、链接目标、文件路径、标识符一律不动；只译正文、列表项、表格文案和标题的描述部分（纯标识符标题保持原样）。
- Markdown 结构（标题层级、列表嵌套、表格对齐行、引用块、分隔线）保持一致。
- 中文正文用全角标点，代码内保持半角。
- 术语全仓库一致，核心对照：garbage collection 垃圾回收；reference/value type 引用类型/值类型；coroutine 协程；interface/impl 接口/实现；namespace/scope 命名空间/作用域；specialization/monomorphization 特化/单态化；bootstrap 自举；vtable 虚表；boxing/unboxing 装箱/拆箱；pass（编译器阶段）遍（"Pass 1" 等专名不译）；emit 输出；register 寄存器；instruction 指令；compile-time/runtime 编译期/运行时；GC 的 span、green tea、root（根）、mark/sweep（标记/清扫）、write barrier（写屏障）按此处理。术语首次出现可括注英文，仅一次。

## 3. 编写规范

### 3.1 语言风格（所有文档通用）

- **用大白话把机制讲清楚。** 用平常的说明文语言，句子完整、直给。不要堆砌专业词汇——术语首次出现时要用一句话说明它是什么，之后才可当专名用。
- **不加比喻、玩笑和情绪。** 不用"就像……一样"的类比，不写俏皮话、感叹号轰炸、主观评价（"优雅的""强大的"这类词不用于形容自家特性），不向读者讨好或抒情。语气始终是平铺直叙的陈述。
- 示例代码必须真实可运行：写进文档的示例先在当前编译器上跑过（`dotnet run --project BabyPenguin -- <file>` 或走 Tests 框架），输出与文档一致。

### 3.2 内容时效（所有文档通用）

- **文档只描述当前代码的行为。** 不写历史过程（"以前是 A，后来改成 B"），不写变更原因或动机叙事（"为了解决 X 问题引入了 Y"），不写 changelog 式内容。读到文档的人只需要现在是什么样。
- 例外：**语言层面**确定要实现、还未实现的功能可以写进教程和规范（明确标注为计划中的语言功能）；实现笔记只写已实现的代码。
- 文档跟不上代码就是 bug：改了语言行为或编译器结构，同步改受影响的文档页。

### 3.3 三类文档各自的写法

| | 教程 `tutorials/` | 规范 `specifications/` | 实现笔记 `impl-notes/` |
|---|---|---|---|
| 回答的问题 | PenguinLang 能做什么、用起来什么样 | 某个功能模块的确切行为定义 | 某个编译器怎么实现这个特性 |
| 读者 | 有一般编程基础、不了解 PenguinLang 的人 | 语言使用者、审规范的人 | 编译器开发者 |
| 深度 | 广而浅 | 细而全 | 具体到代码 |
| 示例 | 精选、简短、能突出特性 | 少而准，用于界定行为边界 | 引用真实代码与 IR/LLVM 片段 |
| 禁区 | 不深入某个语法点的全部细节、不穷举 | 不谈编译器如何实现 | 不写语言该怎样（那是规范的事） |

- **教程**：以简单的描述和优雅的例子为主。每个概念配一个能独立看懂的最小示例，示例即解释的一部分；串讲特性，让读者快速建立整体认识。不逐条罗列语法规则，细节交给规范。
- **规范**：字典式描述 PenguinLang 的一个功能模块。把语法形式、类型规则、语义、错误与边界情况（空输入、嵌套、歧义、与其它特性的交互）一一写清，追求"照着规范就能判定任意程序的合法行为"。讨论穷尽细节，但不讨论编译器内部如何做到。
- **实现笔记**：记录**具体某个编译器**（如 EmperorPenguin；如涉及多编译器，写清各自做法与差异）如何实现特定语言特性。写数据结构与内存布局、处理阶段与顺序、关键代码位置（`文件名:行号`）、与运行时/C 运行时的配合。设计取舍写成"当前设计是什么、代价是什么"，不写成"为什么放弃了原方案"。

### 3.4 快速自检

写完一页后过一遍：

- 有没有句子在讲历史或变更原因？删掉，只留现状。
- 有没有未解释的术语直接当读者已知？补一句白话解释。
- 有没有比喻、玩笑、感叹？删掉，换成直陈。
- 示例跑过吗？输出和文档一致吗？
- 两棵树都写了吗？两份 SUMMARY 都加了吗？`make docs-site` 校验四项都过吗？
