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
} from "vscode-languageclient/node";

let client: LanguageClient;
// type a = Parameters<>;

const interpreter_program: Map<string, string> = new Map([["linux", "/home/cauly/workspace/repos/penguinlang/BabyPenguin/bin/Debug/net8.0/BabyPenguin"], ["win32", "X:\\repos\\penguinlang\\BabyPenguin\\bin\\Debug\\net8.0\\BabyPenguin"]]);
export async function activate(context: ExtensionContext) {
	const traceOutputChannel = window.createOutputChannel("Brainfuck Language Server Client");


	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	// Options to control the language client

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
	const name = "Brainfuck/Launch";
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
