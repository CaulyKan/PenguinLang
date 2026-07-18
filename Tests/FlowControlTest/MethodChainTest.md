# MethodChainTest
## Description
Method chaining: calling a method on the result of another method.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Wrapper {
        val: mut i64 = 0;

        fun new(this: mut Wrapper, v: i64) {
            this.val = v;
        }

        fun add(this: mut Wrapper, v: i64) -> Wrapper {
            this.val = this.val + v;
            return this;
        }

        fun get(this) -> i64 {
            return this.val;
        }
    }

    initial {
        let w: mut Wrapper = new Wrapper(1);
        let result: i64 = w.add(2).get();
        print(cast<string>(result));
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
