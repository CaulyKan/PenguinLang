# FramerSplitFrames
## Description
JSON-RPC Content-Length frame reassembly unit test (LSP transport layer): frames split across chunk boundaries, several frames packed into one chunk, lowercase `content-length` spelling and the lenient `\n\n` terminator. (Pass3-only: the test needs LspFraming.penguin + vector.penguin as extra Compile.Args sources, and the BabyPenguin backend ignores Compile.Args by design.) Feed 1 delivers a header whose body is still partial (0 frames); feed 2 completes it and unpacks a second frame (2 frames: "hello", "ok"); feed 3 uses lowercase + `\n\n` (1 frame: "abc").

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let f : mut lsp.Framer = new lsp.Framer();
    let a : mut std.Vector<string> = f.feed("Content-Length: 5\r\n\r\nhel");
    println(cast<string>(a.size()));
    let b : mut std.Vector<string> = f.feed("lo\r\nContent-Length: 2\r\n\r\nok");
    println(cast<string>(b.size()));
    println(b.at(0).some);
    println(b.at(1).some);
    let c : mut std.Vector<string> = f.feed("content-length: 3\n\nabc");
    println(cast<string>(c.size()));
    println(c.at(0).some);
}
```

## Compile
Args: `MagellanicPenguin/LspServer/LspFraming.penguin EmperorPenguin/std/penguin/vector.penguin`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `0
2
hello
ok
1
abc
`
ExpectedStderr: DISCARD
