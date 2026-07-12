# ValueTypeSemantics
## Description
Semantic rules for IValueType, IReferenceType, and ICopy. Tests auto value type, explicit IValueType, reference type rejection of imm→mut, recursive class classification, and sibling scope let name collision.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    class Point {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) {
            this.x = x;
            this.y = y;
        }
    }
    initial {
        let p = new Point(3, 4);
        let q: mut Point = p;  // value types: imm→mut works (copy)
        println(cast<string>(q.x + q.y));
    }
}
namespace __c2 {
    class Val {
        name: string;
        impl IValueType;
        fun new(mut this, name: string) {
            this.name = name;
        }
    }
    initial {
        let v = new Val("hello");
        let w: mut Val = v;  // IValueType → imm→mut works
        println(w.name);
    }
}
namespace __c3 {
    fun build_list() -> _utils.List<i64> {
        let list: mut _utils.List<i64> = new _utils.List<i64>();
        list.push(cast<i64>(10));
        list.push(cast<i64>(20));
        list.push(cast<i64>(30));
        return list;
    }
    initial {
        let list: _utils.List<i64> = build_list();
        let sum: mut i64 = 0;
        let i: mut i64 = 0;
        while (i < cast<i64>(list.size())) {
            sum = sum + list.at(cast<u64>(i)).some;
            i = i + 1;
        }
        println(cast<string>(sum));
    }
}
namespace __c4 {
    class Box {
        v: i64;
        fun new(mut this, v: i64) {
            this.v = v;
        }
    }
    enum Opt {
        some: i64;
        none;
    }
    initial {
        let cond: i64 = 1;
        if (cond == 0) {
            let item: mut Box = new Box(100);
            println(cast<string>(item.v));
        } else {
            let item: mut Opt = new Opt.some(50);
            if (item is Opt.some) {
                println(cast<string>(item.some));
            } else {
                println("none");
            }
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
ExpectedStdout: EQUALS `7
hello
60
50
`
ExpectedStderr: DISCARD
