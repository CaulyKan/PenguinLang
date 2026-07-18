# RvoTest
## Description
Return value optimization: function returning a class with three fields, then discarding the result.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Foo {
        x: i64;
        y: i64;
        z: i64;
    }
    fun test() -> Foo {
        let f : mut Foo = new Foo();
        f.x = 1;
        return f;
    }
    fun test2() -> Foo {
        let f : Foo = test();
        return f;
    }
    initial {
        let f : Foo = test2();
        print(cast<string>(f.x));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
