---
name: reasonix-coder
description: 把有复杂度的编码任务或缺陷修复委派给本机 reasonix CLI 执行，由你（Claude Code）担任技术负责人做分析、出计划、审查产出并迭代推进。Use when the user asks to implement a feature or fix a bug of moderate-or-greater complexity and delegation to reasonix is appropriate — triggers include "用 reasonix 实现/修复", "让 reasonix 做", "delegate to reasonix", 以及任何需要多文件/需要设计/需要探索的中大型编码任务。也可由用户显式调用 /reasonix-coder。注意：调用本 skill 不等于一定委派——第 1 步永远是先做分流判断。
---

# Reasonix Coder

把实际的编码/修 bug 工作委派给本机的 **reasonix** CLI（DeepSeek-native coding agent）。你担任"技术负责人"：分析、出计划、指挥、审查、迭代、收尾。reasonix 干"写代码"的活；你干"判断和把关"的活。

## 核心契约（务必遵守）

1. **你是负责人，reasonix 是执行者。** 你不写大段实现代码（除非任务被判定为"极简"，见第 1 步）；你把工作切成阶段，逐阶段派给 reasonix。
2. **不向用户转播 reasonix 的内部过程/流式输出。** reasonix 的 stdout 全量 tee 到 `/tmp` 日志，你只读取并提炼成结构化结论向用户汇报。
3. **每个阶段都要 git 快照 + git 审查。** 派活前记录 git 状态；reasonix 返回后用 git diff 审查它的改动。不可接受的改动用 git 回滚后重派。
4. **错误只重试一次。** reasonix 报错（网络/余额/鉴权类）→ 重试一次；仍错 → 立即停止并向用户报告，不无限重试。
5. **多轮迭代用 `-c` 续接同一会话。** 同一任务的后续阶段用 `reasonix run -c ...`，复用 reasonix 的上下文与 prefix cache（实测续轮成本可低至首轮的 1/20）。
6. **始终用 `deepseek-flash` 模型**（= deepseek-v4-flash，用户的硬性要求）。**不要**用 `deepseek-pro`——无论任务多难。`deepseek-flash` 是 reasonix 的默认模型，命令里可省略 `--model`；若要显式写就写 `--model deepseek-flash`。
7. **除非用户显示指定，否则不要自动调用该技能**

---

## 流程

### 第 0 步：前置检查（每次进入本 skill 跑一次，极快）

```bash
command -v reasonix >/dev/null && reasonix --version        # 确认 CLI 存在
reasonix doctor --json >/tmp/rx-doctor.json 2>&1 || true    # 诊断（API key/余额/配置），失败也不阻断
```

- 若 `reasonix` 不存在 → 告诉用户需先 `npm i -g reasonix` 并 `reasonix setup`，**停止**。
- 若 doctor 显示鉴权/余额问题 → 告诉用户，**停止**，不要派活。

### 第 1 步：分流 —— 自己动手 vs 委派

先对任务做一轮分析（读相关代码、定位根因/影响面。），如果需要调查大范围文件，也可以委托subagent或委托给reasonix。然后判断：

**自己动手，当且仅当**满足全部条件：
- 单文件、改动 < 约 30 行；
- 无设计决策、无歧义，改法唯一且显然正确；
- 纯机械改动（改名、补明显的 null 检查、修 typo、调字面量等）。

**否则委派给 reasonix**：多文件、需要探索/设计、行为复杂、涉及类型系统或并发等。

> 判定为"极简"时直接用你自己的工具完成，并向用户说明"任务简单，已直接处理，未调用 reasonix"。

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
git status --short > /tmp/rx-base-<stage>.txt
git rev-parse HEAD > /tmp/rx-base-sha-<stage>.txt
git stash create > /tmp/rx-stash-<stage>.txt 2>/dev/null || true   # 可选：造一个可回退的临时 stash 引用
```

> 目的：reasonix 改完后，能干净地 `git diff` 出"本阶段它动了什么"，必要时能回退。

#### 3b. 派活给 reasonix（headless 一次性执行后退出）

**首阶段**（新会话）：
```bash
timeout 900 reasonix run \
  --dir "$PWD" \
  --max-steps 30 \
  --profile delivery \
  --metrics /tmp/rx-metrics-<stage>.json \
  "<阶段任务提示词，见下方模板>" \
  > /tmp/rx-<stage>.log 2>&1
echo "EXIT=$?"
```

**后续阶段或纠偏**（续接同一会话，便宜）：把 `reasonix run` 改成 `reasonix run -c`，其余参数不变。

参数说明（均经实测）：
| 参数 | 作用 |
|---|---|
| `--dir <path>` | 工作区根（reasonix 的 sandbox 以此为 workspace_root，只能改这里的文件）|
| `--max-steps N` | 工具调用轮数硬上限（0=无限）。**这是让 reasonix"到点停下"的硬阀门**；按阶段复杂度给 15–40 |
| `--profile delivery` | 运行档位：`economy \| balanced \| delivery`；交付编码用 `delivery` |
| `--metrics <file>` | 跑完写一份 JSON（tokens/成本/steps）到该路径，用于核算 |
| `-c` | 续接最近一次会话（保留上下文 + 命中 prefix cache，续轮极便宜）|
| `--model <name>` | **固定 `deepseek-flash`**（= deepseek-v4-flash，用户硬性要求，不要用 pro）。flash 即默认值，命令里通常可省略此参数 |
| `--resume <path>` | 续接指定会话文件（精确控制时用，优先级高于 `-c`）|

> 注意：`run` 子命令**没有** `--yolo`（那是顶层交互模式的参数）。headless `run` 会自动批准工具调用、自己跑、然后退出——这正是我们要的无人值守行为。**不要**把 reasonix 放到后台 (`&`)，前台跑+`timeout` 兜底即可。

#### 3c. 任务提示词模板（每个阶段都要套）

派给 reasonix 的提示词必须包含下列结构（用中文或英文均可，但要明确）：

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

> 这个"停下并汇报"契约 + `--max-steps` 硬阀门，就是要求 3a（遇到问题/阶段性进展时停下回报）的实现方式。

#### 3d. 读取 reasonix 的产出

- `EXIT=0` 且日志末尾有结构化汇报 → 进入 3e 审查。
- `EXIT!=0` → 进入第 4 步（错误处理）。
- 从 `/tmp/rx-<stage>.log` **末尾**提炼 reasonix 的"1/2/3/4"汇报（不要把整段流式输出给用户）。从 `/tmp/rx-metrics-<stage>.json` 读 `cost`/`steps` 记账。

#### 3e. git 审查 reasonix 的改动

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

可选二次意见（便宜，让 reasonix 自己审自己的 diff）：
```bash
reasonix review --base "$(cat /tmp/rx-base-sha-<stage>.txt)" > /tmp/rx-review-<stage>.log 2>&1
```

#### 3f. 决策与推进

- **本阶段合格** → 记录成本、进入下一阶段（用 `reasonix run -c ...` 续接）。
- **小瑕疵** → 用 `reasonix run -c "<针对瑕疵的具体修正指令，仍套汇报契约>"` 做一轮纠偏，再回 3d。
- **方向错/质量差** → `git restore`/`git checkout -- .` 回退本阶段改动，重写更清晰的阶段提示词后重派（首阶段重派不用 `-c`；若想丢弃前序会话可开新会话）。

循环直到计划的所有阶段完成，或预算/时间耗尽（向用户如实说明进度）。

### 第 4 步：错误与重试策略

对每个非零退出码，先读 `/tmp/rx-<stage>.log` 末尾分类：

| 类别 | 典型特征 | 处理 |
|---|---|---|
| **基础设施类**（网络/余额/鉴权/rate-limit/超时）| 日志含 `network`、`balance`、`401/403/429`、`timeout`、`insufficient` | **重试一次**（等 ~30s）；仍失败 → **停止**，把错误尾部报给用户 |
| **逻辑类**（reasonix 跑完了但没达成目标/汇报"卡住"）| EXIT=0 但完成判据不满足，或汇报里有"阻塞" | 这**不是**错误，是 3f 的纠偏场景：用 `-c` 给更具体的指令继续推进 |
| **reasonix 自身崩溃** | panic、segmentation、未知非零码 | 重试一次；仍崩 → 停止并报告 |

**硬规则**：基础设施类错误**最多重试 1 次**，第二次仍错就停下，不要第三次。向用户报告时附上日志路径供其排查。

### 第 5 步：收尾与总结

所有阶段完成（或决定收尾）后：

1. **整体 git 审查**：`git diff` 全量看一遍；必要时按文件 review。
2. **验证**：跑计划里约定的 build/test 命令，把结果如实 tee 到日志（PenguinLang 项目按 CLAUDE.md 的 `dotnet test` / markdown 测试框架执行）。**测试失败就如实说失败**，不要粉饰。
3. **若修了一个可复现的 bug** 且是 PenguinLang 项目：按 CLAUDE.md 要求，把最小复现沉淀成 `Tests/<Category>/<Name>.md` 用例（除非 reasonix 已经在阶段里做了，3e 审查时确认）。
4. **向用户出具完成报告**（精炼，不堆 reasonix 流式日志）：
   - 做了什么（按阶段/按文件）
   - 验证结果（哪些测试通过 / 失败 / 未跑）
   - 总成本（把各阶段 `--metrics` 的 `cost` 求和，注明币种）
   - 遗留问题与建议的后续步骤
   - 提示用户：改动尚未提交（除非用户明确要求过 commit），需要可帮忙提交。

---

## reasonix CLI 速查（实测 v1.17.12）

```bash
reasonix run [--model NAME] [--max-steps N] [-c|--continue] [--resume PATH] [--copy]
             [--dir PATH] [--profile economy|balanced|delivery] [--metrics FILE] <task>
                 # 一次性执行任务后退出；headless；自动批准工具
reasonix review [--base BRANCH] [--commit SHA] [--model NAME]
                 # 基于本地 diff 的 AI 代码审查
reasonix doctor [--json]
                 # 本地诊断（脱敏）：配置/鉴权/余额/能力
reasonix --version | reasonix help
```

- 配置优先级：`flag > ./reasonix.toml > ~/.reasonix/config.toml > 默认值`；密钥经 `api_key_env` 从环境注入（如 `DEEPSEEK_API_KEY`）。
- **模型固定 `deepseek-flash`（= deepseek-v4-flash）**——用户要求始终用 flash，**不要切 `deepseek-pro`**。flash 是默认值，命令可省略 `--model`。
- sandbox：默认 `bash = enforce`、workspace_root = `--dir` 指定的目录——reasonix 只能改工作区内文件，已被 jail。
- 模型可见思考语言可由 `reasonix config reasoning-language zh|en` 配置。

## 禁忌

- **不要**把 reasonix 的整段流式输出贴给用户；只给结论 + 你的 git 审查。
- **不要**对基础设施类错误重试超过 1 次。
- **不要**跳过 git 审查直接采纳 reasonix 的改动。
- **不要**让 reasonix 跨阶段一次性做完整个大任务（会失控）；坚持逐阶段 + `--max-steps` 阀门 + 停下汇报契约。
- **不要**在不属于"极简"的任务上跳过计划审批就派活。
- **不要**在未告知用户的情况下 `git commit`/`git push`（遵循全局规则：只在用户要求时提交）。
- **不要**使用轮询查询reasonix任务状态包括taskoutput等，应该优先使用完成通知唤醒（reasonix任务完成后唤醒） 