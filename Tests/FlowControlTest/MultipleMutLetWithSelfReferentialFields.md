# MultipleMutLetWithSelfReferentialFields
## Description
Regression test: multiple `let x: mut string` declarations in different if-blocks with self-referential List and Option fields.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Node {
        name: mut string = "";
        children: mut _utils.List<Node> = new _utils.List<Node>();
        is_func: mut bool = false;
        is_async_func: mut bool = false;
        func_params: mut _utils.List<Node> = new _utils.List<Node>();
        ret_type: mut Option<Node> = new Option<Node>.none();
        parts: mut _utils.List<string> = new _utils.List<string>();

        fun build_text(this) -> string {
            let prefix: mut string = "";
            if (this.is_func) {
                let s: mut string = prefix + "fun<placeholder>";
                return s;
            }
            if (this.is_async_func) {
                let s: mut string = prefix + "async_fun<placeholder>";
                return s;
            }
            let s: mut string = prefix + this.name;
            if (cast<i64>(this.children.size()) > 0) {
                s = s + "<";
                let i: mut i64 = 0;
                while (i < cast<i64>(this.children.size())) {
                    if (i > 0) { s = s + ", "; }
                    s = s + this.children.at(cast<u64>(i)).some.name;
                    i = i + 1;
                }
                s = s + ">";
            }
            return s;
        }
    }

    initial {
        let inner = new Node();
        inner.name = "i64";
        let outer = new Node();
        outer.name = "List";
        outer.children.push(inner);
        println(outer.build_text());
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
ExpectedStdout: EQUALS `List<i64>
`
ExpectedStderr: DISCARD
