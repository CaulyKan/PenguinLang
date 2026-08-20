# NestedValueClassInlineLayout
## Description
A value-type class field whose type is ANOTHER value-type class (here B,
declared AFTER A — the forward-reference case) must nest INLINE in the
containing layout: `#sizeof(B)` = 16 (meta 8 + i64) and `#sizeof(A)` = 24
(meta 8 + inline B), and writes through `a.p.x` must be visible in the
nested copy. Previously the class layout pass ran in definition order and a
forward-referenced value-class field silently degraded to `ptr`, so
`#sizeof(A)` disagreed with the emitted `{ ptr, ptr }` struct. EmperorPenguin
only (#sizeof meta call); verified on Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class A {
        p: B;
    }
    class B {
        x: i64;
    }
    initial {
        let b : mut B = new B();
        b.x = 7;
        let a : mut A = new A();
        a.p = b;
        b.x = 9;
        println("a.p.x=" + cast<string>(a.p.x));
        println("szB=" + cast<string>(#sizeof(B)));
        println("szA=" + cast<string>(#sizeof(A)));
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
ExpectedStdout: EQUALS `a.p.x=7
szB=16
szA=24
`
ExpectedStderr: DISCARD
