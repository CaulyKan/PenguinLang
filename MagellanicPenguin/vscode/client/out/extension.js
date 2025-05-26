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
let client;
// type a = Parameters<>;
const interpreter_program = new Map([["linux", "server/BabyPenguin"], ["win32", "server/BabyPenguin.exe"]]);
async function activate(context) {
    const traceOutputChannel = vscode_1.window.createOutputChannel("Brainfuck Language Server Client");
    // If the extension is launched in debug mode then the debug server options are used
    // Otherwise the run options are used
    // Options to control the language client
    const debugTraceOutputChannel = vscode_1.window.createOutputChannel("penguinlang DAP");
    vscode_1.debug.registerDebugAdapterTrackerFactory('penguinlang', {
        createDebugAdapterTracker(session) {
            return {
                onWillReceiveMessage: m => debugTraceOutputChannel.appendLine(`> ${JSON.stringify(m, undefined, 2)}`),
                onDidSendMessage: m => debugTraceOutputChannel.appendLine(`< ${JSON.stringify(m, undefined, 2)}`)
            };
        }
    });
    const interpreter = context.asAbsolutePath(interpreter_program.get((0, os_1.platform)()));
    async function launch_interpreter(jit_config) {
        const file = vscode_1.window.activeTextEditor?.document.fileName;
        if ((0, fs_1.existsSync)(file)) {
            const term = await createTerminal();
            term.show();
            term.sendText(interpreter + " --mode=" + jit_config + " --file=\"" + file + "\"");
        }
        else {
            vscode_1.window.showErrorMessage("Please open a valid .bf file.");
        }
    }
}
exports.activate = activate;
async function createTerminal() {
    const name = "Brainfuck/Launch";
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