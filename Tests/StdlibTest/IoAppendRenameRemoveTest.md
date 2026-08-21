# IoAppendRenameRemoveTest
## Description
append_text extends an existing file (and creates it when absent), rename moves it (content preserved), remove deletes it and exists flips to false.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    std.io.write_text("a.txt", "abc");
    if (std.io.append_text("a.txt", "def")) { std.io.println("appended"); }
    std.io.println("t:" + std.io.read_text("a.txt").some);
    if (std.io.rename("a.txt", "b.txt")) {
        std.io.println("r:" + std.io.read_text("b.txt").some);
    }
    if (std.io.remove("b.txt")) {
        std.io.println("e:" + cast<string>(std.io.exists("b.txt")));
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
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `appended
t:abcdef
r:abcdef
e:false
done
`
ExpectedStderr: DISCARD
