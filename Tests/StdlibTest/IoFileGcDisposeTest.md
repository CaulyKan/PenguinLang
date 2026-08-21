# IoFileGcDisposeTest
## Description
std.io.File implements IMemoryDispose: a File that goes out of scope is closed by the GC finalizer when swept (the emitter points the class metadata destructor slot at dispose_mem). Opening, reading, dropping the reference and forcing gc_collect must not crash and the file contents stay intact for a later reader.

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
