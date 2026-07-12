# InterfaceDeclarationExternImplErrorTest
## Description
Compile error: extern impl (impl IFace for Class) not supported.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
        }

        impl IFoo for Foo;
    
        initial {
            let f : Foo = new Foo();
            f.foo();
        }
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
