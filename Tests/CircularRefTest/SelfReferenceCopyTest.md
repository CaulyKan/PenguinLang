# SelfReferenceCopyTest
## Description
A self-referencing class (`next: mut Option<Node>` pointing back at `Node`)
with a bodyless `impl ICopy<Self>;` is REJECTED at compile time: the cycle
forces Node to be reference-like, so the auto-generated copy would be a
shallow clone sharing the referenced nodes. These programs used to rely on
the compiler picking a clone depth (VM: recursive clone, native backends:
memcpy) — the ICopy contract now demands an explicit
`fun copy(this) -> Self` instead (see ValueTypeTest/ICopyExplicitCopyMethod
for the accepted spelling). Verified as a compile error on BabyPenguin
(VM + CS) and EmperorPenguin Pass1/Pass2/Pass3
(`error[E_INTERFACE_IMPL]`, "implements ICopy without providing a 'copy'
function ... shallow-copy the shared reference").

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Node {
        value: mut i64 = 0;
        next: mut Option<Node> = new Option<Node>.none();
        impl ICopy<Self>;
    }
    initial {
        let a : mut Node = new Node();
        a.value = 1;
        a.next = new Option<Node>.some(a);

        let b : mut Node = a.copy();
        b.value = 10;

        print(cast<string>(a.value));
        print(cast<string>(b.value));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
