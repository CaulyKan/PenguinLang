# InterfaceUpcastDowncast
## Description
Interface upcasting and downcasting through nested interface implementations.

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
            let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
            let f3 : IBar<u8> = cast<IBar<u8>>(f2);
            let f4 : IBar<u8> = cast<IBar<u8>>(f);
            let f5 : IFoo<u8> = cast<IFoo<u8>>(f3);
            let f6 : Foo = cast<Foo>(f4);
            print(cast<string>(f2.foo()));
            print(cast<string>(f2.foo2()));
            print(cast<string>(f3.bar()));
            print(cast<string>(f4.bar()));
            print(cast<string>(f5.foo()));
            print(cast<string>(f5.foo2()));
            print(cast<string>(f6.a));
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
ExpectedStdout: EQUALS `2133219`
ExpectedStderr: DISCARD
