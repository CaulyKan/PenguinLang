# OrphanImplBothExternal
## Description
Orphan principle: `impl Interface for Type` where both the interface and the target type are defined in external namespaces. Should report E_ORPHAN_IMPL.

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

    namespace ns3 {
        impl ns1.IFoo for ns2.Bar {
            fun foo(this: ns1.IFoo) {
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
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_ORPHAN_IMPL`
