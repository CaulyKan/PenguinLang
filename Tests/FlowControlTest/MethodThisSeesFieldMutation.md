# MethodThisSeesFieldMutation
## Description
Regression test: method `this` binding must see field mutations after children.push().

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Node {
        name: mut string = "";
        children: mut _utils.List<Node> = new _utils.List<Node>();
        flag: mut bool = false;
        extra: mut _utils.List<Node> = new _utils.List<Node>();
        opt: mut Option<Node> = new Option<Node>.none();

        fun describe(this) -> string {
            let s: mut string = this.name;
            s = s + "(" + cast<string>(this.children.size()) + ")";
            return s;
        }
    }

    initial {
        let inner = new Node();
        inner.name = "i64";
        let outer = new Node();
        outer.name = "List";
        outer.children.push(inner);
        let size_before: string = cast<string>(outer.children.size());
        let desc: string = outer.describe();
        let size_after: string = cast<string>(outer.children.size());
        println(size_before + "|" + desc + "|" + size_after);
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
ExpectedStdout: EQUALS `1|List(1)|1
`
ExpectedStderr: DISCARD
