# FunctionBindingTest
## Description
Bind method to function variable via instance.call.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): 绑定方法引用 x.call 作为 fun 值并调用（EP: 方法值位置不产 fun 类型 + 无 invoker 机制）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Temp {
            a : i32 = 1;
            fun call(this: Self, b : i32) -> i32 {
                return this.a + b;
            }
        }
        initial {
            let x : Temp = new Temp();
            let func : fun<i32, i32> = x.call;
            print(cast<string>(func(2)));
        }
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
ExpectedStdout: EQUALS `3`
ExpectedStderr: DISCARD
