# WhileLoop
## Description
While loop that prints iteration index and a newline. Uses print() so it is kept as an individual test.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let i: mut i64 = 0;
        while (i < 5) {
            print(cast<string>(i));
            i = i + 1;
        }
        println("");
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
ExpectedStdout: EQUALS `01234
`
ExpectedStderr: DISCARD
