#!/usr/bin/env node

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { DapClient } from "./dap-client.js";

// Singleton debug session
let activeSession: DapClient | null = null;

function getSession(): DapClient {
  if (!activeSession) {
    throw new Error(
      "No active debug session. Use penguin_debug_launch to start one."
    );
  }
  return activeSession;
}

function endSession(): void {
  if (activeSession) {
    activeSession.kill();
    activeSession = null;
  }
}

// ─── Create MCP Server ───

const server = new McpServer({
  name: "penguin-debug",
  version: "0.1.0",
});

// ─── Tool: Launch ───

server.tool(
  "penguin_debug_launch",
  "Launch a debug session for a PenguinLang (.penguin) program. " +
    "This compiles the program, optionally sets breakpoints, and starts execution. " +
    "Returns whether the program stopped at a breakpoint/entry or ran to completion.",
  {
    program: z.string().describe("Path to the .penguin or .penguins file to debug"),
    stopOnEntry: z
      .boolean()
      .optional()
      .default(false)
      .describe("Whether to stop at the entry point"),
    breakpoints: z
      .array(
        z.object({
          file: z.string().describe("Source file path"),
          lines: z
            .array(
              z.object({
                line: z.number().describe("Line number (1-based)"),
                column: z.number().optional().describe("Column number (1-based)"),
              })
            )
            .describe("Breakpoint locations in this file"),
        })
      )
      .optional()
      .describe("Breakpoints to set before starting execution"),
  },
  async ({ program, stopOnEntry, breakpoints }) => {
    // End any existing session
    endSession();

    const client = new DapClient();
    activeSession = client;

    const result = await client.startSession(program, breakpoints, stopOnEntry);

    let text = `Debug session started.\nStatus: ${result.status}\n`;

    if (result.breakpoints) {
      text += "\nBreakpoints:\n";
      for (let i = 0; i < (breakpoints?.length ?? 0); i++) {
        const bpGroup = result.breakpoints[i];
        const file = breakpoints![i].file;
        text += `  ${file}:\n`;
        for (const bp of bpGroup) {
          text += `    Line ${bp.line}: ${bp.verified ? "verified" : "not verified"}\n`;
        }
      }
    }

    if (result.error) {
      text += `\nError: ${result.error}\n`;
      endSession();
    } else if (client.state.stopped) {
      // Get stack trace for context
      try {
        const frames = await client.stackTrace();
        if (frames.length > 0) {
          const top = frames[0];
          text += `\nStopped at: ${top.source}:${top.line}:${top.column} in ${top.name}\n`;
          if (frames.length > 1) {
            text += "Call stack:\n";
            for (const f of frames) {
              text += `  ${f.name} at ${f.source}:${f.line}:${f.column}\n`;
            }
          }
        }
      } catch {
        // Stack trace may not be available
      }

      try {
        const vars = await client.variables();
        if (vars.length > 0) {
          text += "\nLocal variables:\n";
          for (const v of vars) {
            text += `  ${v.name} = ${v.value}\n`;
          }
        }
      } catch {
        // Variables may not be available
      }
    } else if (client.state.terminated) {
      text += `\nProgram output:\n${client.output}\n`;
      if (client.debugOutput) {
        text += `\nDebug output:\n${client.debugOutput}\n`;
      }
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Set Breakpoints ───

server.tool(
  "penguin_debug_set_breakpoints",
  "Set breakpoints in a source file during an active debug session.",
  {
    file: z.string().describe("Source file path"),
    breakpoints: z
      .array(
        z.object({
          line: z.number().describe("Line number (1-based)"),
          column: z.number().optional().describe("Column number (1-based)"),
        })
      )
      .describe("Breakpoint locations"),
  },
  async ({ file, breakpoints }) => {
    const client = getSession();
    const results = await client.setBreakpoints(file, breakpoints);

    let text = "Breakpoints set:\n";
    for (const bp of results) {
      text += `  Line ${bp.line}${bp.column ? `:${bp.column}` : ""}: ${bp.verified ? "verified" : "not verified"}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Continue ───

server.tool(
  "penguin_debug_continue",
  "Continue execution of the debugged program. Returns when the program stops (breakpoint, step, exception) or terminates.",
  {},
  async () => {
    const client = getSession();
    const result = await client.continue();

    let text: string;
    if (result.stopped) {
      text = `Execution stopped. Reason: ${result.reason}\n`;

      // Get current position
      try {
        const frames = await client.stackTrace();
        if (frames.length > 0) {
          const top = frames[0];
          text += `Location: ${top.source}:${top.line}:${top.column} in ${top.name}\n`;
        }

        const vars = await client.variables();
        if (vars.length > 0) {
          text += "\nLocal variables:\n";
          for (const v of vars) {
            text += `  ${v.name} = ${v.value}\n`;
          }
        }
      } catch {
        // Ignore errors getting context
      }
    } else {
      text = "Program execution completed.\n";
      text += `\nProgram output:\n${client.output}\n`;
      if (client.debugOutput) {
        text += `\nDebug output:\n${client.debugOutput}\n`;
      }
      endSession();
    }

    if (result.output) {
      text += `\nNew output: ${result.output}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Step Over ───

server.tool(
  "penguin_debug_step_over",
  "Step over the current line. Executes the current line without entering function calls.",
  {},
  async () => {
    const client = getSession();
    const result = await client.stepOver();
    return formatStepResult(client, result);
  }
);

// ─── Tool: Step Into ───

server.tool(
  "penguin_debug_step_into",
  "Step into the current function call.",
  {},
  async () => {
    const client = getSession();
    const result = await client.stepInto();
    return formatStepResult(client, result);
  }
);

// ─── Tool: Step Out ───

server.tool(
  "penguin_debug_step_out",
  "Step out of the current function, returning to the caller.",
  {},
  async () => {
    const client = getSession();
    const result = await client.stepOut();
    return formatStepResult(client, result);
  }
);

// Helper for step results
async function formatStepResult(
  client: DapClient,
  result: { stopped: boolean; reason?: string; output?: string }
) {
  let text: string;

  if (result.stopped) {
    text = `Stepped. Reason: ${result.reason}\n`;

    try {
      const frames = await client.stackTrace();
      if (frames.length > 0) {
        const top = frames[0];
        text += `Location: ${top.source}:${top.line}:${top.column} in ${top.name}\n`;
      }

      const vars = await client.variables();
      if (vars.length > 0) {
        text += "\nLocal variables:\n";
        for (const v of vars) {
          text += `  ${v.name} = ${v.value}\n`;
        }
      }
    } catch {
      // Context may not be available
    }
  } else {
    text = "Program execution completed after step.\n";
    text += `\nProgram output:\n${client.output}\n`;
    if (client.debugOutput) {
      text += `\nDebug output:\n${client.debugOutput}\n`;
    }
    endSession();
  }

  if (result.output) {
    text += `\nNew output: ${result.output}\n`;
  }

  return { content: [{ type: "text" as const, text }] };
}

// ─── Tool: Stack Trace ───

server.tool(
  "penguin_debug_stack_trace",
  "Get the current call stack trace. Shows function names, file locations, and line numbers.",
  {},
  async () => {
    const client = getSession();
    const frames = await client.stackTrace();

    if (frames.length === 0) {
      return { content: [{ type: "text" as const, text: "No stack frames available." }] };
    }

    let text = `Call stack (${frames.length} frames):\n`;
    for (let i = 0; i < frames.length; i++) {
      const f = frames[i];
      const marker = i === 0 ? ">>>" : "   ";
      text += `${marker} #${i} ${f.name} at ${f.source}:${f.line}:${f.column}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Variables ───

server.tool(
  "penguin_debug_variables",
  "Get variables in the current scope. Shows variable names, values, and types.",
  {
    variablesReference: z
      .number()
      .optional()
      .describe("Reference ID for nested variables (omit for current scope locals)"),
  },
  async ({ variablesReference }) => {
    const client = getSession();
    const vars = await client.variables(variablesReference);

    if (vars.length === 0) {
      return { content: [{ type: "text" as const, text: "No variables in current scope." }] };
    }

    let text = "Variables:\n";
    for (const v of vars) {
      const expandable = v.variablesReference > 0 ? " [expandable]" : "";
      text += `  ${v.name} = ${v.value}${expandable}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Evaluate ───

server.tool(
  "penguin_debug_evaluate",
  "Evaluate an expression in the current debug context. (Note: evaluation support depends on the DAP adapter implementation)",
  {
    expression: z.string().describe("Expression to evaluate"),
  },
  async ({ expression }) => {
    const client = getSession();
    const result = await client.evaluate(expression);

    let text = `Result: ${result.result}\n`;
    if (result.variablesReference > 0) {
      text += "(Use penguin_debug_variables with variablesReference to expand)\n";
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Disconnect ───

server.tool(
  "penguin_debug_disconnect",
  "End the current debug session and terminate the program.",
  {},
  async () => {
    const client = getSession();
    const result = await client.disconnect();
    endSession();

    let text = "Debug session ended.\n";
    if (result.output) {
      text += `\nFinal program output:\n${result.output}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Status ───

server.tool(
  "penguin_debug_status",
  "Get the current debug session status: running, stopped, or terminated.",
  {},
  async () => {
    if (!activeSession) {
      return {
        content: [{ type: "text" as const, text: "No active debug session." }],
      };
    }

    const state = activeSession.state;
    let text = "Debug session status:\n";
    text += `  State: ${state.terminated ? "terminated" : state.stopped ? "stopped" : state.running ? "running" : "unknown"}\n`;

    if (state.stopped && state.stopReason) {
      text += `  Stop reason: ${state.stopReason}\n`;
    }

    if (state.currentFile && state.currentLine) {
      text += `  Current location: ${state.currentFile}:${state.currentLine}\n`;
    }

    if (activeSession.output) {
      text += `\nAccumulated output:\n${activeSession.output}\n`;
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Tool: Diagnostic Output ───

server.tool(
  "penguin_debug_output",
  "Get the diagnostic/debug output from the DAP adapter. This includes compiler messages, breakpoint status, internal debug logs, and any non-program output sent by the debugger.",
  {},
  async () => {
    if (!activeSession) {
      return {
        content: [{ type: "text" as const, text: "No active debug session." }],
      };
    }

    const debugOut = activeSession.debugOutput;
    const programOut = activeSession.output;

    if (!debugOut && !programOut) {
      return {
        content: [{ type: "text" as const, text: "No output yet." }],
      };
    }

    let text = "";
    if (debugOut) {
      text += "=== Diagnostic/Debug Output ===\n" + debugOut + "\n";
    }
    if (programOut) {
      text += "=== Program Output (stdout) ===\n" + programOut + "\n";
    }

    return { content: [{ type: "text" as const, text }] };
  }
);

// ─── Start server ───

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  console.error("MCP server error:", err);
  process.exit(1);
});
