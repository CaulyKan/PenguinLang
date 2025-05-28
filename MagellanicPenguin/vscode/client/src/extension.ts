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
	["linux", "/home/cauly/workspace/repos/penguinlang/MagellanicPenguin/LSP/bin/Debug/net8.0/MagellanicPenguinLSP"],
	["win32", "Y:\\Workspace\\penguinlang\\MagellanicPenguin\\LSP\\bin\\Debug\\net8.0\\MagellanicPenguinLSP.exe"]
]);

// Command to restart the language server
async function restartLanguageServer() {
	if (client) {
		await client.stop();
	}
	await startLanguageServer();
}

// Function to start the language server
async function startLanguageServer() {
	const traceOutputChannel = window.createOutputChannel("PenguinLang Language Server");

	// Server options
	const serverOptions: ServerOptions = {
		run: {
			command: lspServerPath.get(platform()) || "",
			transport: TransportKind.stdio,
		},
		debug: {
			command: lspServerPath.get(platform()) || "",
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
		commands.registerCommand('penguinlangvscode.restartLanguageServer', restartLanguageServer)
	);

	// Start the language server
	await startLanguageServer();

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
