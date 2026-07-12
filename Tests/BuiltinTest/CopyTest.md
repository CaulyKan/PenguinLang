# CopyTest
## Description
ICopy.copy() on a custom class implementing ICopy<Self>.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Foo {
        x : i64;
        y : i64;

        impl ICopy<Self>;
    }
    initial {
        let a : mut Foo = new Foo();
        a.x = 1;
        a.y = 2;
        let b : mut Foo = a.copy();
        b.x = 3;
        b.y = 4;
        print(cast<string>(a.x));
        print(cast<string>(a.y));
        print(cast<string>(b.x));
        print(cast<string>(b.y));
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
ExpectedStdout: EQUALS `1234`
ExpectedStderr: DISCARD
