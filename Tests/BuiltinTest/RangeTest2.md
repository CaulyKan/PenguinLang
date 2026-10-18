# RangeTest2
## Description
Range as explicit iterator type: mut IIterator<i64> = range(0, 5).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let rg : mut IIterator<i64> = range(0, 5);
        while(true) {
            let n : Option<i64> = rg.next();
            if (n.is_none())
                break;
            else
                print(cast<string>(n.some));
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
ExpectedStdout: EQUALS `01234`
ExpectedStderr: DISCARD
