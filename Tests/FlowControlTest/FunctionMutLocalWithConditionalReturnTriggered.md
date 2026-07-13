# FunctionMutLocalWithConditionalReturnTriggered
## Description
Regression test: function with mut local, conditional early return triggered (flag=true), and mut reassignment.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Formatter {
        name: mut string = "";
        flag: mut bool = false;

        fun build_text(this) -> string {
            let prefix: mut string = "";
            if (this.flag) { prefix = "mut "; }
            if (this.flag) {
                return prefix + "special";
            }
            let s: mut string = prefix + this.name;
            return s;
        }
    }

    initial {
        let f = new Formatter();
        f.name = "List";
        f.flag = true;
        println(f.build_text());
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
ExpectedStdout: EQUALS `mut special
`
ExpectedStderr: DISCARD
