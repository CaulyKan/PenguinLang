# OnEventClassTest
## Description
Event defined inside a class with `on` handler on instance.

## Apply To
* BabyPenguin

## Test Code
```
    class Foo {
        event test_event : i32;

        on this.test_event (b: i32) {
            print(cast<string>(b));
        }
        
        fun foo(this: Foo) {
            emit this.test_event(cast<i32>(1));
            emit this.test_event(cast<i32>(2));
        }
    }

    let f : Foo = new Foo();

    on f.test_event (b: i32) {
        print(cast<string>(b));
    }

    initial {
        f.foo();
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
ExpectedStdout: EQUALS `1122`
ExpectedStderr: DISCARD
