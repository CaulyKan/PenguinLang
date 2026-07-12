# ThreeWayCircularCopyTest
## Description
Three-way circular chain: A -> B -> C -> A, copied via ICopy.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
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
        let b : mut Node = new Node();
        b.value = 2;
        let c : mut Node = new Node();
        c.value = 3;
        a.next = new Option<Node>.some(b);
        b.next = new Option<Node>.some(c);
        c.next = new Option<Node>.some(a);

        let a2 : mut Node = a.copy();
        a2.value = 10;

        print(cast<string>(a.value));
        print(cast<string>(a2.value));
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
