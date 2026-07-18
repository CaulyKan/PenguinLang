# ClassConstructorTest
## Description
Explicit constructor initializes fields from parameters, with default value for other fields.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : Test = new Test(2);
            test.print_sum();
        }

        class Test {
            a : u8=1;
            b : u8;

            fun print_sum(this) {
                print(cast<string>(this.a + this.b));
            }

            fun new(mut this, b: u8) {
                this.b = b;
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
