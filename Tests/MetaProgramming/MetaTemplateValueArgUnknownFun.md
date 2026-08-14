# MetaTemplateValueArgUnknownFun
## Description
NEGATIVE: a meta-call template argument naming a #fun that does not exist — `new A<#no_such_fun()>()`. try_eval_meta_value_arg declines (find_meta_function fails), the arg falls to the type path (the # call does not produce a type), and resolution fails — compile error, exit NONZERO. Pass3 (value-meta-arg machinery; on Pass2 the same construct also fails to resolve the arg type — NONZERO there too).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
class A {
    foo: i32 = N;
}
initial {
    let a = new A<#no_such_fun()>();
    println("unreachable");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
