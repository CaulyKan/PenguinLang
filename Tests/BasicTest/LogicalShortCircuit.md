# LogicalShortCircuit
## Description
Short-circuit evaluation of && and ||: RHS must not be evaluated when the result is already determined. Only EmperorPenguin Pass1 implements short-circuit codegen.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun rhs_evaluated() -> bool {
        println("RHS");
        return true;
    }
    initial {
        let a: bool = false && rhs_evaluated();
        let b: bool = true || rhs_evaluated();
        if (a) { println("a_yes"); } else { println("a_no"); }
        if (b) { println("b_yes"); } else { println("b_no"); }
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
ExpectedStdout: EQUALS `a_no
b_yes
`
ExpectedStderr: DISCARD
