# InterfaceStaticFunctionTest
## Description
Static function call on class that implements interface (uses interface default).

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
            impl IFoo;
        }
    
        initial {
            Foo.foo();
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
ExpectedStdout: EQUALS `IFoo.foo`
ExpectedStderr: DISCARD
