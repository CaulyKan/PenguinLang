# EnumValueClassPayloadInline
## Description
An enum variant whose payload is a VALUE-type class must store the payload
INLINE in the enum struct (`{ ptr meta, i32 tag, %class.Point }`), so
mutating the source after construction does not alias the stored payload
(value semantics), and reading `s.circle.x` returns the copy. Previously the
enum layout pass ran before class layouts, the payload silently degraded to
`ptr`, and the emitter papered over it by heap-boxing a copy — with the
correct size model the payload nests inline. Behavioral only (no meta
calls) so it applies to every compiler.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Point {
        x: i64;
        y: i64;
    }
    enum Shape {
        circle: Point;
        none;
    }
    initial {
        let p : mut Point = new Point();
        p.x = 3;
        p.y = 4;
        let s : Shape = new Shape.circle(p);
        p.x = 100;
        if (s is Shape.circle) {
            println("c=" + cast<string>(s.circle.x) + "," + cast<string>(s.circle.y));
        }
        let n : Shape = new Shape.none();
        println("none=" + cast<string>(n is Shape.none));
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
ExpectedStdout: EQUALS `c=3,4
none=true
`
ExpectedStderr: DISCARD
