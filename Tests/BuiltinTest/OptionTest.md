# OptionTest
## Description
Option<T> built-in: some and none variants, is_some, is_none, value_or.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : Option<u32> = new Option<u32>.some(10);
        println(cast<string>(a.is_some()));
        println(cast<string>(a.is_none()));
        println(cast<string>(a.value_or(9)));

        let b : Option<u32> = new Option<u32>.none();
        println(cast<string>(b.is_some()));
        println(cast<string>(b.is_none()));
        println(cast<string>(b.value_or(9)));
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
ExpectedStdout: EQUALS `true
false
10
false
true
9
`
ExpectedStderr: DISCARD
