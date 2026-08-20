# RecursiveValueTypeSizeError
## Description
A class explicitly marked as a VALUE type (`impl IValueType`) whose field graph
recursively contains itself through an enum payload (`next: Option<Node>`)
has no finite inline layout: Option<Node> would inline Node, which inlines
Option<Node>, forever. The compiler must reject it with
`error[E_SIZE_CYCLE]: Cannot compute size of 'Node'` instead of emitting a
bogus layout (the payload used to silently degrade to a pointer, leaving
stack addresses dangling) or crashing. Note the AUTO-classified form (no
impl IValueType) is intentionally NOT an error: the classifier detects the
cycle and makes Node a reference type — see CircularRefTest/* and
LinkedRefThroughOption. BabyPenguin accepts this program (its VM has no
inline layout), so Apply To is EmperorPenguin-only. Verified on
Pass1/Pass2/Pass3 (compile fails, message on stderr; the mangled names in
the message are path-dependent, so stderr is DISCARDed and only the exit
code is asserted).

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Node {
        impl __builtin.IValueType;
        value: i64;
        next: Option<Node>;
    }
    initial {
        let a : mut Node = new Node();
        a.value = 1;
        print(cast<string>(a.value));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
