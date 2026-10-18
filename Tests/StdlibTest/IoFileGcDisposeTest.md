# IoFileGcDisposeTest
## Description
std.io.File closes EXPLICITLY (close()); an open handle that becomes unreachable leaks to process exit (the IMemoryDispose/finalizer hook is retired with the native-arrays GC simplification). Dropping the reference and forcing gc_collect must not crash, and a later reader opening its own handle still reads intact contents.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun leaky_read(path: string) -> string {
    let f: mut std.io.File = std.io.open(path, "r");
    let l = f.read_line();
    if (l.is_some()) { return l.some; }
    return "";
}

initial {
    std.io.write_text("gc.txt", "data");
    std.io.println("l:" + leaky_read("gc.txt"));
    gc_collect();
    let r: mut std.io.File = std.io.open("gc.txt", "r");
    let again = r.read_line();
    if (again.is_some()) { std.io.println("again:" + again.some); }
    r.close();
    std.io.println("ok");
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
ExpectedStdout: EQUALS `l:data
again:data
ok
`
ExpectedStderr: DISCARD
