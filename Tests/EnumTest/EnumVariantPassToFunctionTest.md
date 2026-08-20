# EnumVariantPassToFunctionTest
## Description
Pass an enum variant payload to a function that modifies its parameter: the
by-value parameter is a COPY (value-copy semantics — `mut` on a plain parameter
is a permission to mutate the local copy, not to write through; only method
receivers `mut this` alias the caller's slot). `setX(e.a, 55)` does not change
the enum's payload — prints 0.

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

        fun setX(foo: mut Foo, val: i64) {
            foo.x = val;
        }

        #template(T: type)
        enum E {
            a : T;
        }

        initial {
            let e : mut E<mut Foo> = new E<mut Foo>.a(new Foo());
            setX(e.a, 55);
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
