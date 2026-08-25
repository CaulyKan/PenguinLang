# PanicInModuleInitial
## Description
An uncaught panic inside a class-body initial routine runs the program up to the panic (module initials spawn during construct, so the module prints first), then exits non-zero with an "Uncaught runtime error" diagnostic on stderr; buffered stdout is flushed, not swallowed. BabyPenguin is interpreted (compile stage = full run), so the expectations live in the Compile section (same shape as ExceptionTest/UncaughtError). Previously a bogus compile-time error[E_RESOLVE_TYPE] carrying the panic message appeared and no output was printed (the Penguin-level code 1 collided with the C# ErrorCode enum, and the quiet-mode output buffer was dropped on the exception path).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class M { input x : i64 = 0; initial { println("module-start"); wait 1 tick; panic("boom"); } }
construct { let m : mut M = new M(); }
initial { println("before"); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: NONZERO
ExpectedStdout: EQUALS `module-start
before
panic: boom
`
ExpectedStderr: CONTAINS `Uncaught runtime error`
