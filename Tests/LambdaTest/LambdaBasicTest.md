# LambdaBasicTest
## Description
Basic lambda expression assigned to fun<void> and called.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): lambda 绑定与经局部 fun 变量调用（EP 当前: bind_expression 无 lambda case + 局部 fun 调用走 void 兜底 → E_INTERNAL）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let x : fun<void> = fun { print("hello"); };
        x();
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
