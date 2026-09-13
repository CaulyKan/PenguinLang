# WaitIntShortForm
## Description
`wait <int>;` — the short timer form (`wait n tick;` with the `tick` keyword elided). Covers an integer LITERAL (`wait 1;`), an i64 VARIABLE (`wait n;`), a narrower i32 variable (the lowering casts it into _after's i64 deadline), and a computed expression — each wait advances the simulation clock by exactly that many ticks, verified with `_sim_now()` after every step. Also asserts the clock does NOT move before the first wait.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    let n: i64 = 2;
    let k: i32 = 3;
    wait n;
    println("a:" + cast<string>(_sim_now()));
    wait k;
    println("b:" + cast<string>(_sim_now()));
    wait 1;
    println("c:" + cast<string>(_sim_now()));
    wait (n + k);
    println("d:" + cast<string>(_sim_now()));
    wait (n + 1) tick;
    println("e:" + cast<string>(_sim_now()));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a:2
b:5
c:6
d:11
e:14
`
ExpectedStderr: DISCARD
