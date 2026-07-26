# MetaCreateDefinition
## Description
Phase 5c: `#create_definition("code")` parses a literal definition fragment and registers it; a `#fun -> ast` called at DEFINITION position (a top-level `#make_foo();`) JIT-executes at compile time and the returned Definition is injected into the compilation unit before the 9 semantic passes, so it flows through binding as if originally in source. The generated `fun foo()` is then callable from `initial`. Demonstrates compile-time code generation of a whole definition. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun make_foo() -> ast {
    return #create_definition("fun foo() -> i64 { return 42; }");
}
#make_foo();
initial {
    println("foo=" + cast<string>(foo()));
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
ExpectedStdout: EQUALS `foo=42
`
ExpectedStderr: DISCARD
