# ClassMethodCantVisitMemberDirectly
## Description
Compile-error: method without `this` parameter cannot access fields directly.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    namespace ns {
        class Test {
            a : u8;
            b : u8;

            fun print_sum() {
                a=1;
            }
        }
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
