# EnumGenericCustomTypeTest
## Description
Generic enum with custom class type parameter, variant access and field mutation.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        initial {
            let test : mut Test<mut Foo> = new Test<mut Foo>.a();
            if (test is Test<mut Foo>.a) {
                print("a");
            }
            test = new Test<mut Foo>.b(new Foo());
            if (test is Test<mut Foo>.b) {
                print(cast<string>(test.b.x));
                test.b.x = 1;
                print(cast<string>(test.b.x));
            } else if (test is Test<mut Foo>.a) {
                print("not possible");
            }
        }

        class Foo {
            x: u8 = 0;
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
ExpectedStdout: EQUALS `a01`
ExpectedStderr: DISCARD
