# ClassMethodWithReturnedOwnerTest
## Description
Call a method on the result of a function call: (foo()).print_sum().

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        (foo()).print_sum();
    }

    class Test {
        a : u8;
        b : u8;
        fun print_sum(this) {
            print(cast<string>(this.a + this.b));
        }
    }

    fun foo() -> Test {
        let test : mut Test = new Test();
        test.a = 1;
        test.b = 1;
        return test;
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
ExpectedStdout: EQUALS `2`
ExpectedStderr: DISCARD
