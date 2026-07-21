# InterfaceInheritedDefaultMethodTest
## Description
Interface inheritance: IBar implements IFoo (overriding foo) and a class
implements IBar. Calling foo() dispatches to IBar's override; calling foo2()
(IFoo's default, not overridden by IBar) uses the inherited default. Exercises
transitive vtable merge + default-method slots.

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
            fun foo2(this: IFoo) -> i64 {
                return 1;
            }
        }

        interface IBar {
            impl IFoo {
                fun foo(this: IFoo) -> i64 {
                    return 2;
                }
            }
        }

        class Foo {
            impl IBar;
        }

        initial {
            let f : Foo = new Foo();
            let fi : IFoo = cast<IFoo>(f);
            print(cast<string>(fi.foo()));
            print(cast<string>(fi.foo2()));
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
ExpectedStdout: EQUALS `21`
ExpectedStderr: DISCARD
