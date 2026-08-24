# VoidGenericArg
## Description
`void` as a generic type argument (the unit type): `Option<void>` values, a generic class with a void-typed field, void parameters and void returns through T. This locks in the void-as-unit elision fix (2026-08-24): before it, any `Option<void>` mention died with `error[E_INTERNAL]: Function call has no callee symbol` — the root cause was `umangleable(void)` returning true (void is a PrimitiveKind), which injected the `IUniqueMangleName` impl for `Option<void>` whose body calls `.get_unique_name()` on a void payload (no impl → poisoned void-typed call). The compiler now treats void as non-umangleable AND elides void storage everywhere at LLVM emission: void fields are skipped in class layouts, void params/args are dropped from signatures and call sites, void enum payloads are tag-only, and void member reads/returns lower to the unit value. Verified on BabyPenguin and EmperorPenguin Pass1.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
    #template(T: type)
    class Holder {
        stored: mut T;
        fun new(mut this, v: T) {
            this.stored = v;
        }
        fun touch(mut this, v: T) {
            this.stored = v;
        }
        fun get(this) -> T {
            return this.stored;
        }
    }

    fun take_void(v: void) -> void {
        return;
    }

    initial {
        let n : __builtin.Option<void> = new __builtin.Option<void>.none();
        if (n.is_none()) { print("A"); }
        let h : mut Holder<void> = new Holder<void>(void);
        h.touch(void);
        take_void(void);
        print("B");
    }
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `AB`
