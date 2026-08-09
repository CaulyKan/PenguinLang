# MetaTemplateTypeof
## Description
req1 (meta x template): a `#template(T: type)` function passes its type parameter `T` to a `#fun` meta function via `#typeof(T)`. When the function is specialized as `count_fields<Point>`, the template parameter resolves through the local scope chain to the concrete type `Point`, so `#field_count_of(#typeof(T))` JIT-evaluates with Point's type token and returns its field count (2). Without scope-chain resolution of template type params, this fails with "meta type argument 'T' is not a known type". Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: mut i32; y: mut i32; }
#fun field_count_of(t: type) -> i64 {
    return cast<i64>(t.fields().size());
}
#template(T: type)
fun count_fields(x: T) -> i64 {
    return #field_count_of(#typeof(T));
}
initial {
    let p = new Point();
    let n = count_fields<Point>(p);
    println("fields=" + cast<string>(n));
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
ExpectedStdout: EQUALS `fields=2
`
ExpectedStderr: DISCARD
