# StdioStreamEcho
## Description
StdioStream module e2e (LSP stdio boundary): stdin chunks flow out of StdioStream.rx through an Echo module (prefixes each chunk) back into StdioStream.tx as outbound frames; the writer loop writes them to fd 1 with backpressure parking, and stdin EOF ends the reader so the whole program drains and exits at quiescence. Exercises: string port payloads (both directions), two-module construct wiring (module output→input, module output→module input), and the EOF → park-on-dead-source → quiescent-exit cascade. Also the regression lock for the connect-time seeding bug this test caught: _Fanout.subscribe delivered an output port's synthesized TYPE-ZERO seed (deliver=false, bare current() reads only) as a wire DELIVERABLE, so every subscriber woke at time 0 with a garbage empty transaction — the echo prefix doubled ('[echo][echo]abc' = '[echo]'+'' followed by '[echo]'+'abc'). subscribe now consults LatestChannel.has_live_value (ever_written || seed_deliver): declared defaults and variable nets still deliver at time 0, payload zeros do not. Same fix applied to BabyPenguin's Builtin.penguin (source-compatible).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Echo {
    input c : string;
    output f : string;
    initial {
        while (true) {
            let s : string = wait this.c;
            this.f.write("[echo]" + s);
        }
    }
}

construct {
    let io : mut lsp.StdioStream = new lsp.StdioStream();
    let e : mut Echo = new Echo();
    connect(io.rx, e.c);
    connect(e.f, io.tx);
}
```

## Compile
Args: `--enable-coroutine MagellanicPenguin/LspServer/StdioStream.penguin`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: `abc`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `[echo]abc`
ExpectedStderr: DISCARD
