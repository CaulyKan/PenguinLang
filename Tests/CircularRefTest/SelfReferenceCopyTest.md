# SelfReferenceCopyTest
## Description
Node with self-referencing Option<Node>, copied via ICopy.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Node {
        value: mut i64 = 0;
        next: mut Option<Node> = new Option<Node>.none();
        impl ICopy<Self>;
    }
    initial {
        let a : mut Node = new Node();
        a.value = 1;
        a.next = new Option<Node>.some(a);

        let b : mut Node = a.copy();
        b.value = 10;

        print(cast<string>(a.value));
        print(cast<string>(b.value));
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
ExpectedStdout: EQUALS `110`
ExpectedStderr: DISCARD
