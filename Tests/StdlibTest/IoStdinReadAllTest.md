# IoStdinReadAllTest
## Description
std.io.read_all slurps the whole stdin (newlines preserved verbatim) as one string.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let all: string = std.io.read_all();
    std.io.println("all:" + all);
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
Stdin: `l1\nl2\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `all:l1
l2

`
ExpectedStderr: DISCARD
