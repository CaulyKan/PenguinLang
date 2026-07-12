# InterfaceStaticFunctionAmbiguousTest
## Description
Compile error: ambiguous static function call when class implements two interfaces with same function.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IB {
            fun foo() {
                print("IB.foo");
            }
        }

        interface IC {
            fun foo() {
                print("IC.foo");
            }
        }

        class Foo {
            impl IB;
            impl IC;
        }

        initial {
            Foo.foo();
        }
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
