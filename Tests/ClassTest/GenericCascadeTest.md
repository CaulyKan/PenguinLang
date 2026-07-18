# GenericCascadeTest
## Description
Nested generic types: Test<Test2<u8>> and Test<Test2<string>>.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        #template(T: type)
        class Test {
            x: auto T;
        }

        #template(T: type)
        class Test2 {
            y: auto T;
        }

        initial {
            let t : mut Test<Test2<u8>> = new Test<Test2<u8>>();
            t.x = new Test2<u8>();
            t.x.y = 1;
            print(cast<string>(t.x.y));

            let t2 : mut Test<Test2<string>> = new Test<Test2<string>>();
            t2.x = new Test2<string>();
            t2.x.y = "2";
            print(t2.x.y);
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
