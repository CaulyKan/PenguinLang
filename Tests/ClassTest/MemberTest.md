# ClassMemberTest
## Description
Basic class with two u8 fields, field assignment and arithmetic.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let test : mut Test = new Test();
        test.a = 1;
        test.b = 1;
        test.b += 1;
        print(cast<string>(test.a));
        print(cast<string>(test.b));
        print(cast<string>(test.a + test.b));
    }

    class Test {
        a : u8;
        b : u8;
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
ExpectedStdout: EQUALS `123`
ExpectedStderr: DISCARD
