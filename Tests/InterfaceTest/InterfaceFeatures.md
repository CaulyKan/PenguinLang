# InterfaceFeatures
## Description
Interface implementation, enum interface, value type boxing/unboxing, and multiple interface boxing. Interface support requires EmperorPenguin.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    interface IShow {
        fun show(this) -> string;
    }
    class Point {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) {
            this.x = x;
            this.y = y;
        }
        impl IShow {
            fun show(this) -> string {
                return "(" + cast<string>(this.x) + "," + cast<string>(this.y) + ")";
            }
        }
    }
    initial {
        let p = new Point(1, 2);
        println(p.show());
    }
}
namespace __c2 {
    interface IShow {
        fun show(this) -> string;
    }
    enum Color {
        Red;
        Blue;
        impl IShow {
            fun show(this) -> string {
                return "color";
            }
        }
    }
    initial {
        let c = new Color.Red();
        println(c.show());
    }
}
namespace __c3 {
    interface IShow {
        fun show(this) -> string;
    }
    enum Color {
        Red;
        Green;
        Blue;
        impl IShow {
            fun show(this) -> string {
                if (this is Color.Red) { return "red"; }
                if (this is Color.Green) { return "green"; }
                return "blue";
            }
        }
    }
    initial {
        let c1 = new Color.Red();
        let c2 = new Color.Green();
        let c3 = new Color.Blue();
        println(c1.show());
        println(c2.show());
        println(c3.show());
    }
}
namespace __c4 {
    interface IShow {
        fun show(this) -> string;
    }
    class Point {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; }
        impl IValueType {}
        impl IShow {
            fun show(this) -> string {
                return "(" + cast<string>(this.x) + "," + cast<string>(this.y) + ")";
            }
        }
    }
    initial {
        let p = new Point(3, 4);
        let s: IShow = cast<IShow>(p);
        println(s.show());
    }
}
namespace __c5 {
    interface IShow {
        fun show(this) -> string;
    }
    class Val {
        x: i32;
        fun new(mut this, x: i32) { this.x = x; }
        impl IValueType {}
        impl IShow {
            fun show(this) -> string { return cast<string>(this.x); }
        }
    }
    initial {
        let v = new Val(42);
        let s: IShow = cast<IShow>(v);
        let v2: Val = cast<Val>(s);
        println(cast<string>(v2.x));
    }
}
namespace __c6 {
    interface IShow {
        fun show(this) -> string;
    }
    class Val {
        x: i32;
        fun new(mut this, x: i32) { this.x = x; }
        impl IValueType {}
        impl IShow {
            fun show(this) -> string { return cast<string>(this.x); }
        }
    }
    initial {
        let v = new Val(99);
        println(cast<IShow>(v).show());
    }
}
namespace __c7 {
    interface IFoo {
        fun foo(this) -> string;
    }
    interface IBar {
        fun bar(this) -> string;
    }
    class Multi {
        val: i32;
        fun new(mut this, val: i32) { this.val = val; }
        impl IValueType {}
        impl IFoo {
            fun foo(this) -> string { return "foo=" + cast<string>(this.val); }
        }
        impl IBar {
            fun bar(this) -> string { return "bar=" + cast<string>(this.val); }
        }
    }
    initial {
        let m = new Multi(7);
        let f: IFoo = cast<IFoo>(m);
        let b: IBar = cast<IBar>(m);
        println(f.foo());
        println(b.bar());
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
ExpectedStdout: EQUALS `(1,2)
color
red
green
blue
(3,4)
42
99
foo=7
bar=7
`
ExpectedStderr: DISCARD
