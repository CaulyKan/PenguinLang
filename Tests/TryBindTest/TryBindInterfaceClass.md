# TryBindInterfaceClass
## Description
Try-bind cast checks: `let a : IFoo := bar` (interface target) and `let a : Foo := bar` (class target). Compile-time folded when the RHS static type is a concrete class: a concrete class implementing the interface → TRUE (then-branch always runs, `a` bound to the interface cast); a concrete class not implementing → FALSE. Class targets use exact identity (no class inheritance). NOTE: the runtime ISINSTANCE fallback for interface→interface targets is limited by a pre-existing `_emperor_isinstance` issue (same as plain `is`), so this test covers the foldable concrete cases.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __ic {
    interface IFoo {
        fun get(this) -> i32;
    }
    class Foo {
        impl IFoo {
            fun get(this) -> i32 {
                return 42;
            }
        }
    }
    class Bar {
    }
    fun iface_fold_true(f: Foo) -> string {
        // concrete Foo impls IFoo → compile-time TRUE
        if (let a : IFoo := f) {
            return "t:" + cast<string>(a.get());
        }
        return "fold-false?!";
    }
    fun iface_fold_false(b: Bar) -> string {
        // concrete Bar does NOT impl IFoo → compile-time FALSE
        if (let a : IFoo := b) {
            return "should-not-run";
        }
        return "not-ifoo";
    }
    fun class_fold(f: Foo, b: Bar) -> string {
        let r1 = (let a : Foo := f);
        let r2 = (let a : Foo := b);
        return cast<string>(r1) + "," + cast<string>(r2);
    }
    initial {
        println(iface_fold_true(new Foo()));
        println(iface_fold_false(new Bar()));
        println(class_fold(new Foo(), new Bar()));
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
ExpectedStdout: EQUALS `t:42
not-ifoo
true,false
`
ExpectedStderr: DISCARD
