# FixedBufValueParam
## Description
Build a `.penguin-lib` shipping `FixedBuf<i32,5>` — a mixed type+value class template (`export class FixedBuf<T,N>` with `N: u64` stashed in a field; the std.Array carrier was retired with the raw-pointer intrinsics, this keeps the value-template-param path covered). A seed instantiates `FixedBuf<i32,5>`; the consumer uses the shipped instance, exercising the value-template-param path across the lib boundary. KNOWN LIMITATION (native-arrays round): the `__builtin.__array_*` declaration-only intrinsics do not specialize inside a libmeta-injected pre-specialized method body (the specialized callee is never emitted into the lib), so the carrier avoids array intrinsics — the std dyn-lib itself is unaffected (its #template files ship as verbatim source and specialize in the consumer). Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace std {
#template(T: type, N: u64)
export class FixedBuf {
    impl __builtin.IReferenceType;
    n: u64 = N;

    fun new(mut this) {}

    fun size(this) -> u64 { return this.n; }

    fun double_n(this) -> u64 { return this.n * 2; }
}
}

fun __force_std_exports() {
    let _a = new std.FixedBuf<i32, 5>();
}
```
## Build 1
Kind: lib
Name: std.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let mut a = new std.FixedBuf<i32, 5>();
    println("size=" + cast<string>(a.size()));
    println("double=" + cast<string>(a.double_n()));
}
```
## Build 2
Args: `--lib ${WORKDIR}/std.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=5
double=10
`
ExpectedStderr: DISCARD
