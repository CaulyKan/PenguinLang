# PenguinLang DAP MCP Server 实现计划

## 目标
创建一个 MCP (Model Context Protocol) 服务器，作为 Claude Code 和 PenguinLang DAP 调试器之间的桥梁，使 Claude 可以直接调试 PenguinLang 代码。

## 架构

```
Claude Code  <--MCP/stdio-->  MCP Server (Node.js)  <--DAP/stdio-->  MagellanicPenguinDAP.exe
```

MCP Server 作为中间层：
- 对上：暴露 MCP tools 给 Claude Code 调用
- 对下：作为 DAP client 与 `MagellanicPenguinDAP.exe` 子进程通信

## 项目结构

```
MagellanicPenguin/mcp-debug/
├── package.json
├── tsconfig.json
└── src/
    ├── index.ts          # MCP server 入口，定义所有 tools
    └── dap-client.ts     # DAP 协议客户端，管理子进程通信
```

## DAP 协议通信

DAP 使用 header-based 消息格式：
```
Content-Length: <N>\r\n\r\n<JSON body>
```

`dap-client.ts` 负责：
- 启动 `MagellanicPenguinDAP.exe` 子进程
- 发送/接收 DAP 消息（请求-响应 + 事件监听）
- 维护 sequence number
- 缓存事件（stopped, output, terminated 等）

## MCP Tools 定义

### 1. `penguin_debug_launch`
启动调试会话。编译并运行 .penguin 文件。
- 参数：`program` (文件路径), `stopOnEntry` (bool, 默认 false)
- 流程：initialize → launch → setBreakpoints(如有) → configurationDone
- 返回：会话状态和初始输出

### 2. `penguin_debug_set_breakpoints`
设置断点。
- 参数：`file` (文件路径), `breakpoints` (数组: [{line, column?}])
- 返回：已验证的断点列表

### 3. `penguin_debug_continue`
继续执行。
- 返回：执行结果（stopped 原因 / 程序输出 / 已终止）

### 4. `penguin_debug_step_over`
单步跳过。
- 返回：同上

### 5. `penguin_debug_step_into`
单步进入。
- 返回：同上

### 6. `penguin_debug_step_out`
单步跳出。
- 返回：同上

### 7. `penguin_debug_stack_trace`
获取调用栈。
- 返回：栈帧列表（函数名、文件、行号、列号）

### 8. `penguin_debug_variables`
获取当前作用域变量。
- 参数：可选 `frameId`（默认当前帧）
- 返回：变量列表（名称、值、类型）

### 9. `penguin_debug_disconnect`
终止调试会话。
- 返回：程序最终输出

### 10. `penguin_debug_status`
查询当前调试状态。
- 返回：是否在运行/暂停/已终止，当前位置信息

## 实现步骤

### Step 1: 项目初始化
- 创建 `MagellanicPenguin/mcp-debug/` 目录
- `package.json` 添加 `@modelcontextprotocol/sdk` 依赖
- `tsconfig.json` 配置 TypeScript 编译

### Step 2: 实现 DAP Client (`src/dap-client.ts`)
- `DapClient` 类，管理子进程生命周期
- 消息收发：`sendMessage()` / `onMessage()`
- 请求-响应匹配（通过 sequence number）
- 事件缓存（stopped, output, terminated 等）
- 便捷方法：`initialize()`, `launch()`, `setBreakpoints()`, `continue()`, `stepIn()`, `stepOut()`, `next()`, `stackTrace()`, `variables()`, `disconnect()`

### Step 3: 实现 MCP Server (`src/index.ts`)
- 使用 `@modelcontextprotocol/sdk` 创建 MCP server
- 注册所有 10 个 tools
- 每个 tool 内部调用 `DapClient` 对应方法
- 格式化返回结果为易读文本

### Step 4: 编译测试
- `npm run build` 编译 TypeScript
- 手动测试 MCP server 连接

### Step 5: 配置 Claude Code
在项目 `.claude/settings.json` 中添加 MCP server 配置：
```json
{
  "mcpServers": {
    "penguin-debug": {
      "command": "node",
      "args": ["MagellanicPenguin/mcp-debug/dist/index.js"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

## 关键设计决策

1. **使用 TypeScript + MCP SDK**：官方 SDK 支持最好，生态成熟
2. **子进程模式**：MCP server 每次需要调试时启动 DAP 子进程，会话结束后关闭
3. **单会话模型**：同一时间只支持一个调试会话（与当前 DAP 实现一致）
4. **DAP 路径自动检测**：优先使用环境变量 `PENGUIN_DAP_PATH`，其次使用相对路径查找 `MagellanicPenguin/DAP/bin/Debug/net8.0/MagellanicPenguinDAP.exe`，最后 fallback 到 `dotnet run`
5. **异步事件处理**：step/continue 操作等待 DAP stopped 事件后返回，确保 Claude 能拿到最新的调试状态
