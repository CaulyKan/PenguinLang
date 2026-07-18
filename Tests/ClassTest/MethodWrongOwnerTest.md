# ClassMethodWrongOwnerTest
## Description
Compile-error: method called on wrong class type.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
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
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
