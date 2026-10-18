# GenericMetaTypeArg
## Description
The #sizeof compile-time intrinsic accepts a GENERIC type operand (e.g. #sizeof(Option<T>), and #sizeof(T) inside a generic class's method where T is the template parameter): the bare name resolves to the generic definition, then the compiler specializes it by the written arguments (mangle + specialization-symbol lookup, creating the specialization on demand when every argument is concrete) so the size uses the real specialized layout. Option<i32> lays out as { ptr meta, i64 tag, [4 x i8] payload } — the payload union is an opaque byte array (so whole-enum copies can never be split at a single variant's field boundaries) and the tag is i64 (so the alignment-1 byte array lands at offset 16); total = align_up(16+4, 8) = 24. The raw pointer intrinsics (#__load/#__store) that shared this generic-resolution mechanism are RETIRED with the native-arrays round (containers use array<T> now); #sizeof stays. BabyPenguin's ANTLR grammar does not parse expression-level meta calls, so Apply To is EmperorPenguin-only. Verified on Pass1/Pass2/Pass3.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class SizeBox {
    fun slot_bytes(this) -> i64 {
        return #sizeof(Option<T>);
    }
    fun elem_bytes(this) -> i64 {
        return #sizeof(T);
    }
}
initial {
    let n: i64 = #sizeof(Option<i32>);
    println("opt=" + cast<string>(n));
    let b: mut SizeBox<i64> = new SizeBox<i64>();
    println("box_opt=" + cast<string>(b.slot_bytes()));
    println("box_elem=" + cast<string>(b.elem_bytes()));
    let b2: mut SizeBox<string> = new SizeBox<string>();
    println("box2_opt=" + cast<string>(b2.slot_bytes()));
    println("box2_elem=" + cast<string>(b2.elem_bytes()));
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
ExpectedStdout: EQUALS `opt=24
box_opt=24
box_elem=8
box2_opt=24
box2_elem=8
`
ExpectedStderr: DISCARD
