# EnumVariantMutableAccess_NonGenericTest
## Description
Modifying a class field through enum variant access (non-generic enum).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Foo {
            x: mut i64 = 0;
        }

        enum E {
            a : Foo;
        }

        initial {
            let e : mut E = new E.a(new Foo());
            print(cast<string>(e.a.x));
            e.a.x = 42;
            print(cast<string>(e.a.x));
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
ExpectedStdout: EQUALS `042`
ExpectedStderr: DISCARD
