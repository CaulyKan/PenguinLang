# WrongFunctionTypeTest
## Description
Compile error: assigning function with return value to fun<void>.
RED SENTINEL (known gap in EmperorPenguin, .agents/plans/emperorpenguin-fun-values.md): fun 类型赋值不兼容须报 E_TYPE_MISMATCH（EP: 编译失败但无该错误码）. Should turn green once implemented.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun x() -> i32 { 
    }
    initial {
        let y : fun<void> = x;
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
