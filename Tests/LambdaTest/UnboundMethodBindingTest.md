# UnboundMethodBindingTest
## Description
Unbound method reference: `Temp.call` used as a value of type `fun<i32, Temp, i32>` (return type first, then the receiver as first parameter — the value form of the `a.b(x) ≡ A.b(a, x)` sugar), called with the receiver passed explicitly.

DOUBLE RED SENTINEL — this is NEW semantics from .agents/plans/emperorpenguin-fun-values.md that NEITHER compiler implements yet (BabyPenguin lacks the unbound member-access branch; EmperorPenguin lacks method references entirely). Both should turn green once milestone 3 lands (BabyPenguin reference implementation first, then EmperorPenguin).

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
            let h : fun<i32, Temp, i32> = Temp.call;
            print(cast<string>(h(x, 2)));
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
