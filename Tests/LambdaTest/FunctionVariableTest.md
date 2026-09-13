# FunctionVariableTest
## Description
Assign function to fun<void> variable and call it.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): 局部 fun 变量调用 y()（EP: 标识符 callee 路径只认 function_sym → E_INTERNAL: Function call has no callee symbol）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun x() { print("hello"); }
    initial {
        let y : fun<void> = x;
        y();
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
ExpectedStdout: EQUALS `hello`
ExpectedStderr: DISCARD
