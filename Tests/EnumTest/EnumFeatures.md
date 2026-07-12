# EnumFeatures
## Description
Enum definition, matching with `is`, payload variants, return from functions, casting to string, and RVO with generics.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    enum Color { Red; Green; Blue; }
    fun color_name(c: Color) -> string {
        if (c is Color.Red) { return "red"; }
        if (c is Color.Green) { return "green"; }
        return "blue";
    }
    initial {
        println(color_name(new Color.Red()));
        println(color_name(new Color.Blue()));
    }
}
namespace __c2 {
    enum OptVal { some: i32; none; }
    fun get_or_default(o: OptVal, def: i32) -> i32 {
        if (o is OptVal.some) { return o.some; }
        return def;
    }
    initial {
        let a = new OptVal.some(42);
        let b = new OptVal.none();
        println(cast<string>(get_or_default(a, 0)));
        println(cast<string>(get_or_default(b, -1)));
    }
}
namespace __c3 {
    enum ResVal { ok: i32; err; }
    initial {
        let r: ResVal = new ResVal.ok(100);
        if (r is ResVal.ok) {
            println("ok:" + cast<string>(r.ok));
        } else {
            println("err");
        }
    }
}
namespace __c4 {
    enum Shape { circle: i32; rect: i32; }
    fun area(s: Shape) -> i32 {
        if (s is Shape.circle) {
            let r: i32 = s.circle;
            return r * r * 3;
        }
        let side: i32 = s.rect;
        return side * side;
    }
    initial {
        println(cast<string>(area(new Shape.circle(5))));
        println(cast<string>(area(new Shape.rect(4))));
    }
}
namespace __c5 {
    enum BoolVal { yes; no; }
    fun to_bool(b: BoolVal) -> bool {
        if (b is BoolVal.yes) { return true; }
        return false;
    }
    initial {
        println(cast<string>(to_bool(new BoolVal.yes())));
        println(cast<string>(to_bool(new BoolVal.no())));
    }
}
namespace __c6 {
    enum Color { red; green; blue; }
    fun get_color() -> Color {
        return new Color.red();
    }
    fun check_color(c: Color) -> string {
        if (c is Color.red) { return "red"; }
        return "other";
    }
    initial {
        let c = get_color();
        println(check_color(c));
    }
}
namespace __c7 {
    enum OptStr { some: string; none; }
    fun make_some(s: string) -> OptStr {
        return new OptStr.some(s);
    }
    initial {
        let o = make_some("hello");
        if (o is OptStr.some) {
            println(o.some);
        } else {
            println("none");
        }
    }
}
namespace __c8 {
    enum OptI64 { some: i64; none; }
    fun make_some(v: i64) -> OptI64 {
        return new OptI64.some(v);
    }
    initial {
        let o = make_some(cast<i64>(42));
        if (o is OptI64.some) {
            println(cast<string>(o.some));
        } else {
            println("none");
        }
    }
}
namespace __c9 {
    class Point {
        x: i32;
        y: i32;
        fun new(mut this, x: i32, y: i32) {
            this.x = x;
            this.y = y;
        }
    }
    fun make_point(x: i32, y: i32) -> Point {
        return new Point(x, y);
    }
    initial {
        let p = make_point(3, 4);
        println(cast<string>(p.x + p.y));
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
ExpectedStdout: EQUALS `red
blue
42
-1
ok:100
75
16
true
false
red
hello
42
7
`
ExpectedStderr: DISCARD
