# IoFileSeekTellTest
## Description
std.io.File.tell/read_all/seek: tell reports 0 at open, 10 after slurping a 10-byte file; seek(4) repositions absolutely from the start and read_all resumes from there.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    std.io.write_text("digits.txt", "0123456789");
    let f: mut std.io.File = std.io.open("digits.txt", "r");
    std.io.println("t0:" + cast<string>(f.tell()));
    let all: string = f.read_all();
    std.io.println("all:" + all);
    std.io.println("t1:" + cast<string>(f.tell()));
    if (f.seek(4)) {
        let rest: string = f.read_all();
        std.io.println("rest:" + rest);
    }
    f.close();
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
ExpectedStdout: EQUALS `t0:0
all:0123456789
t1:10
rest:456789
`
ExpectedStderr: DISCARD
