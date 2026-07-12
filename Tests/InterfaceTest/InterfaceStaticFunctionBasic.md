# InterfaceStaticFunctionBasic
## Description
Static function call on interface itself.

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
        
        initial {
            IFoo.foo();
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
