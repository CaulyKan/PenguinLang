# MetaMultiDefinitionSplice
## Description
`compiler().create_definition` with a definition LIST text ("height : i32 = 3 ; fun double_h() -> i32 { return 6 ; }") registers a definition_group; the def-position splice (`try_splice_meta_fun_def` via `get_ast_definition_list`) expands EVERY element in order — both the field and the generated function become live class members. Guards the multi-definition half of create_definition + the group splice. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun mk_pair() -> ast {
    return compiler().create_definition(
        "height : i32 = 3 ; fun double_h() -> i32 { return 6 ; }");
}
class Box {
    #mk_pair();
}
initial {
    let b: mut Box = new Box();
    println(cast<string>(b.height) + "," + cast<string>(b.double_h()));
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
ExpectedStdout: EQUALS `3,6
`
ExpectedStderr: DISCARD
