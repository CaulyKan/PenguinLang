# MetaSpecializingTypeCondition
## Description
`#specializing`'s condition can test the TYPE parameter directly: `T.is_primitive()` selects one impl, otherwise another. The callback's `T` is the real `emperor.BoundType` (meta JIT), so conditions are ordinary expressions on the type — no constraint-solver / where-clause machinery.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
#template(T: type)
interface IDescribe {
    fun describe(this) -> string;
}

#template(T: type)
class Wrapped {
    value: T;
    impl IReferenceType;
    fun new(mut this, v: T) { this.value = v; }
}

class Thing {
    x: i64 = 0;
}

#specializing Wrapped<T> {
    if (T.is_primitive()) {
        impl IDescribe {
            fun describe(this) -> string { return "primitive:" + cast<string>(this.value); }
        }
    }
    else {
        impl IDescribe {
            fun describe(this) -> string { return "other"; }
        }
    }
}
}
initial {
    let a = new penguin.Wrapped<i32>(42);
    let da: penguin.IDescribe = a;
    println(da.describe());
    let b = new penguin.Wrapped<penguin.Thing>(new penguin.Thing());
    let db: penguin.IDescribe = b;
    println(db.describe());
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
ExpectedStdout: EQUALS `primitive:42
other
`
ExpectedStderr: DISCARD
