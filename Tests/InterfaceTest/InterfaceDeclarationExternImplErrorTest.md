# InterfaceDeclarationExternImplErrorTest
## Description
Compile error: extern impl (impl IFace for Class) not supported.
BabyPenguin: interface has field declarations so `impl for` is rejected with
E_INTERNAL. EmperorPenguin (pass1/pass2/pass3): should turn green once the IR
lowering for `impl_for_def` is fully tested — the semantic model now injects
the vtable into the target type (process_impl_for), and IRGenerator lowers the
methods. Root cause: BabyPenguin's `HasDeclartion` check.

## Apply To
* BabyPenguin
* BabyPenguin CS
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
ExpectedStderr: CONTAINS `E_INTERNAL`
