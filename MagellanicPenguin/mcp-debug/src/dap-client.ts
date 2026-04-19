import { ChildProcess, spawn } from "child_process";
import { EventEmitter } from "events";
import path from "path";
import fs from "fs";

/**
 * DAP protocol message types
 */
interface DapRequest {
  seq: number;
  type: "request";
  command: string;
  arguments?: Record<string, unknown>;
}

interface DapResponse {
  seq: number;
  type: "response";
  request_seq: number;
  success: boolean;
  command: string;
  message?: string;
  body?: Record<string, unknown>;
}

interface DapEvent {
  seq: number;
  type: "event";
  event: string;
  body?: Record<string, unknown>;
}

type DapMessage = DapRequest | DapResponse | DapEvent;

interface PendingRequest {
  resolve: (response: DapResponse) => void;
  reject: (error: Error) => void;
  timer: ReturnType<typeof setTimeout>;
}

export interface DebugBreakpoint {
  line: number;
  column?: number;
}

export interface StackFrame {
  id: number;
  name: string;
  source?: string;
  line: number;
  column: number;
}

export interface DebugVariable {
  name: string;
  value: string;
  variablesReference: number;
}

export interface DebugState {
  running: boolean;
  stopped: boolean;
  terminated: boolean;
  stopReason?: string;
  currentLine?: number;
  currentFile?: string;
}

/**
 * DAP Client - communicates with MagellanicPenguinDAP.exe via stdio
 */
export class DapClient extends EventEmitter {
  private process: ChildProcess | null = null;
  private seq = 1;
  private pendingRequests = new Map<number, PendingRequest>();
  private buffer = "";
  private initialized = false;
  private launched = false;
  private configured = false;

  // Event state
  private _state: DebugState = {
    running: false,
    stopped: false,
    terminated: false,
  };
  private _outputLines: string[] = [];
  private _debugLines: string[] = [];
  private _currentStackFrames: StackFrame[] = [];
  private _currentVariables: DebugVariable[] = [];

  get state(): DebugState {
    return { ...this._state };
  }

  get output(): string {
    return this._outputLines.join("");
  }

  get debugOutput(): string {
    return this._debugLines.join("");
  }

  /**
   * Find the DAP executable path
   */
  private static findDapPath(): string {
    // 1. Environment variable override
    const envPath = process.env.PENGUIN_DAP_PATH;
    if (envPath) return envPath;

    // 2. Relative to cwd: look for the debug build
    const candidates = [
      // From project root
      "MagellanicPenguin/DAP/bin/Debug/net8.0/MagellanicPenguinDAP.exe",
      // From MagellanicPenguin
      "DAP/bin/Debug/net8.0/MagellanicPenguinDAP.exe",
    ];

    for (const rel of candidates) {
      const full = path.resolve(rel);
      if (fs.existsSync(full)) return full;
    }

    // 3. Fallback: use dotnet run
    return "dotnet";
  }

  /**
   * Start the DAP server process
   */
  async connect(): Promise<void> {
    const dapPath = DapClient.findDapPath();
    const isDotnet = dapPath === "dotnet" || dapPath.endsWith("dotnet.exe");

    const args = isDotnet
      ? ["run", "--project", "MagellanicPenguin/DAP"]
      : [];

    this.process = spawn(dapPath, args, {
      stdio: ["pipe", "pipe", "pipe"],
      env: { ...process.env },
    });

    this.process.stdout!.on("data", (data: Buffer) => {
      this.onData(data.toString());
    });

    this.process.stderr!.on("data", (data: Buffer) => {
      this.emit("stderr", data.toString());
    });

    this.process.on("close", (code) => {
      this._state.terminated = true;
      this._state.running = false;
      this._state.stopped = false;
      this.emit("closed", code);
    });

    this.process.on("error", (err) => {
      this.emit("error", err);
    });
  }

  /**
   * Parse and dispatch incoming DAP messages
   */
  private onData(data: string): void {
    this.buffer += data;

    while (true) {
      // Find header end
      const headerEnd = this.buffer.indexOf("\r\n\r\n");
      if (headerEnd === -1) break;

      const header = this.buffer.substring(0, headerEnd);
      const match = header.match(/Content-Length:\s*(\d+)/i);
      if (!match) {
        this.buffer = this.buffer.substring(headerEnd + 4);
        continue;
      }

      const contentLength = parseInt(match[1], 10);
      const bodyStart = headerEnd + 4;
      const bodyEnd = bodyStart + contentLength;

      if (this.buffer.length < bodyEnd) break;

      const body = this.buffer.substring(bodyStart, bodyEnd);
      this.buffer = this.buffer.substring(bodyEnd);

      try {
        const message: DapMessage = JSON.parse(body);
        this.handleMessage(message);
      } catch {
        // Ignore malformed messages
      }
    }
  }

  private handleMessage(message: DapMessage): void {
    if (message.type === "response") {
      const pending = this.pendingRequests.get(message.request_seq);
      if (pending) {
        clearTimeout(pending.timer);
        this.pendingRequests.delete(message.request_seq);
        pending.resolve(message);
      }
    } else if (message.type === "event") {
      this.handleEvent(message);
    }
  }

  private handleEvent(event: DapEvent): void {
    switch (event.event) {
      case "initialized":
        this.initialized = true;
        break;
      case "stopped":
        this._state.stopped = true;
        this._state.running = false;
        this._state.stopReason = event.body?.reason as string;
        break;
      case "continued":
        this._state.stopped = false;
        this._state.running = true;
        break;
      case "terminated":
        this._state.terminated = true;
        this._state.running = false;
        this._state.stopped = false;
        break;
      case "exited":
        this._state.terminated = true;
        this._state.running = false;
        break;
      case "output":
        const category = event.body?.category as string;
        const text = (event.body?.output as string) ?? "";
        if (category === "stdout") {
          this._outputLines.push(text);
        } else {
          this._debugLines.push(text);
        }
        break;
      case "thread":
        // Thread started/exited events
        break;
    }
    this.emit("event", event);
  }

  /**
   * Send a DAP request and wait for response
   */
  private sendRequest(
    command: string,
    args?: Record<string, unknown>,
    timeout = 10000
  ): Promise<DapResponse> {
    return new Promise((resolve, reject) => {
      if (!this.process || this.process.killed) {
        reject(new Error("DAP process not running"));
        return;
      }

      const seq = this.seq++;
      const request: DapRequest = {
        seq,
        type: "request",
        command,
        arguments: args,
      };

      const body = JSON.stringify(request);
      const header = `Content-Length: ${Buffer.byteLength(body)}\r\n\r\n`;

      const timer = setTimeout(() => {
        this.pendingRequests.delete(seq);
        reject(new Error(`DAP request '${command}' timed out after ${timeout}ms`));
      }, timeout);

      this.pendingRequests.set(seq, { resolve, reject, timer });

      this.process.stdin!.write(header + body);
    });
  }

  /**
   * Wait for a specific event
   */
  waitForEvent(eventName: string, timeout = 30000): Promise<DapEvent> {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error(`Timed out waiting for event '${eventName}'`));
        cleanup();
      }, timeout);

      const handler = (event: DapEvent) => {
        if (event.event === eventName) {
          resolve(event);
          cleanup();
        }
      };

      const cleanup = () => {
        clearTimeout(timer);
        this.off("event", handler);
      };

      this.on("event", handler);
    });
  }

  // ─── High-level debug operations ───

  /**
   * Initialize DAP session
   */
  async initialize(): Promise<void> {
    const response = await this.sendRequest("initialize", {
      clientID: "penguin-debug-mcp",
      clientName: "Penguin Debug MCP",
      adapterID: "penguinlang",
      pathFormat: "path",
      linesStartAt1: true,
      columnsStartAt1: true,
    });

    if (!response.success) {
      throw new Error(`Initialize failed: ${response.message}`);
    }

    this.initialized = true;
  }

  /**
   * Launch a .penguin program
   */
  async launch(program: string, stopOnEntry = false): Promise<void> {
    const absPath = path.resolve(program);
    const response = await this.sendRequest("launch", {
      program: absPath,
      stopAtEntry: stopOnEntry,
    });

    if (!response.success) {
      throw new Error(`Launch failed: ${response.message}`);
    }

    this.launched = true;
  }

  /**
   * Set breakpoints in a file
   */
  async setBreakpoints(
    file: string,
    breakpoints: DebugBreakpoint[]
  ): Promise<{ verified: boolean; line: number; column?: number }[]> {
    const absPath = path.resolve(file);
    const response = await this.sendRequest("setBreakpoints", {
      source: { path: absPath },
      breakpoints: breakpoints.map((bp) => ({
        line: bp.line,
        column: bp.column,
      })),
      linesStartingAt1: true,
      columnsStartingAt1: true,
    });

    if (!response.success) {
      throw new Error(`SetBreakpoints failed: ${response.message}`);
    }

    const bps = (response.body?.breakpoints ?? []) as Array<{
      verified: boolean;
      line: number;
      column?: number;
    }>;

    return bps;
  }

  /**
   * Signal configuration done - starts execution
   */
  async configurationDone(): Promise<void> {
    const response = await this.sendRequest("configurationDone");
    if (!response.success) {
      throw new Error(`ConfigurationDone failed: ${response.message}`);
    }
    this.configured = true;

    // Wait for either stopped or terminated event
    if (!this._state.stopped && !this._state.terminated) {
      try {
        const event = await this.waitForEvent("stopped", 60000);
        this._state.stopReason = event.body?.reason as string;
      } catch {
        // Might have terminated instead
        if (!this._state.terminated) {
          // Wait a bit more for termination
          try {
            await this.waitForEvent("terminated", 10000);
          } catch {
            // Give up waiting
          }
        }
      }
    }
  }

  /**
   * Continue execution, wait for next stop or termination
   */
  async continue(): Promise<{ stopped: boolean; reason?: string; output?: string }> {
    const outputBefore = this._outputLines.length;

    const response = await this.sendRequest("continue", { threadId: 0 });
    if (!response.success) {
      throw new Error(`Continue failed: ${response.message}`);
    }

    // Wait for stopped or terminated
    try {
      await Promise.race([
        this.waitForEvent("stopped", 60000),
        this.waitForEvent("terminated", 60000),
      ]);
    } catch {
      // Timeout
    }

    const newOutput = this._outputLines.slice(outputBefore).join("");

    if (this._state.stopped) {
      return { stopped: true, reason: this._state.stopReason, output: newOutput };
    } else {
      return { stopped: false, output: newOutput };
    }
  }

  /**
   * Step over
   */
  async stepOver(): Promise<{ stopped: boolean; reason?: string; output?: string }> {
    const outputBefore = this._outputLines.length;

    const response = await this.sendRequest("next", { threadId: 0 });
    if (!response.success) {
      throw new Error(`Next failed: ${response.message}`);
    }

    try {
      await Promise.race([
        this.waitForEvent("stopped", 60000),
        this.waitForEvent("terminated", 60000),
      ]);
    } catch {
      // Timeout
    }

    const newOutput = this._outputLines.slice(outputBefore).join("");

    if (this._state.stopped) {
      return { stopped: true, reason: this._state.stopReason, output: newOutput };
    } else {
      return { stopped: false, output: newOutput };
    }
  }

  /**
   * Step into
   */
  async stepInto(): Promise<{ stopped: boolean; reason?: string; output?: string }> {
    const outputBefore = this._outputLines.length;

    const response = await this.sendRequest("stepIn", { threadId: 0 });
    if (!response.success) {
      throw new Error(`StepIn failed: ${response.message}`);
    }

    try {
      await Promise.race([
        this.waitForEvent("stopped", 60000),
        this.waitForEvent("terminated", 60000),
      ]);
    } catch {
      // Timeout
    }

    const newOutput = this._outputLines.slice(outputBefore).join("");

    if (this._state.stopped) {
      return { stopped: true, reason: this._state.stopReason, output: newOutput };
    } else {
      return { stopped: false, output: newOutput };
    }
  }

  /**
   * Step out
   */
  async stepOut(): Promise<{ stopped: boolean; reason?: string; output?: string }> {
    const outputBefore = this._outputLines.length;

    const response = await this.sendRequest("stepOut", { threadId: 0 });
    if (!response.success) {
      throw new Error(`StepOut failed: ${response.message}`);
    }

    try {
      await Promise.race([
        this.waitForEvent("stopped", 60000),
        this.waitForEvent("terminated", 60000),
      ]);
    } catch {
      // Timeout
    }

    const newOutput = this._outputLines.slice(outputBefore).join("");

    if (this._state.stopped) {
      return { stopped: true, reason: this._state.stopReason, output: newOutput };
    } else {
      return { stopped: false, output: newOutput };
    }
  }

  /**
   * Get stack trace
   */
  async stackTrace(): Promise<StackFrame[]> {
    const response = await this.sendRequest("stackTrace", { threadId: 0 });
    if (!response.success) {
      throw new Error(`StackTrace failed: ${response.message}`);
    }

    const frames = (response.body?.stackFrames ?? []) as Array<{
      id: number;
      name: string;
      source?: { path?: string };
      line: number;
      column: number;
    }>;

    this._currentStackFrames = frames.map((f) => ({
      id: f.id,
      name: f.name,
      source: f.source?.path,
      line: f.line,
      column: f.column,
    }));

    // Update current position
    if (this._currentStackFrames.length > 0) {
      const top = this._currentStackFrames[0];
      this._state.currentFile = top.source;
      this._state.currentLine = top.line;
    }

    return this._currentStackFrames;
  }

  /**
   * Get variables for a scope/frame
   */
  async variables(variablesReference?: number): Promise<DebugVariable[]> {
    // If no reference, get scopes first then locals
    if (variablesReference === undefined) {
      const stackFrames = await this.stackTrace();
      if (stackFrames.length === 0) return [];

      const frameId = stackFrames[0].id;

      const scopesResponse = await this.sendRequest("scopes", {
        frameId,
      });

      if (!scopesResponse.success) return [];

      const scopes = scopesResponse.body?.scopes as Array<{
        name: string;
        variablesReference: number;
      }>;

      if (scopes && scopes.length > 0) {
        variablesReference = scopes[0].variablesReference;
      } else {
        return [];
      }
    }

    const response = await this.sendRequest("variables", { variablesReference });
    if (!response.success) {
      throw new Error(`Variables failed: ${response.message}`);
    }

    const vars = (response.body?.variables ?? []) as Array<{
      name: string;
      value: string;
      variablesReference: number;
    }>;

    this._currentVariables = vars.map((v) => ({
      name: v.name,
      value: v.value,
      variablesReference: v.variablesReference,
    }));

    return this._currentVariables;
  }

  /**
   * Evaluate an expression
   */
  async evaluate(
    expression: string,
    frameId?: number
  ): Promise<{ result: string; variablesReference: number }> {
    const args: Record<string, unknown> = { expression };
    if (frameId !== undefined) {
      args.frameId = frameId;
    }

    const response = await this.sendRequest("evaluate", args);
    if (!response.success) {
      throw new Error(`Evaluate failed: ${response.message}`);
    }

    return {
      result: (response.body?.result as string) ?? "",
      variablesReference: (response.body?.variablesReference as number) ?? 0,
    };
  }

  /**
   * Disconnect from debug session
   */
  async disconnect(): Promise<{ output: string }> {
    const finalOutput = this.output;

    try {
      await this.sendRequest("disconnect", { terminateDebuggee: true }, 5000);
    } catch {
      // Ignore disconnect errors
    }

    this.kill();
    return { output: finalOutput };
  }

  /**
   * Kill the DAP process
   */
  kill(): void {
    if (this.process && !this.process.killed) {
      this.process.kill();
      this.process = null;
    }

    // Clear pending requests
    for (const [seq, pending] of this.pendingRequests) {
      clearTimeout(pending.timer);
      pending.reject(new Error("Session terminated"));
    }
    this.pendingRequests.clear();
  }

  /**
   * Full debug session: initialize → launch → setBreakpoints → configurationDone
   */
  async startSession(
    program: string,
    breakpoints?: Array<{ file: string; lines: DebugBreakpoint[] }>,
    stopOnEntry = false
  ): Promise<{
    status: string;
    breakpoints?: Array<{ verified: boolean; line: number }[]>;
    error?: string;
  }> {
    try {
      await this.connect();
      await this.initialize();

      // Set breakpoints before launch if provided
      let bpResults: Array<{ verified: boolean; line: number }[]> | undefined;
      if (breakpoints && breakpoints.length > 0) {
        bpResults = [];
        for (const bp of breakpoints) {
          const result = await this.setBreakpoints(bp.file, bp.lines);
          bpResults.push(result);
        }
      }

      await this.launch(program, stopOnEntry);
      await this.configurationDone();

      const status = this._state.terminated
        ? "program terminated"
        : this._state.stopped
          ? `stopped (${this._state.stopReason})`
          : "running";

      return { status, breakpoints: bpResults };
    } catch (err) {
      this.kill();
      return {
        status: "error",
        error: err instanceof Error ? err.message : String(err),
      };
    }
  }
}
