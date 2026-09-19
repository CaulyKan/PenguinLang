# GlobalOptionTryBind

## Description
try-bind (`if (let v := a.some)`) reading the payload of a GLOBAL `Option` variable. FIXED: BabyPenguin's `IRGenerator` now emits a `GLOBAL_STORE` after the WRMBR writes of a `new E.v(payload)` initializer (enum globals get a clone at every `GLOBAL_STORE`, so the in-place tag/payload writes on the register object never reached the global's storage and the payload read returned NotInitialized). BabyPenguin and EmperorPenguin Pass3 both print `42`.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
    let a = new Option<i32>.some(42);

    initial {
        if (let v := a.some) {
            print(cast<string>(v));
        } else {
            print("none");
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
