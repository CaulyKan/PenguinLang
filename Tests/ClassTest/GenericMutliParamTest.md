# GenericMutliParamTest
## Description
Generic class with two type parameters.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(T: type, U: type)
        class Test {
            x: auto T;
            y: auto U;
        }

        initial {
            let t : mut Test<u8, string> = new Test<u8, string>();
            t.x = 1;
            t.y = "2";
            print(cast<string>(t.x));
            print(cast<string>(t.y));
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
