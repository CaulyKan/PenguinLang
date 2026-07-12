# InterfaceStaticFunctionBasicWithNamespace
## Description
Static function on interface vs overridden on class, called with namespace qualifier.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
    }
    
    initial {
        ns.IFoo.foo();
        ns.Foo.foo();
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
ExpectedStdout: EQUALS `IFoo.fooFoo.foo`
ExpectedStderr: DISCARD
