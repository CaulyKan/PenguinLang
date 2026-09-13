# FunIndirectStringReturn
## Description
Calling through a fun value whose return type is `string` (`ref<string>`, a pointer scalar on EmperorPenguin) — locks the thunk-forwarding path for non-integer returns and string concatenation inside the callee.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun greet(name: string) -> string {
    return "hello " + name;
}
initial {
    let f : fun<string, string> = greet;
    println(f("world"));
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
ExpectedStdout: EQUALS `hello world
`
ExpectedStderr: DISCARD
