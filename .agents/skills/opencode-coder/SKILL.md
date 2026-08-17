---
name: opencode-coder
description: 把有复杂度的编码任务或缺陷修复委派给本机 opencode CLI 执行，由你（Claude Code）担任技术负责人做分析、出计划、审查产出并迭代推进。Use when the user asks to implement a feature or fix a bug of moderate-or-greater complexity and delegation to opencode is appropriate — triggers include "用 opencode 实现/修复", "让 opencode 做", "delegate to opencode", 以及任何需要多文件/需要设计/需要探索的中大型编码任务。也可由用户显式调用 /opencode-coder。注意：调用本 skill 不等于一定委派——第 1 步永远是先做分流判断。
---

# OpenCode Coder

把实际的编码/修 bug 工作委派给本机的 **opencode** CLI（SST 出品的开源 coding agent）。你担任"技术负责人"：分析、出计划、指挥、审查、迭代、收尾。opencode 干"写代码"的活；你干"判断和把关"的活。

## 关于模型
模型成本从低到高为：`opencode-go/mimo-v2.5` < `opencode-go/deepseek-v4-flash` < 不使用opencode。因此：
* 首先默认使用`opencode-go/mimo-v2.5`
* 如果超时后发现opencode输出或完成后review判断质量不理想，换成`opencode-go/deepseek-v4-flash`重试任务
* 如果还不行，停止任务并向用户汇报。不允许使用任何未指定的模型。

## 核心契约（务必遵守）

1. **你是负责人，opencode 是执行者。** 你不写大段实现代码（除非任务被判定为"极简"，见第 1 步）；你把工作切成阶段，逐阶段派给 opencode。
2. **不向用户转播 opencode 的内部过程/流式输出。** opencode 的 stdout 全量 tee 到 `/tmp` 日志，你只读取并提炼成结构化结论向用户汇报。
3. **每个阶段都要 git 快照 + git 审查。** 派活前记录 git 状态；opencode 返回后用 git diff 审查它的改动。不可接受的改动用 git 回滚后重派。
4. **错误只重试一次。** opencode 报错（网络/余额/鉴权类）→ 重试一次；仍错 → 立即停止并向用户报告，不无限重试。
5. **多轮迭代用 `-c` 续接同一会话。** 同一任务的后续阶段用 `opencode run -c ...`，复用 opencode 的会话上下文与 DeepSeek 上下文缓存（实测续轮明显更便宜）。


---

## 流程

### 第 0 步：前置检查（每次进入本 skill 跑一次，极快）

```bash
command -v opencode >/dev/null && opencode --version      # 确认 CLI 存在
opencode providers list 2>&1 | grep -i deepseek           # 确认 DeepSeek 凭据已配置
opencode debug config >/tmp/oc-config.json 2>&1 || true   # 可选：看已解析配置，失败也不阻断
```

- 若 `opencode` 不存在 → 告诉用户需先安装（`curl -fsSL https://opencode.ai/install | bash` 或 `npm i -g opencode-ai`）并 `opencode providers login deepseek`，**停止**。
- 若 `providers list` 里没有 DeepSeek 凭据 → 告诉用户需先 `opencode providers login deepseek`，**停止**，不要派活。

### 第 1 步：分流 —— 自己动手 vs 委派

先对任务做一轮分析（读相关代码、定位根因/影响面。），如果需要调查大范围文件，也可以委托subagent或委托给opencode。然后判断：

**自己动手，当且仅当**满足全部条件：
- 单文件、改动 < 约 30 行；
- 无设计决策、无歧义，改法唯一且显然正确；
- 纯机械改动（改名、补明显的 null 检查、修 typo、调字面量等）。

**否则委派给 opencode**：多文件、需要探索/设计、行为复杂、涉及类型系统或并发等。

> 判定为"极简"时直接用你自己的工具完成，并向用户说明"任务简单，已直接处理，未调用 opencode"。

### 第 2 步：出计划（plan mode）

对要委派的任务，进入 **plan 模式**产出一份**详细**执行计划，写进 plan 文件后用 ExitPlanMode 交给用户审批。计划必须包含：

- **目标与范围**：要达成什么、明确不做什么（out-of-scope）。
- **涉及文件/模块**：列出关键路径（先 grep/读代码确认，别猜）。
- **方案**：怎么改、为什么这么改、有无替代方案及取舍。
- **分阶段拆解**：把任务切成 N 个**可独立验证**的阶段（每阶段一个 PR 粒度的改动）。每阶段写明：输入、产出、验证方式。
- **验证计划**：跑哪些 build/test 命令、预期结果；对 PenguinLang 项目遵循 CLAUDE.md 里的测试约定（tee 日志、markdown 测试用例等）。
- **风险**：可能踩的坑、不确定的地方。

用户批准后再进入第 3 步。若用户要调整，先改计划再继续。

### 第 3 步：委派—审查—迭代循环

对计划的每个阶段执行一轮下面的子流程。

#### 3a. 记录 git 基线（每个阶段开头）

```bash
git status --short > /tmp/oc-base-<stage>.txt
git rev-parse HEAD > /tmp/oc-base-sha-<stage>.txt
git stash create > /tmp/oc-stash-<stage>.txt 2>/dev/null || true   # 可选：造一个可回退的临时 stash 引用
```

> 目的：opencode 改完后，能干净地 `git diff` 出"本阶段它动了什么"，必要时能回退。

#### 3b. 派活给 opencode（headless 一次性执行后退出）

**首阶段**（新会话）：
```bash
timeout 900 opencode run \
  --dir "$PWD" \
  -m opencode-go/deepseek-v4-flash \
  --auto \
  --title "stage-<N>-<简短阶段名>" \
  "<阶段任务提示词，见下方模板>" \
  > /tmp/oc-<stage>.log 2>&1
echo "EXIT=$?"
```

**后续阶段或纠偏**（续接同一会话，便宜）：把 `opencode run` 改成 `opencode run -c`，其余参数不变。

参数说明（均经实测 v1.18.18）：
| 参数                           | 作用                                                                                                                                                                                                  |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--dir <path>`                 | 工作区根（opencode 在该目录下运行，只能改这里的文件）                                                                                                                                                 |
| `-m, --model <provider/model>` | **固定 `opencode-go/deepseek-v4-flash`**（用户硬性要求，不要用 pro/reasoner）。每次都要显式传，opencode 默认模型不保证是 flash                                                                        |
| `--auto`                       | 自动批准未被显式 deny 的权限请求（等价 reasonix 的自动批准；`opencode.json` 里显式 `deny` 的规则仍生效，`--auto` 不覆盖）。更严格的管控可改用项目 `opencode.json` 的 `permission` 键配 allow/ask/deny |
| `--title <name>`               | 给会话命名，方便之后在 `opencode session list` 里定位它                                                                                                                                               |
| `-c, --continue`               | 续接最近一次会话（保留上下文，续轮更便宜）                                                                                                                                                            |
| `-s, --session <id>`           | 续接指定会话（精确控制时用，优先级高于 `-c`；会话 id 用 `opencode session list` 查）                                                                                                                  |
| `--format json`                | 可选：stdout 输出原始 JSON 事件流，便于机器解析；平时用默认 format 即可                                                                                                                               |
| `timeout <秒>`                 | **硬阀门**：opencode 没有 `--max-steps` 之类参数，用 shell `timeout` 限制单次运行总时长（默认 900s，按阶段复杂度调整；超时被杀的退出码是 124）                                                        |

> 注意：`opencode run` 本来就是 headless——跑完自动退出，没有交互。**不要**把它放到后台 (`&`)，前台跑+`timeout` 兜底即可，任务结束由命令返回（EXIT）自然唤醒，不需要轮询。

#### 3c. 任务提示词模板（每个阶段都要套）

派给 opencode 的提示词必须包含下列结构（用中文或英文均可，但要明确）：

```
【背景】<2–4 句项目/模块上下文 + 本次大目标>
【本阶段任务】<只描述这一个阶段的范围，明确边界>
【约束】
- 只改<这些文件/目录>，不要动<这些>。
- 不要开始下一阶段的工作。
- 遵守仓库的代码风格与现有约定。
【完成判据】<明确的、可验证的完成条件，例如某测试通过、某行为改变>
【完成后必须停下并按此格式汇报】
  1. 改动的文件清单（含每个文件改了什么）
  2. 已验证的内容（跑了什么命令、结果）
  3. 未完成/剩余的事项
  4. 任何阻塞或不确定点
完成或被卡住时立即停止，不要继续往下做。
```

> 这个"停下并汇报"契约 + `timeout` 硬阀门，就是要求 3a（遇到问题/阶段性进展时停下回报）的实现方式。

#### 3d. 读取 opencode 的产出

- `EXIT=0` 且日志末尾有结构化汇报 → 进入 3e 审查。
- `EXIT!=0`（含 124 = timeout 被杀）→ 进入第 4 步（错误处理）。
- 从 `/tmp/oc-<stage>.log` **末尾**提炼 opencode 的"1/2/3/4"汇报（不要把整段流式输出给用户）。成本记账用 `opencode session list`（按 `--title` 找会话 id）+ `opencode stats` / `opencode export <sessionID>`。

#### 3e. git 审查 opencode 的改动

```bash
git status --short
git diff --stat
git diff                 # 逐文件看；大改时按文件分批看
```

审查要点：
- **正确性**：逻辑对不对？有没有引入 bug、边界遗漏？
- **范围**：有没有越界改动（动了不该动的文件、大范围重构、格式化噪音）？
- **风格**：是否匹配周围代码的命名/注释密度/惯用法（CLAUDE.md 的"读起来像周围代码"原则）？
- **安全**：有没有注入、敏感信息、危险操作？
- **测试**：该阶段若声明加了/改了测试，是否真的加了？是否真的能跑？

可选二次意见（便宜，让 opencode 自己审自己的 diff）：
```bash
opencode run -c -m opencode-go/deepseek-v4-flash --auto \
  "用 git diff 审查你刚才完成的改动：正确性、越界改动、风格、测试是否真的加了且能跑。按问题严重程度列出。" \
  > /tmp/oc-review-<stage>.log 2>&1
```

#### 3f. 决策与推进

- **本阶段合格** → 记录成本、进入下一阶段（用 `opencode run -c ...` 续接）。
- **小瑕疵** → 用 `opencode run -c "<针对瑕疵的具体修正指令，仍套汇报契约>"` 做一轮纠偏，再回 3d。
- **方向错/质量差** → `git restore`/`git checkout -- .` 回退本阶段改动，重写更清晰的阶段提示词后重派（首阶段重派不用 `-c`；若想丢弃前序会话可开新会话）。

循环直到计划的所有阶段完成，或预算/时间耗尽（向用户如实说明进度）。

### 第 4 步：错误与重试策略

对每个非零退出码，先读 `/tmp/oc-<stage>.log` 末尾分类：

| 类别                                                 | 典型特征                                                              | 处理                                                                                      |
| ---------------------------------------------------- | --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| **基础设施类**（网络/余额/鉴权/rate-limit/超时）     | 日志含 `network`、`balance`、`401/403/429`、`timeout`、`insufficient` | **重试一次**（等 ~30s）；仍失败 → **停止**，把错误尾部报给用户                            |
| **逻辑类**（opencode 跑完了但没达成目标/汇报"卡住"） | EXIT=0 但完成判据不满足，或汇报里有"阻塞"                             | 这**不是**错误，是 3f 的纠偏场景：用 `-c` 给更具体的指令继续推进                          |
| **超时被杀**                                         | EXIT=124（timeout 触发）                                              | 看日志判断进度：接近完成 → 用 `-c` 续接收尾；刚起步 → 重试一次并缩小阶段范围/加长 timeout |
| **opencode 自身崩溃**                                | panic、segmentation、未知非零码                                       | 重试一次；仍崩 → 停止并报告                                                               |

**硬规则**：基础设施类错误**最多重试 1 次**，第二次仍错就停下，不要第三次。向用户报告时附上日志路径供其排查。

### 第 5 步：收尾与总结

所有阶段完成（或决定收尾）后：

1. **整体 git 审查**：`git diff` 全量看一遍；必要时按文件 review。
2. **验证**：跑计划里约定的 build/test 命令，把结果如实 tee 到日志（PenguinLang 项目按 CLAUDE.md 的 `dotnet test` / markdown 测试框架执行）。**测试失败就如实说失败**，不要粉饰。
3. **若修了一个可复现的 bug** 且是 PenguinLang 项目：按 CLAUDE.md 要求，把最小复现沉淀成 `Tests/<Category>/<Name>.md` 用例（除非 opencode 已经在阶段里做了，3e 审查时确认）。
4. **向用户出具完成报告**（精炼，不堆 opencode 流式日志）：
   - 做了什么（按阶段/按文件）
   - 验证结果（哪些测试通过 / 失败 / 未跑）
   - 总成本（`opencode stats` 看总量，或按 `--title` 定位各阶段会话后 `opencode export <sessionID>` 看明细，注明币种）
   - 遗留问题与建议的后续步骤
   - 提示用户：改动尚未提交（除非用户明确要求过 commit），需要可帮忙提交。

---

## opencode CLI 速查（实测 v1.18.18）

```bash
opencode run [message..] [-m MODEL] [-c|--continue] [-s SESSION] [--auto]
              [--dir PATH] [--title NAME] [--format json] [--thinking]
                 # 一次性执行任务后退出；headless；跑完自动退出
opencode providers list
                 # 凭据检查（相当于 doctor）：确认 DeepSeek api key 已配置
opencode session list              # 会话列表（id + 标题 + 更新时间）
opencode stats                     # token/成本统计（总量）
opencode export <sessionID>        # 导出会话 JSON（usage 明细，做记账）
opencode debug config              # 查看已解析的配置
opencode models [provider]         # 列出可用模型
opencode providers login [url]     # 登录/配置 provider
```

- 配置优先级：项目 `opencode.json` > `~/.config/opencode/opencode.json`；凭据存于 `~/.local/share/opencode/auth.json`（`opencode providers login` 管理）。
- **模型固定 `opencode-go/deepseek-v4-flash`**——用户要求始终用 flash，**不要切 `deepseek-v4-pro` 或 `deepseek-reasoner`**。opencode 默认模型不保证是 flash，命令里每次都要显式 `-m opencode-go/deepseek-v4-flash`。
- 权限：headless run 用 `--auto` 自动批准"询问"类权限（显式 `deny` 仍生效）；更严格的管控在 `opencode.json` 的 `permission` 键配置 allow/ask/deny。
- 自定义 agent（类似"运行档位"概念）可放 `.opencode/agent/*.md` 定义，本技能不强制要求。
- 无 `--max-steps`：用 shell `timeout` 做单次运行的硬阀门（超时退出码 124）。

## 禁忌

- **不要**把 opencode 的整段流式输出贴给用户；只给结论 + 你的 git 审查。
- **不要**对基础设施类错误重试超过 1 次。
- **不要**跳过 git 审查直接采纳 opencode 的改动。
- **不要**让 opencode 跨阶段一次性做完整个大任务（会失控）；坚持逐阶段 + `timeout` 阀门 + 停下汇报契约。
- **不要**在不属于"极简"的任务上跳过计划审批就派活。
- **不要**在未告知用户的情况下 `git commit`/`git push`（遵循全局规则：只在用户要求时提交）。
- **不要**后台运行 opencode 再轮询任务状态：`opencode run` 前台执行，任务结束由命令返回（EXIT 码）自然唤醒，无需任何轮询。
