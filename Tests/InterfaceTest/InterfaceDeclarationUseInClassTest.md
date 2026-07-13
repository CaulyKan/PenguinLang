# InterfaceDeclarationUseInClassTest
## Description
Class method uses interface cast to access interface field.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IFoo {
            a : u8 = 1;
            fun foo(this: IFoo) {
                print(cast<string>(this.a));
            }
        }

        class Foo {
            impl IFoo;
            fun bar(this: Foo) {
                let f : IFoo = cast<IFoo>(this);
                print(cast<string>(f.a));
            }
        }
    
        initial {
            let f : Foo = new Foo();
            f.bar();
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
