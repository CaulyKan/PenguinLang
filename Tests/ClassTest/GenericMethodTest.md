# GenericMethodTest
## Description
Generic class with a method that reads both type parameters.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(T: type, U: type)
        class Test {
            x: auto T;
            y: auto U;
            fun print_sum(this) {
                print(cast<string>(this.x + this.y));
            }
        }

        initial {
            let t : mut Test<u8, u16> = new Test<u8, u16>();
            t.x = 1;
            t.y = 2;
            t.print_sum();
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
