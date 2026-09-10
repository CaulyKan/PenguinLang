# LibcExternStringArg
## Description
Guards the bare-libc-extern string marshaling added with the header-prefixed string representation: a bare top-level extern binds a literal libc symbol whose ABI is `char*`, so a PenguinLang `string` argument must cross as its DATA pointer (`getelementptr +16` past the metaptr/length header) and a `string` return value must be adopt-copied into a fresh GC string. `strlen` exercises both directions in one call; `getenv` additionally proves the adopt path returns stable, content-correct strings (empty when unset). BabyPenguin's VM has no C linkage for user externs, hence Pass2/Pass3 only (see GlobalExternLibcTest).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
extern fun strlen(s: string) -> i64;
extern fun getenv(name: string) -> string;

initial {
    println(cast<string>(strlen("hello")));
    println(cast<string>(strlen("")));
    let s: string = "0123456789";
    println(cast<string>(strlen(s + s)));
    println("[" + getenv("PENGUIN_UNSET_VAR_XYZ") + "]");
    println("[" + getenv("PENGUIN_LIBC_TEST_VAR") + "]");
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
Env: `PENGUIN_LIBC_TEST_VAR=adopted`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `5
0
20
[]
[adopted]
`
ExpectedStderr: DISCARD
