# InterfaceStaticFunctionAsMemberCallOverrideTest
## Description
Interface variable calling overridden static function as member.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IFoo {
            fun foo() {
                print("IFoo.foo");
            }
        }
        
        class Foo {
            impl IFoo {
                fun foo() {
                    print("Foo.foo");
                }
            }
        }
    
        initial {
            let f : IFoo = new Foo();
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
ExpectedStdout: EQUALS `Foo.foo`
ExpectedStderr: DISCARD
