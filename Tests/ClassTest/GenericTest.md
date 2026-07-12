# GenericTest
## Description
Generic class with single type parameter, assigning to field of type T.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(T: type)
        class Test {
            x: auto T;
        }

        initial {
            let t : mut Test<u8> = new Test<u8>();
            t.x = 1;
            print(cast<string>(t.x));

            let t2 : mut Test<string> = new Test<string>();
            t2.x = "2";
            print(t2.x);
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
