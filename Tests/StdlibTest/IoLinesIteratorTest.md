# IoLinesIteratorTest
## Description
std.io.lines(path) iterates a file's lines with for-in: an empty line mid-file yields some(""), a trailing empty piece is dropped, and line-final '\r' is stripped (CRLF tolerance). Exercises StringLineIterator (string_find_from/string_substring on "\n" literals — real unescaped newlines on both compilers).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    std.io.write_text("lines.txt", "one\n\ntwo");
    for (let line in std.io.lines("lines.txt")) {
        std.io.println("[" + line + "]");
    }
    std.io.write_text("crlf.txt", "a\r\nb\r\n");
    for (let line in std.io.lines("crlf.txt")) {
        std.io.println("<" + line + ">");
    }
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
ExpectedStdout: EQUALS `[one]
[]
[two]
<a>
<b>
`
ExpectedStderr: DISCARD
