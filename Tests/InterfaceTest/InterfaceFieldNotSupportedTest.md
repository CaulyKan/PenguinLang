# InterfaceFieldNotSupportedTest
## Description
Negative test: EmperorPenguin does not support interface field declarations
(the feature is BabyPenguin-VM only). An interface with a field (`a : u8 = 1;`)
must fail to compile with E_PARSE. BabyPenguin VM still supports fields, so this
test only applies to EmperorPenguin.

## Apply To
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
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `Interface field declarations are not supported`
