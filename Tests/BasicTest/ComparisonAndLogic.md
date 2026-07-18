# ComparisonAndLogic
## Description
Comparison operators (<, >, >=, ==, !=), logical NOT, and bitwise AND/OR.

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
        println(cast<string>(1 < 2));
        println(cast<string>(3 >= 3));
        println(cast<string>(2 == 2));
    }
}
namespace __c2 {
    initial {
        println(cast<string>(1 > 2));
        println(cast<string>(3 != 3));
    }
}
namespace __c3 {
    initial {
        println(cast<string>(!true));
        println(cast<string>(!false));
    }
}
namespace __c4 {
    initial {
        println(cast<string>(12 & 10));
        println(cast<string>(12 | 10));
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
ExpectedStdout: EQUALS `true
true
true
false
false
false
true
8
14
`
ExpectedStderr: DISCARD
