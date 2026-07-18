# OrphanImplInterfaceLocal
## Description
Orphan principle: `impl Interface for Type` where the interface is defined in the same namespace as the impl block (but the type is external). Should succeed because at least one (the interface) is local.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns1 {
        interface IFoo {
            fun foo(this: IFoo);
        }
    }

    namespace ns2 {
        class Bar {
        }
    }

    namespace ns1 {
        impl IFoo for ns2.Bar {
            fun foo(this: IFoo) {
                print("foo");
            }
        }

        initial {
            let b : ns2.Bar = new ns2.Bar();
            print("done");
        }
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0

## Run
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done`
ExpectedStderr: DISCARD
