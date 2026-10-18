# ArrayStringElements
## Description
array<string> arrays: strings are references; fill copies the reference, at returns the same string content.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let words : mut array<string> = create_array(3, "x");
    words.set(0, "penguin");
    words.set(2, "lang");
    let out : mut string = "";
    for (let w in words) {
        out += w;
    }
    println(out);
    println("len=" + cast<string>(words.size()));
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
ExpectedStdout: EQUALS `penguinxlang
len=3
`
ExpectedStderr: DISCARD
