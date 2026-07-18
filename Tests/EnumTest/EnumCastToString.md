# EnumCastToString
## Description
Cast an enum variant to string returns the tag ordinal (0, 1, 2...) on EmperorPenguin. BabyPenguin returns the variant name instead, so this is EP-only.

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    enum Color { red; green; blue; }
    fun color_name(c: Color) -> string {
        return cast<string>(c);
    }
    initial {
        println(color_name(new Color.red()));
        println(color_name(new Color.blue()));
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
ExpectedStdout: EQUALS `0
2
`
ExpectedStderr: DISCARD
