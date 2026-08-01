# MetaPrintf
## Description
R4 composite: `#printf("a={}, b={}") { a, b }` — a `#fun` that takes a format string + an `unstructured_ast` trailing block. The raw text `"a , b"` arrives as a string; the #fun parses it into a real `FunctionCallArguments` AST node via `emperor.penguin_meta_parse_arguments`, retrieves it with `penguin_meta_get_ast_expression`, and introspects `expr.function_call_arguments.items`. It iterates the format string's `{}` placeholders, builds a `println` expression that interpolates each argument, and returns it via `#create_expression`. Exercises: `unstructured_ast` raw trailing-block capture + argument-list parsing + real-node introspection (FunctionCallArguments) + string building + format parsing + computed `#create_expression` + statement-position splice. Requires native Pass2/Pass3.
## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3
## Test Code
```
#fun printf(fmt: string, params: unstructured_ast) -> ast {
    let token = emperor.penguin_meta_parse_arguments(params);
    let expr = emperor.penguin_meta_get_ast_expression(token);
    if (expr is emperor.Expression.function_call_arguments) {
        let args = expr.function_call_arguments;
        let n = cast<i64>(args.size());
        let mut fmt_idx: mut i64 = 0;
        let mut arg_idx: mut i64 = 0;
        let fmt_len = string_length(fmt);
        let mut result = "println(\"";
        while (fmt_idx < fmt_len) {
            let ch = string_char_at(fmt, fmt_idx);
            if (ch == "{" && fmt_idx + 1 < fmt_len && string_char_at(fmt, fmt_idx + 1) == "}") {
                if (arg_idx < n) {
                    let arg_expr = args.at(cast<u64>(arg_idx)).some;
                    result = result + "\" + cast<string>(" + arg_expr.build_text() + ") + \"";
                }
                arg_idx = arg_idx + 1;
                fmt_idx = fmt_idx + 2;
            } else {
                result = result + ch;
                fmt_idx = fmt_idx + 1;
            }
        }
        result = result + "\")";
        return #create_expression(result);
    }
    return #create_expression("println(\"error\")");
}
initial {
    let a: i32 = 10;
    let b: i32 = 20;
    #printf("a={}, b={}") { a, b };
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
ExpectedStdout: EQUALS `a=10, b=20
`
ExpectedStderr: DISCARD
