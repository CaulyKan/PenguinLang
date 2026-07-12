# EnumGenericTest
## Description
Generic enum with type parameter: Test<T> with a and b(T) variants.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : mut Test<u8> = new Test<u8>.a();
            if (test is Test<u8>.a) {
                print("a");
            }
            test = new Test<u8>.b(2);
            if (test is Test<u8>.b) {
                print(cast<string>(test.b));
            } else if (test is Test<u8>.a) {
                print("not possible");
            }
        }

        #template(T: type)
        enum Test {
            a;
            b : T;
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
ExpectedStdout: EQUALS `a2`
ExpectedStderr: DISCARD
