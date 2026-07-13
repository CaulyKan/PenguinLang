# EnumVariantAssignToMutableVariableTest
## Description
Assign enum variant value to a mutable variable, modify it, then check the original.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Foo {
            x: mut i64 = 0;
        }

        #template(T: type)
        enum E {
            a : T;
        }

        initial {
            let e : mut E<mut Foo> = new E<mut Foo>.a(new Foo());
            let foo : mut Foo = e.a;
            foo.x = 99;
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
ExpectedStdout: EQUALS `99`
ExpectedStderr: DISCARD
