# MetaUnstructuredAst
## Description
`unstructured_ast` parameter kind: the trailing `{ a, b }` block is captured as **raw text** (no parsing — commas preserved), delivered to the `#fun` as a `string`. The `#fun` receives `"a , b"` (token texts joined with spaces) and uses it in a `compiler().create_expression` to print it. Proves the parser raw-capture + string delivery pipeline. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun echo_raw(fmt: string, raw: unstructured_ast) -> ast {
    return compiler().create_expression("println(\"" + raw + "\")");
}
initial {
    let a: i32 = 10;
    let b: i32 = 20;
    #echo_raw("fmt") { a, b };
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
ExpectedStdout: EQUALS `a , b
`
ExpectedStderr: DISCARD
