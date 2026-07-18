# ClassMemberCascadeTest
## Description
Nested class member access through cascade: test.test1.a = 1.

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
        test.test1.b += 1;
        print(cast<string>(test.test1.a));
        print(cast<string>(test.test1.b));
        print(cast<string>(test.test1.a + test.test1.b));
    }

    class Test1 {
        a : u8;
        b : u8;
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
ExpectedStdout: EQUALS `123`
ExpectedStderr: DISCARD
