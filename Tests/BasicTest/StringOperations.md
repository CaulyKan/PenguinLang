# StringOperations
## Description
String concatenation with +, string interpolation of integers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    initial {
        let s: string = "hello" + " " + "world";
        println(s);
    }
}
namespace __c2 {
    initial {
        let x: i64 = 42;
        println("x=" + cast<string>(x));
    }
}
namespace __c3 {
    initial {
        let a: string = "a" + "b" + "c";
        println(a);
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
ExpectedStdout: EQUALS `hello world
x=42
abc
`
ExpectedStderr: DISCARD
