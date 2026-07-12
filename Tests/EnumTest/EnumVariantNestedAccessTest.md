# EnumVariantNestedAccessTest
## Description
Enum containing a class with another class field, nested access and mutation.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Inner {
            val: mut i64 = 0;
        }

        class Outer {
            inner: mut Inner = new Inner();
        }

        #template(T: type)
        enum E {
            a : T;
        }

        initial {
            let e : mut E<mut Outer> = new E<mut Outer>.a(new Outer());
            e.a.inner.val = 77;
            print(cast<string>(e.a.inner.val));
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
ExpectedStdout: EQUALS `77`
ExpectedStderr: DISCARD
