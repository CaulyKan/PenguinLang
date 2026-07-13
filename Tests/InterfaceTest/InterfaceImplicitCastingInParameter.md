# InterfaceImplicitCastingInParameter
## Description
Implicit casting when passing class to function expecting interface parameter.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        interface IFoo {
            fun foo(this: IFoo) {
                print("1");
            }
            fun foo2(this: Foo) {
                print(cast<string>(this.a));
            }
        }

        class Foo {
            impl IFoo;
            a : u8 = 2;
        }

        fun test(a : Foo, b: IFoo) {}
    
        initial {
            let f : Foo = new Foo();
            test(f,f);
            f.foo();
            f.foo2();
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
