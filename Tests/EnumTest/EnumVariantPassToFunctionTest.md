# EnumVariantPassToFunctionTest
## Description
Pass enum variant value to a function that modifies it.

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
ExpectedStdout: EQUALS `55`
ExpectedStderr: DISCARD
