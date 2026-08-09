# MetaTemplateValueList
## Description
WIP / forward-looking (EXPECTED RED on Pass2/Pass3 for now): a value-template function that builds and returns a `List<i32>` from the integer value param N — `filled<3>()` should yield a list of size 3. This currently FAILS because value-template functions desugar to compile-time `#funs`, whose meta ABI returns via the i64 trampolines (with side-channel globals only for `type`/`string`/`ast` returns). A runtime reference type like `List<i32>` cannot be returned from a #fun and spliced at the call site — so the result is mis-spliced and `l.size()` does not resolve. This is a red sentinel for the intended feature: it should turn green once the meta layer supports returning/splicing runtime reference types (or this is handled by a different mechanism, e.g. req5-style specialization instead of desugar-to-#fun). Kept on Pass2/Pass3 as a stable-baseline failure.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun filled() -> List<i32> {
    let l: mut List<i32> = new List<i32>();
    let i: mut i64 = 0;
    while (i < N) {
        l.push(i);
        i = i + 1;
    }
    return l;
}
initial {
    let l = filled<3>();
    println("size=" + cast<string>(l.size()));
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
ExpectedStdout: EQUALS `size=3
`
ExpectedStderr: DISCARD
