# LibTemplateSourceShipped
## Description
libmeta v1 mixed keep-set: a lib built from TWO kinds of sources — vector.penguin (contains `#template`, so it must ship VERBATIM as a source entry) plus a plain file with a non-generic `export class` (ships as a structured symbol-table entry). The consumer (a) instantiates a NEW generic instance the lib never shipped (re-monorphized locally from the shipped template source) and (b) calls the exported non-generic class whose body lives only in the .so (declaration materialized from the symbol table). Exercises the source-entry + decl-entry paths coexisting in one metadata document. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace mixed {
    export class Stamp {
        seq: i64;

        fun new(mut this, seq: i64) {
            this.seq = seq;
        }

        fun describe(this) -> string {
            return "stamp#" + cast<string>(this.seq);
        }
    }
}
```
## Build 1
Kind: lib
Name: mixed.penguin-lib
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    // (a) NEW instance (u16 keys) — never instantiated inside the lib; its
    // template source shipped verbatim so the consumer specializes it locally.
    let mut v = new std.Vector<u16>();
    v.push(cast<u16>(3));
    v.push(cast<u16>(4));
    let sum: mut i64 = 0;
    for (let x in v) { sum = sum + cast<i64>(x); }
    println("vec=" + cast<string>(v.size()) + ":" + cast<string>(sum));
    // (b) exported non-generic class — body in the .so, decl from the table.
    let s: mixed.Stamp = new mixed.Stamp(42);
    println(s.describe());
}
```
## Build 2
Args: `--lib ${WORKDIR}/mixed.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `vec=2:7
stamp#42
`
ExpectedStderr: DISCARD
