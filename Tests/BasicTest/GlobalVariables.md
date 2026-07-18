# GlobalVariables
## Description
Global variable declaration, mutation, namespace-qualified access, and arithmetic with globals.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    let x: i64 = 42;
    initial {
        println(cast<string>(x));
    }
}
namespace __c2 {
    let counter: mut i64 = 0;
    initial {
        println(cast<string>(counter));
        counter = 1;
        println(cast<string>(counter));
    }
}
namespace __c3 {
    namespace Foo {
        let msg: string = "hello";
    }
    initial {
        println(Foo.msg);
    }
}
namespace __c4 {
    let a: i64 = 10;
    let b: i64 = 20;
    initial {
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
0
1
hello
30
`
ExpectedStderr: DISCARD
