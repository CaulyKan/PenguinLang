# ExplicitCastValueAlias
## Description
Box semantics (sentinel turned green): an explicit `cast<Interface>(value)` of
a VALUE-TYPE class BOXES — it copies the struct into a fresh box on BOTH
compilers (EmperorPenguin emit_box memcpy's into a fresh GC allocation;
BabyPenguin CastValue deep-copies value classes to interface). Later mutation
of the original through a mut binding is NOT visible through the interface —
prints 1. Historically a red sentinel (BabyPenguin aliased, EmperorPenguin
copied); the value-copy semantics change adopted the box (copy) behavior as
the contract, matching C#-style boxing.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; }
        impl IGetX { fun get_x(this) -> i32 { return this.x; } }
    }
    interface IGetX { fun get_x(this) -> i32; }

    initial {
        let mut p = new Point(1, 2);
        let i: IGetX = cast<IGetX>(p);
        p.x = 9;
        print(cast<string>(i.get_x()));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
