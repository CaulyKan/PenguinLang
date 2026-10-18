# BoxForwardNotForwarded
## Description
The negative side of Box interface forwarding: an interface T does NOT implement is not forwarded (Box<i64> does not become IOther), and runtime-marker interfaces (ICopy/IValueType/...) are excluded by design — forwarding ICopy would flip Box's reference semantics.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IOther { fun q(this) -> i64; }

initial {
    let a: mut Box<i64> = new Box<i64>(7);
    println(cast<string>(a is IHash));
    println(cast<string>(a is IOther));
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
ExpectedStdout: EQUALS `true
false
`
ExpectedStderr: DISCARD
