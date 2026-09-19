# GenericValueClassBoxing

## Description
Boxing a SPECIALIZED GENERIC value class into an interface and dispatching through it. FIXED: `get_concrete_type_name` in `EmperorPenguin/src/bound/SemanticBindExpressions.penguin` now resolves a template+args bound type to its SPECIALIZED def's mangled name (the key `ensure_class_layout` registers layouts under), so the BOX instruction finds the class layout. Previously the template's bare name ("Pair") matched no layout and `emit_box` emitted nothing — the boxed interface local's `store ptr %tN` referenced an undefined SSA value (verifier error at link time). Non-generic value classes box fine (see ImplicitValueToInterfaceBoxes); `emit_box` now also fails loudly when neither a class nor an enum layout is found.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
    interface IPrintable {
        fun print_label(this: IPrintable) -> string {
            return "?";
        }
    }

    #template(T: type)
    class Pair {
        first : T;
        second : T;
        impl IPrintable {
            fun print_label(this: IPrintable) -> string {
                let self = cast<Pair<T>>(this);
                return cast<string>(self.first) + "," + cast<string>(self.second);
            }
        }
        fun new(mut this, a : T, b : T) {
            this.first = a;
            this.second = b;
        }
    }

    initial {
        let p : mut Pair<i32> = new Pair<i32>(3, 4);
        let ip : IPrintable = cast<IPrintable>(p);
        println(ip.print_label());
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
ExpectedStdout: EQUALS `3,4
`
ExpectedStderr: DISCARD
