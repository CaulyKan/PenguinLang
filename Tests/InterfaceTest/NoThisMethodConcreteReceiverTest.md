# NoThisMethodConcreteReceiverTest
## Description
A class member function whose first parameter is NOT `this` (a "static"-style
method) called via a concrete-typed receiver `c.foo()`. The receiver is
discarded (not passed), and the override in the concrete class is selected.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IFoo {
            fun foo() {
                print("IFoo.foo");
            }
        }

        class Foo {
            impl IFoo {
                fun foo() {
                    print("Foo.foo");
                }
            }
        }

        initial {
            let c : Foo = new Foo();
            c.foo();
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
ExpectedStdout: EQUALS `Foo.foo`
ExpectedStderr: DISCARD
