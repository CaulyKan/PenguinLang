# InterfaceDefaultMethod
## Description
Interface with default method using `fun new` in interface (EmperorPenguin-only feature).

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IGreet {
        fun new(mut this) {}
        fun greet(this) -> string {
            return "hello";
        }
    }
    class Foo {
        fun new(mut this) {}
        impl IGreet {}
    }
    initial {
        let f = new Foo();
        println(f.greet());
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
ExpectedStdout: EQUALS `hello
`
ExpectedStderr: DISCARD
