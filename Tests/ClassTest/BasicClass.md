# BasicClass
## Description
Class with fields, constructors, methods, mutable fields, method chaining, and field assignment.

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
        println(cast<string>(p.x));
        println(cast<string>(p.y));
    }
}
namespace __c2 {
    class Point {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) {
            this.x = x;
            this.y = y;
        }
        fun sum(this) -> i32 {
            return this.x + this.y;
        }
    }
    initial {
        let p = new Point(3, 4);
        println(cast<string>(p.sum()));
    }
}
namespace __c3 {
    class Foo {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) {
            this.x = x;
            this.y = y;
        }
        fun to_str(this) -> string {
            return "foo=" + cast<string>(this.x) + "," + cast<string>(this.y);
        }
    }
    initial {
        let f = new Foo(1, 2);
        println(f.to_str());
    }
}
namespace __c4 {
    class Counter {
        val: mut i32;
        fun new(mut this) {
            this.val = 0;
        }
        fun increment(mut this) {
            this.val = this.val + 1;
        }
        fun get(this) -> i32 {
            return this.val;
        }
    }
    initial {
        let c = new Counter();
        c.increment();
        c.increment();
        c.increment();
        println(cast<string>(c.get()));
    }
}
namespace __c5 {
    class Calc {
        val: mut i32;
        fun new(mut this, v: i32) {
            this.val = v;
        }
        fun dbl(this) -> i32 {
            return this.val * 2;
        }
        fun neg(this) -> i32 {
            return -this.val;
        }
    }
    initial {
        let c = new Calc(5);
        println(cast<string>(c.dbl()));
        println(cast<string>(c.neg()));
    }
}
namespace __c6 {
    class Box {
        value: i32;
        fun new(mut this, v: i32) {
            this.value = v;
        }
        fun get(this) -> i32 {
            return this.value;
        }
    }
    fun wrap(x: i32) -> Box {
        return new Box(x);
    }
    initial {
        let b = wrap(99);
        println(cast<string>(b.get()));
    }
}
namespace __c7 {
    class Pair {
        a: i32;
        b: i32;
        fun new(mut this, a: i32, b: i32) {
            this.a = a;
            this.b = b;
        }
    }
    initial {
        let p = new Pair(1, 2);
        let sum: i32 = p.a + p.b;
        println(cast<string>(sum));
    }
}
namespace __c8 {
    class Math {
        fun new(mut this) {}
        fun add_mul(this, x: i32, y: i32, z: i32) -> i32 {
            return (x + y) * z;
        }
    }
    initial {
        let m = new Math();
        println(cast<string>(m.add_mul(2, 3, 4)));
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
ExpectedStdout: EQUALS `3
4
7
foo=1,2
3
10
-5
99
3
20
`
ExpectedStderr: DISCARD
