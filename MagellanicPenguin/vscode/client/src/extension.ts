/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import {
	workspace,
	ExtensionContext,
	window,
	languages,
	debug,
	Terminal,
	DebugSession,
	TerminalOptions,
	commands,
} from "vscode";

import { platform } from 'os';
import { existsSync } from 'fs';
import { join } from 'path';

import {
	Executable,
	LanguageClient,
	LanguageClientOptions,
	RevealOutputChannelOn,
	ServerOptions,
	TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient;

// LSP server path configuration
const lspServerPath: Map<string, string> = new Map([
	["linux", "server/linux/MagellanicPenguinLSP"],
	["win32", "server\\windows\\MagellanicPenguinLSP.exe"]
]);

// EmperorPenguin compiler: the emperor driver script (links the LLVM-IR
// emitter binary that sits beside it). On windows a .bat, spawned through
// the user's terminal.
const emperorPenguinPath: Map<string, string> = new Map([
	["linux", "server/linux/emperor_penguin"],
	["win32", "server\\windows\\emperor_penguin.bat"]
]);

// Command to restart the language server
async function restartLanguageServer(context: ExtensionContext) {
	if (client) {
		await client.stop();
	}
	await startLanguageServer(context);
}

// Function to start the language server
async function startLanguageServer(context: ExtensionContext) {
	const traceOutputChannel = window.createOutputChannel("PenguinLang Language Server");

	const lspCommand = process.env.PENGUINLANG_LSPSERVER_PATH || context.asAbsolutePath(lspServerPath.get(platform()) || "");
	if (!lspCommand || !existsSync(lspCommand)) {
		window.showErrorMessage(
			`PenguinLang LSP server not found at "${lspCommand}". ` +
			`Run 'make lsp' (linux) or 'make lsp TARGET=win' (windows) and 'make publish' first.`
		);
		return;
	}

	// Server options
	const serverOptions: ServerOptions = {
		run: {
			command: lspCommand,
			transport: TransportKind.stdio,
		},
		debug: {
			command: lspCommand,
			transport: TransportKind.stdio,
		}
	};

	// Client options
	const clientOptions: LanguageClientOptions = {
		documentSelector: [{ scheme: 'file', language: 'penguinlang' }],
		revealOutputChannelOn: RevealOutputChannelOn.Never,
		traceOutputChannel
	};

	// Create and start the client
	client = new LanguageClient(
		'penguinlangvscode',
		'Penguin Language Server',
		serverOptions,
		clientOptions
	);

	// Start the client
	await client.start();
}

export async function activate(context: ExtensionContext) {
	// Register the restart command
	context.subscriptions.push(
		commands.registerCommand('penguinlangvscode.restartLanguageServer', () => restartLanguageServer(context))
	);

	// Register the "Run with Emperor Penguin" command
	context.subscriptions.push(
		commands.registerCommand('penguinlangvscode.runWithEmperorPenguin', async () => {
			const editor = window.activeTextEditor;
			if (!editor) {
				window.showErrorMessage('No active editor found');
				return;
			}
			const filePath = editor.document.uri.fsPath;
			if (!filePath.endsWith('.penguin') && !filePath.endsWith('.penguins')) {
				window.showErrorMessage('Current file is not a PenguinLang source (.penguin / .penguins)');
				return;
			}

			// Locate the emperor_penguin binary for the current platform
			const emperorPath = process.env.PENGUINLANG_EMPEROR_PATH || context.asAbsolutePath(emperorPenguinPath.get(platform()) || '');
			if (!emperorPath || !existsSync(emperorPath)) {
				window.showErrorMessage(`EmperorPenguin binary not found at "${emperorPath}". Run 'make publish' first.`);
				return;
			}

			const terminal = await createTerminal();
			terminal.show();
			terminal.sendText(`"${emperorPath}" "${filePath}"`);
		})
	);

	// Start the language server
	await startLanguageServer(context);

	// Register debug adapter tracker
	const debugTraceOutputChannel = window.createOutputChannel("penguinlang DAP");
	debug.registerDebugAdapterTrackerFactory('penguinlang', {
		createDebugAdapterTracker(session: DebugSession) {
			return {
				onWillReceiveMessage: m => debugTraceOutputChannel.appendLine(`> ${JSON.stringify(m, undefined, 2)}`),
				onDidSendMessage: m => debugTraceOutputChannel.appendLine(`< ${JSON.stringify(m, undefined, 2)}`)
			};
		}
	});
}

async function createTerminal(): Promise<Terminal> {
	const name = "PenguinLang/Launch";
	for (const term of window.terminals) {
		if (term.name == name) {
			return term;
		}
	}
	const options: TerminalOptions = {
		"name": name,
	};
	return window.createTerminal(options);
}

export function deactivate(): Thenable<void> | undefined {
	if (!client) {
		return undefined;
	}
	return client.stop();
}
