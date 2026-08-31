# ProjectFileFlags
## Description
.penguins project files support flags = [...]: compiler options applied as if typed on the command line (project flags are parsed after the CLI; only '-'-prefixed tokens are honored, so a project can never inject sources). Here the project carries --enable-coroutine, and the compilation is invoked WITHOUT the CLI flag — the wait-tick program (which the gate would otherwise reject with E_UNSUPPORTED) builds and runs. Companion key lib = [...] (dependency dyn-libs, project-dir-relative) shares the same pre-scan; its end-to-end shape is covered by the DynamicLink suite via CLI --lib.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial { }
```

## Compile
Args: `Tests/LspTest/fixtures/projflags/proj.penguins`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `project-flags-ok
`
ExpectedStderr: DISCARD
