# VariablesAndAssignment
## Description
Variable declaration, mutation, swap, and multiple variable usage.

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
        let x: mut i64 = 1;
        x = 42;
        println(cast<string>(x));
    }
}
namespace __c2 {
    initial {
        let a: mut i64 = 1;
        let b: mut i64 = 2;
        let t: mut i64 = a;
        a = b;
        b = t;
        println(cast<string>(a));
        println(cast<string>(b));
    }
}
namespace __c3 {
    initial {
        let x: i64 = 1000000;
        println(cast<string>(x + 1));
    }
}
namespace __c4 {
    initial {
        let a: i64 = 10;
        let b: i64 = 20;
        println(cast<string>(a + b));
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
ExpectedStdout: EQUALS `42
2
1
1000001
30
`
ExpectedStderr: DISCARD
