# ClassMethodWrongOwnerTest
## Description
Compile-error: method called on wrong class type.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let test : Test2 = new Test2();
        test.print_sum();
    }

    class Test1 {
        a : u8;
        b : u8;
        fun print_sum() {
            print(cast<string>(this.a + this.b));
        }
    }

    class Test2 {
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
