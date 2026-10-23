# AsyncArrayParameter
## Description
Passing a native array as a direct `async` call argument. DOUBLE RED
SENTINEL: EmperorPenguin reports `Can't resolve type specifier
'mut <mut S>'` (array<T> flowing through the async-argument type
serialization); BabyPenguin fails with `Cant resolve symbol 'a'` (its
async path does not bind the array parameter). Neither compiler
supports the shape today; both should turn green. (Workaround in real
code: route the array through a global.)

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class S { impl IReferenceType; v: mut i64 = 0; }
fun worker(a: mut array<S>) -> i64 { return cast<i64>(a.size()); }
initial {
    let a: mut array<S> = create_array(4, new S());
    let f = async worker(a);
    println("r=" + cast<string>(wait f));
}
```

## Run
ExitCode: 0
ExpectedStdout: EQUALS `r=4
`
