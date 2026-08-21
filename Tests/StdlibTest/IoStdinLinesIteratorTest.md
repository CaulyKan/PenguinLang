# IoStdinLinesIteratorTest
## Description
std.io.stdin_lines() returns a lazy IIterator<string> over stdin; for-in desugars onto it (RangeIterator shape: bare next + iter + impl IIterator). One line per next(), exhaustion matches std.io.read_line.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    for (let line in std.io.stdin_lines()) {
        std.io.println("got:" + line);
    }
    std.io.println("done");
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
Stdin: `p\nq\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `got:p
got:q
done
`
ExpectedStderr: DISCARD
