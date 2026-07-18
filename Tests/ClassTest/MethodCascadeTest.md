# ClassMethodCascadeTest
## Description
Nested class method call through cascade: test.test1.print_sum().

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let test : mut Test2 = new Test2();
        test.test1 = new Test1();
        test.test1.a = 1;
        test.test1.b = 1;
        test.test1.print_sum();
    }

    class Test1 {
        a : u8;
        b : u8;
        fun print_sum(this) {
            print(cast<string>(this.a + this.b));
        }
    }

    class Test2 {
        test1: Test1;
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
