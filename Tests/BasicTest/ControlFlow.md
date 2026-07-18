# ControlFlow
## Description
If-else statements, nested if, and while loops.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    initial {
        if (1 > 0) {
            println("yes");
        } else {
            println("no");
        }
    }
}
namespace __c2 {
    initial {
        if (0 > 1) {
            println("yes");
        } else {
            println("no");
        }
    }
}
namespace __c3 {
    initial {
        let x: i64 = 15;
        if (x > 10) {
            if (x > 20) {
                println("big");
            } else {
                println("mid");
            }
        }
    }
}
namespace __c4 {
    initial {
        let sum: mut i64 = 0;
        let i: mut i64 = 1;
        while (i <= 10) {
            sum = sum + i;
            i = i + 1;
        }
        println(cast<string>(sum));
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
ExpectedStdout: EQUALS `yes
no
mid
55
`
ExpectedStderr: DISCARD
