# MethodTemplateNameCollidesWithFreeFunction

## Description

Regression sentinel for a pre-existing pass-3 monomorphize bug (found during the
batch 0.3 SemanticMonomorphize split; the split build emits byte-identical `.ll`
to the pre-split HEAD for this program, so the bug predates the refactor and is
NOT a regression of it).

When a class METHOD template (`Foo.identity<T>`) shares its bare name with a
top-level generic function template (`identity<T>`), the pass-3 fixpoint's
bare-name function-instantiation collection (`collect_func_inst_from_expr`,
EmperorPenguin/src/bound/SemanticMonomorphizeCollect.penguin) records the member
call `a.identity<string>("m")` under the bare name `identity`; the fixpoint then
matches it via `find_bound_function_def` to the TOP-LEVEL template —
`is_ambiguous_method_template` only counts same-named methods across classes, so
the class-method/free-function collision is not filtered — and a spurious
top-level `identity<string>` specialization is emitted alongside the real
`Foo.identity<string>`. After the collision, the explicit call
`identity<i32>(43)` is emitted with a void-typed argument
(`call i32 @..identity$..i32(void %t3)`), producing invalid IR: clang fails the
link with `void type only allowed for function results`.

Verified red on Pass1 (both the BabyPenguin-hosted pre-split HEAD build and the
batch 0.3 split build), Pass2 and Pass3: compile exit 0, link fails. Should turn
green on all of them once fixed (no spurious specialization, `m` and `43`
printed). BabyPenguin is not listed: its front-end cannot compile method
templates (E_RESOLVE_TYPE on the method-level `#template`), same as
GenericClassFunction.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
fun identity(x: T) -> T {
    return x;
}
class Foo {
    #template(T: type)
    fun identity(this, x: T) -> T { return x; }
}
initial {
    let a = new Foo();
    println(a.identity<string>("m"));
    println(cast<string>(identity<i32>(43)));
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
ExpectedStdout: EQUALS `m
43
`
ExpectedStderr: DISCARD
