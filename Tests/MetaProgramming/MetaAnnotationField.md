# MetaAnnotationField
## Description
The general trailing-definition annotation: `#tagged("alpha")` written WITHOUT a trailing `;`, followed directly by the field it annotates (`name: string = "field-ok";`). The parser appends the field NAME as a `field: string` argument and delivers the field's raw text to the `unstructured_ast` last parameter — the #fun OWNS the field and must re-emit it. Here `tagged` shape-checks the text via `compiler().create_definition` + `compiler().get_definition_kind` (must be "class_field"), generates the marker `tag_of_name()` (no `this` — a static member, called through a receiver whose value is discarded), and returns the MULTI-definition "marker + re-emitted field" — `create_definition`'s definition-list form, spliced in order by `try_splice_meta_fun_def`. Both the marker and the field are live at runtime. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun tagged(tag: string, field: string, trailing_ast: unstructured_ast) -> ast {
    let probe: i64 = compiler().create_definition(trailing_ast);
    if (compiler().get_definition_kind(probe) != "class_field") {
        compiler().error("#tagged must annotate a field declaration");
    }
    return compiler().create_definition(
        "fun tag_of_" + field + "() -> string { return \"" + tag + "/" + field + "\"; } "
        + trailing_ast);
}
class C {
    #tagged("alpha")
    name: string = "field-ok";
}
initial {
    let c: mut C = new C();
    println(c.tag_of_name() + " " + c.name);
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
ExpectedStdout: EQUALS `alpha/name field-ok
`
ExpectedStderr: DISCARD
