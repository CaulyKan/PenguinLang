# InterfaceUpcastDowncastBare
## Description
Interface upcast and downcast without intermediate cast calls.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(T: type)
        interface IFoo {
            fun foo(this: IFoo<T>) -> T;
            fun foo2(this: IFoo<T>) -> T {
                return 1;
            }
        }

        #template(T: type)
        interface IBar {
            impl IFoo<T> {
                fun foo(this: IFoo<T>) -> T {
                    return 2;
                }
            }
            fun bar(this: IBar<T>) -> T {
                return 3;
            }
        }
        
        class Foo {
            impl IBar<u8>;
            a: u8 = 9;
        }
    
        initial {
            let f : Foo = new Foo();
            let fb : IBar<u8> = cast<IBar<u8>>(f);
            let f2 : IFoo<u8> = cast<IFoo<u8>>(fb);
            print(cast<string>(f2.foo()));
            print(cast<string>(f2.foo2()));
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
