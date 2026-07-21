# InterfaceDefaultCallsDefaultTest
## Description
An interface default method calls another default method on the same interface
via `this`. Verifies that default-method self-calls dispatch correctly (the
default calling another default).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IGreeter {
            fun greet(this: IGreeter) -> string {
                return this.name();
            }
            fun name(this: IGreeter) -> string {
                return "anonymous";
            }
        }

        class Foo {
            impl IGreeter {
                fun name(this: IGreeter) -> string {
                    return "Foo";
                }
            }
        }

        initial {
            let g : IGreeter = cast<IGreeter>(new Foo());
            print(g.greet());
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
ExpectedStdout: EQUALS `Foo`
ExpectedStderr: DISCARD
