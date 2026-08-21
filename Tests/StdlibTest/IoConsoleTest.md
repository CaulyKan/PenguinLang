# IoConsoleTest
## Description
io console wrappers: print/println to stdout (byte-exact, including no-newline prints) and eprint/eprintln to stderr. Exercises the `io` namespace auto-loaded from `EmperorPenguin/std/penguin/io.penguin` (pass3+ native runtime).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    std.io.print("a");
    std.io.print("b");
    std.io.println("c");
    std.io.println("d");
    std.io.eprint("E");
    std.io.eprintln("F");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `abc
d
`
ExpectedStderr: EQUALS `EF
`
