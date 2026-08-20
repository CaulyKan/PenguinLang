# PrintNonStringValue
## Description
The print family (print/println/eprint/eprintln) is declared `print(text: string)` but the
reference compiler (BabyPenguin VM and C# backend) accepts ANY basic-typed value and
stringifies it at runtime — `print(42)` prints "42". EmperorPenguin used to emit the scalar
argument with its own IR type (`call void @_emperor_print(i32 %t)`), which is invalid LLVM IR
and failed at the clang link step. Fixed in IRGenerator.lower_function_call: a primitive
non-string argument to the extern print family is wrapped in an int/bool/float→string CAST
before the call. Verified on all compilers.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        print(42);
        print(",");
        println(7);
        print(true);
        print(false);
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
ExpectedStdout: EQUALS `42,7
truefalse`
ExpectedStderr: DISCARD
