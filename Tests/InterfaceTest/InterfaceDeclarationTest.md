# InterfaceDeclarationTest
## Description
Interface with field declaration and method that uses the field.

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
        }

        initial {
            let f : Foo = new Foo();
            f.foo();
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
