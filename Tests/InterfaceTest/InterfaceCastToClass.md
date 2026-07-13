# InterfaceCastToClass
## Description
Cast interface to class and access class field.

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
            fun bar(this: IFoo<T>) -> T {
                return 1;
            }
        }
        
        class Foo {
            a: u8 = 9;
            impl IFoo<u8> {
                fun foo(this: IFoo<u8>) -> u8 {
                    let f : Foo = cast<Foo>(this);
                    return f.a;
                }
            }
        }
    
        initial {
            let f : Foo = new Foo();
            let f2 : IFoo<u8> = cast<IFoo<u8>>(f);
            print(cast<string>(f2.foo()));
            print(cast<string>(f2.bar()));
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
ExpectedStdout: EQUALS `91`
ExpectedStderr: DISCARD
