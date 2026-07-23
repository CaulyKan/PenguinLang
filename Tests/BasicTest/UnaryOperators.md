# UnaryOperators
## Description
Unary numeric and logical operators on numeric and boolean types.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    initial {
        println(cast<string>(+5));
        println(cast<string>(+(-7)));
        println(cast<string>(-3));
        println(cast<string>(!true));
    }
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
ExpectedStdout: EQUALS `5
-7
-3
false
`
ExpectedStderr: DISCARD
