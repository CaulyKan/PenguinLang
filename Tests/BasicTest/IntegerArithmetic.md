# IntegerArithmetic
## Description
Basic integer arithmetic operations: add, subtract, multiply, divide, modulo, precedence.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    initial {
        println(cast<string>(3 + 4));
    }
}
namespace __c2 {
    initial {
        println(cast<string>(10 - 3));
        println(cast<string>(3 * 4));
    }
}
namespace __c3 {
    initial {
        println(cast<string>(10 / 3));
        println(cast<string>(10 % 3));
    }
}
namespace __c4 {
    initial {
        println(cast<string>(2 + 3 * 4));
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
ExpectedStdout: EQUALS `7
7
12
3
1
14
`
ExpectedStderr: DISCARD
