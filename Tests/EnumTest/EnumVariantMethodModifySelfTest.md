# EnumVariantMethodModifySelfTest
## Description
Call a method on enum variant that modifies self (mut this).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Counter {
            count: mut i64 = 0;

            fun increment(this: mut Counter) {
                this.count = this.count + 1;
            }
        }

        #template(T: type)
        enum E {
            a : T;
        }

        initial {
            let e : mut E<mut Counter> = new E<mut Counter>.a(new Counter());
            e.a.increment();
            e.a.increment();
            print(cast<string>(e.a.count));
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
ExpectedStdout: EQUALS `2`
ExpectedStderr: DISCARD
