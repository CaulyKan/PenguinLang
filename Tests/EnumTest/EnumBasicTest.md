# EnumBasicTest
## Description
Basic enum with variant a (no payload) and b (u8 payload), matching via `is`, and reassignment.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : mut Test = new Test.a();
            if (test is Test.a) {
                print("a");
            }
            test = new Test.b(2);
            if (test is Test.b) {
                print(cast<string>(test.b));
            } else if (test is Test.a) {
                print("not possible");
            }
        }

        enum Test {
            a;
            b : u8;
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
