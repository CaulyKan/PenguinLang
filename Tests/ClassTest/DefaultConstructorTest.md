# ClassDefaultConstructorTest
## Description
Default constructor initializes fields with default values.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : Test = new Test();
            test.print_sum();
        }

        class Test {
            a : u8=1;
            b : u8=1+1;

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
ExpectedStdout: EQUALS `3`
ExpectedStderr: DISCARD
