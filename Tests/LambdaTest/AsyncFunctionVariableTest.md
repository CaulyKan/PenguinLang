# AsyncFunctionVariableTest
## Description
Assign async function to async_fun variable and call it.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): async 函数赋给 async_fun 变量并调用（EP: async_fun<...> 类型语法不识别）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun x() -> i32 { 
        wait; 
        return 1;
    }
    initial {
        let y : async_fun<i32> = x;
        let z : i32 = y();
        print(cast<string>(z));
    }
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
