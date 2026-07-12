# InterfaceImplementationOverride
## Description
Override interface methods in nested implementation.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
                fun foo2(this: IFoo<T>) -> T {
                    return 2;
                }
            }
        }
        
        class Foo {
            impl IFoo<u8> {
                fun foo(this: IFoo<u8>) -> u8 {
                    return 3;
                }
                fun foo2(this: IFoo<u8>) -> u8 {
                    return 3;
                }
            }
            a: u8 = 9;
        }
    
        initial {
            let f : Foo = new Foo();
            let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
            let f4 : IFoo<u8> = cast<IFoo<u8>>(f);
            print(cast<string>(f2.foo()));
            print(cast<string>(f2.foo2()));
            print(cast<string>(f4.foo()));
            print(cast<string>(f4.foo2()));
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
ExpectedStdout: EQUALS `3333`
ExpectedStderr: DISCARD
