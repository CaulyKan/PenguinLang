# MetaSpecializingNoImpl
## Description
Positive semantics: when NO branch of a `#specializing` activates an impl (the `else {}` fall-through), the type simply has no impl — calling an interface method that would only exist via the impl is a compile error at the use site. `Multi<i32>` implements nothing, so `m.one()` (which exists only in the not-activated `IOne` impl) fails to resolve. No silent fallback, no constraint solver. Compile must fail.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
#template(T: type)
interface IOne {
    fun one(this) -> i64;
}

#template(T: type)
class Multi {
    impl IReferenceType;
}

#specializing Multi<T> {
    if (false) {
        impl IOne {
            fun one(this) -> i64 { return 1; }
        }
    }
    else {
    }
}
}
initial {
    let m = new penguin.Multi<i32>();
    println("one=" + cast<string>(m.one()));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
