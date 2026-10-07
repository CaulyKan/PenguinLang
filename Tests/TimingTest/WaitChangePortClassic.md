# WaitChangePortClassic
## Description
The port-watching shape (`wait change(s.line)`) migrated to plain transaction waits, pinned on the CLASSIC native scheduler (EmperorPenguin Pass3, default scheduler.penguin): a port transaction wait delivers NO time-0 seed there (park-first; the first delivery is the first write), so two consecutive `wait s.line` consume write(false)@tick2 and write(true)@tick4 — "edge1" then "true". Under the aligned BabyPenguin scheduler the same program observes the declared seed first and prints "false" (see WaitChangePort); the two tests together pin both schedulers' observable difference.


## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Src {
    output line : bool = true;
    initial {
        wait 2;
        this.line.write(false);
        wait 2;
        this.line.write(true);
    }
}

construct {
    let s : mut Src = new Src();
}

initial {
    let v1 : bool = wait s.line;
    println("edge1");
    let v2 : bool = wait s.line;
    println(cast<string>(v2));
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `edge1
true
`
ExpectedStderr: DISCARD
