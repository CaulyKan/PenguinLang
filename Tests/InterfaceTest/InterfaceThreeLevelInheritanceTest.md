# InterfaceThreeLevelInheritanceTest
## Description
Three-level interface inheritance: IBaz implements IBar implements IFoo. A
class implements IBaz, transitively satisfying both IBar and IFoo. Calls
through the IFoo reference dispatch to the override provided by IBar.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IFoo {
            fun foo(this: IFoo) -> i64;
        }

        interface IBar {
            impl IFoo {
                fun foo(this: IFoo) -> i64 {
                    return 2;
                }
            }
            fun bar(this: IBar) -> i64 {
                return 3;
            }
        }

        interface IBaz {
            impl IBar {
                fun bar(this: IBar) -> i64 {
                    return 4;
                }
            }
        }

        class Foo {
            impl IBaz;
        }

        initial {
            let f : Foo = new Foo();
            let ff : IFoo = cast<IFoo>(f);
            let fb : IBar = cast<IBar>(f);
            print(cast<string>(ff.foo()));
            print(cast<string>(fb.bar()));
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
ExpectedStdout: EQUALS `24`
ExpectedStderr: DISCARD
