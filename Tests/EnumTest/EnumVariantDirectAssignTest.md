# EnumVariantDirectAssignTest
## Description
Directly assign a new value to an enum variant's contained data. Tests the compiler generates WRENUM instead of WRMBR.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Foo {
            x: mut i64 = 0;
        }

        enum E {
            a : mut Foo;
        }

        initial {
            let e : mut E = new E.a(new Foo());
            e.a.x = 42;
            print(cast<string>(e.a.x));
            e.a = new Foo();
            print(cast<string>(e.a.x));
            e.a.x = 99;
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
ExpectedStdout: EQUALS `42099`
ExpectedStderr: DISCARD
