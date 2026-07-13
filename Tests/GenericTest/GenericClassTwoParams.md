# GenericClassTwoParams
## Description
Generic class with two type parameters.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    #template(T: type, U: type)
    class Pair {
        first: T;
        second: U;
        fun new(mut this, a: T, b: U) {
            this.first = a;
            this.second = b;
        }
        fun get_first(this) -> T {
            return this.first;
        }
        fun get_second(this) -> U {
            return this.second;
        }
    }
    initial {
        let p = new Pair<i32, string>(1, "hello");
        println(cast<string>(p.get_first()));
        println(p.get_second());
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
ExpectedStdout: EQUALS `1
hello
`
ExpectedStderr: DISCARD
