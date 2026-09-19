"use strict";
/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
Object.defineProperty(exports, "__esModule", { value: true });
exports.deactivate = exports.activate = void 0;
const vscode_1 = require("vscode");
const os_1 = require("os");
const fs_1 = require("fs");
const node_1 = require("vscode-languageclient/node");
let client;
// LSP server path configuration
const lspServerPath = new Map([
    ["linux", "server/linux/MagellanicPenguinLSP"],
    ["win32", "server\\windows\\MagellanicPenguinLSP.exe"]
]);
// EmperorPenguin compiler: the emperor driver script (links the LLVM-IR
// emitter binary that sits beside it). On windows a .bat, spawned through
// the user's terminal.
const emperorPenguinPath = new Map([
    ["linux", "server/linux/emperor_penguin"],
    ["win32", "server\\windows\\emperor_penguin.bat"]
]);
// Command to restart the language server
async function restartLanguageServer(context) {
    if (client) {
        await client.stop();
    }
    await startLanguageServer(context);
}
// Function to start the language server
async function startLanguageServer(context) {
    const traceOutputChannel = vscode_1.window.createOutputChannel("PenguinLang Language Server");
    const lspCommand = process.env.PENGUINLANG_LSPSERVER_PATH || context.asAbsolutePath(lspServerPath.get((0, os_1.platform)()) || "");
    if (!lspCommand || !(0, fs_1.existsSync)(lspCommand)) {
        vscode_1.window.showErrorMessage(`PenguinLang LSP server not found at "${lspCommand}". ` +
            `Run 'make lsp' (linux) or 'make lsp TARGET=win' (windows) and 'make publish' first.`);
        return;
    }
    // Server options
    const serverOptions = {
        run: {
            command: lspCommand,
            transport: node_1.TransportKind.stdio,
        },
        debug: {
            command: lspCommand,
            transport: node_1.TransportKind.stdio,
        }
    };
    // Client options
    const clientOptions = {
        documentSelector: [{ scheme: 'file', language: 'penguinlang' }],
        revealOutputChannelOn: node_1.RevealOutputChannelOn.Never,
        traceOutputChannel
    };
    // Create and start the client
    client = new node_1.LanguageClient('penguinlangvscode', 'Penguin Language Server', serverOptions, clientOptions);
    // Start the client
    await client.start();
}
async function activate(context) {
    // Register the restart command
    context.subscriptions.push(vscode_1.commands.registerCommand('penguinlangvscode.restartLanguageServer', () => restartLanguageServer(context)));
    // Register the "Run with Emperor Penguin" command
    context.subscriptions.push(vscode_1.commands.registerCommand('penguinlangvscode.runWithEmperorPenguin', async () => {
        const editor = vscode_1.window.activeTextEditor;
        if (!editor) {
            vscode_1.window.showErrorMessage('No active editor found');
            return;
        }
        const filePath = editor.document.uri.fsPath;
        if (!filePath.endsWith('.penguin') && !filePath.endsWith('.penguins')) {
            vscode_1.window.showErrorMessage('Current file is not a PenguinLang source (.penguin / .penguins)');
            return;
        }
        // Locate the emperor_penguin binary for the current platform
        const emperorPath = process.env.PENGUINLANG_EMPEROR_PATH || context.asAbsolutePath(emperorPenguinPath.get((0, os_1.platform)()) || '');
        if (!emperorPath || !(0, fs_1.existsSync)(emperorPath)) {
            vscode_1.window.showErrorMessage(`EmperorPenguin binary not found at "${emperorPath}". Run 'make publish' first.`);
            return;
        }
        const terminal = await createTerminal();
        terminal.show();
        terminal.sendText(`"${emperorPath}" "${filePath}"`);
    }));
    // Start the language server
    await startLanguageServer(context);
    // Register debug adapter tracker
    const debugTraceOutputChannel = vscode_1.window.createOutputChannel("penguinlang DAP");
    vscode_1.debug.registerDebugAdapterTrackerFactory('penguinlang', {
        createDebugAdapterTracker(session) {
            return {
                onWillReceiveMessage: m => debugTraceOutputChannel.appendLine(`> ${JSON.stringify(m, undefined, 2)}`),
                onDidSendMessage: m => debugTraceOutputChannel.appendLine(`< ${JSON.stringify(m, undefined, 2)}`)
            };
        }
    });
}
exports.activate = activate;
async function createTerminal() {
    const name = "PenguinLang/Launch";
    for (const term of vscode_1.window.terminals) {
        if (term.name == name) {
            return term;
        }
    }
    const options = {
        "name": name,
    };
    return vscode_1.window.createTerminal(options);
}
function deactivate() {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
exports.deactivate = deactivate;
//# sourceMappingURL=extension.js.map