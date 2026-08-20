# EnumVariantAssignToMutableVariableTest
## Description
Assign enum variant payload to a mutable variable and modify it: the extraction
COPIES the value-class payload (value-copy semantics — mut is a permission, not
storage identity), so `foo.x = 99` writes foo's own copy and the enum's payload
is unchanged — prints 0. Chain writes through the slot (`e.a.x = 99`) would
still stick (lvalue addressing); only BINDING extraction copies.

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
ExpectedStdout: EQUALS `0`
ExpectedStderr: DISCARD
