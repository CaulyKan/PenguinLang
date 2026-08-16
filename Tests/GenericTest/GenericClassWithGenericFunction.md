# GenericClassWithGenericFunction
## Description
Generic class with a generic method that casts from U to T.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    #template(T: type)
    class Foo {
        v: T;

        #template(U: type)
        fun foo(mut this, x: U) {
            this.v = cast<T>(x);
        }
    }
    initial {
        let mut a = new Foo<i32>();
        a.foo<i64>(123);
        println(cast<string>(a.v));
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
ExpectedStdout: EQUALS `123
`
ExpectedStderr: DISCARD
