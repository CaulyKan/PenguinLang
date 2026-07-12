# GarbageCollection
## Description
EmperorPenguin GC tests: retains reachable objects, preserves string locals/globals, frees memory on collection, and reflects allocations. Uses _emperor_gc_* builtins only available in EmperorPenguin.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    class Node {
        val: i64;
        fun new(mut this, v: i64) {
            this.val = v;
        }
    }
    initial {
        let anchor = new Node(999);
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp = new Node(i);
            i = i + 1;
        }
        _emperor_gc_collect();
        println(cast<string>(anchor.val));
    }
}
namespace __c2 {
    initial {
        let s: string = "alive";
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp: string = "garbage" + cast<string>(i);
            i = i + 1;
        }
        _emperor_gc_collect();
        println(s);
    }
}
namespace __c3 {
    let msg: string = "global_alive";
    initial {
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp: string = "noise" + cast<string>(i);
            i = i + 1;
        }
        _emperor_gc_collect();
        println(msg);
    }
}
namespace __c4 {
    class Node {
        val: i64;
        fun new(mut this, v: i64) {
            this.val = v;
        }
    }
    initial {
        let i: mut i64 = 0;
        while (i < 500) {
            let tmp = new Node(i);
            i = i + 1;
        }
        let before: i64 = _emperor_gc_info();
        _emperor_gc_collect();
        let after: i64 = _emperor_gc_info();
        if (after < before) {
            println("freed");
        } else {
            println("no_free");
        }
    }
}
namespace __c5 {
    initial {
        let before: i64 = _emperor_gc_info();
        let s: string = "hello" + " world";
        let after: i64 = _emperor_gc_info();
        if (after > before) {
            println("grew");
        } else {
            println("no_grow");
        }
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
ExpectedStdout: EQUALS `999
alive
global_alive
freed
grew
`
ExpectedStderr: DISCARD
