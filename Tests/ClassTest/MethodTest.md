# ClassMethodTest
## Description
Class method that reads fields via `this`.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : mut Test = new Test();
            test.a = 1;
            test.b = 1;
            test.print_sum();
        }

        class Test {
            a : u8;
            b : u8;

            fun print_sum(this) {
                print(cast<string>(this.a + this.b));
            }
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
