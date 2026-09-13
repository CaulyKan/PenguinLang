# AsyncFunctionBindingTest
## Description
Bind async method to async_fun variable.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): async 方法引用绑定 async_fun 变量（EP: async_fun<...> 类型语法不识别 + 方法引用缺失）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Temp {
            a : i32 = 1;
            fun call(this: Self) -> i32 {
                wait;
                return this.a;
            }
        }
        initial {
            let x : Temp = new Temp();
            let func : async_fun<i32> = x.call;
            print(cast<string>(func()));
        }
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
