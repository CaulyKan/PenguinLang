# MetaSpecializingMultipleBlocks
## Description
A type may have MULTIPLE `#specializing` blocks; each is a separate callback and each executed `impl` contributes an interface impl. `Multi<i32>` gets `IOne` from the first block and `ITwo` from the second.

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
interface ITwo {
    fun two(this) -> i64;
}

#template(T: type)
class Multi {
    impl IReferenceType;
}

#specializing Multi<T> {
    if (T.is_primitive()) {
        impl IOne {
            fun one(this) -> i64 { return 1; }
        }
    }
}

#specializing Multi<T> {
    if (T.is_primitive()) {
        impl ITwo {
            fun two(this) -> i64 { return 2; }
        }
    }
}
}
initial {
    let m = new penguin.Multi<i32>();
    let d1: penguin.IOne = m;
    let d2: penguin.ITwo = m;
    println("one=" + cast<string>(d1.one()) + " two=" + cast<string>(d2.two()));
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
ExpectedStdout: EQUALS `one=1 two=2
`
ExpectedStderr: DISCARD
