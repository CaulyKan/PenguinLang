# LibGlobalInterposition
## Description
A .penguin-lib may declare GLOBAL variables: the consumer re-defines them from the lib's embedded source and initializes them (both literal initializers and function-call `<init>` initializers run in the consumer's startup chain), while the lib's own `.so` copies are interposed away — the exe's `-rdynamic` definitions win the GOT-mediated data references of the `-fPIC` lib, so lib code and consumer code observe ONE initialized copy. This exercises the exact pattern the compiler lib (EmperorPenguinLib.penguins, MetaHost's `active_*` globals) relies on. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace glib {
    let counter: mut i64 = 7;
    let label: mut string = make_default();

    export fun make_default() -> string { return "default-label"; }
    export fun read_counter() -> i64 { return counter; }
    export fun bump(n: i64) -> i64 {
        counter = counter + n;
        return counter;
    }
    export fun read_label() -> string { return label; }
}
```
## Build 1
Kind: lib
Name: glib.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    println("c0=" + cast<string>(glib.read_counter()));
    println("bump=" + cast<string>(glib.bump(5)));
    println("c2=" + cast<string>(glib.read_counter()));
    println("label=" + glib.read_label());
}
```
## Build 2
Args: `--lib ${WORKDIR}/glib.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `c0=7
bump=12
c2=12
label=default-label
`
ExpectedStderr: DISCARD
