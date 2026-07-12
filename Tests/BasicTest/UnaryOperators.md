# UnaryOperators
## Description
Unary numeric and logical operators, including unary plus on strings in concatenation.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
namespace __c2 {
    initial {
        let name: string = "world";
        println("hello " + +name);
        println(+(cast<string>(42)));
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
hello world
42
`
ExpectedStderr: DISCARD
