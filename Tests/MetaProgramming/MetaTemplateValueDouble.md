# MetaTemplateValueDouble
## Description
A non-type (value) template parameter of type `f64`. `#template(N: f64) fun scale()` is specialized at runtime (D6): `scale<2.5>()` → `scale__2.5`, whose body substitutes `N` → `2.5` and computes `N * 2.0` at runtime. Exercises a value-template type other than i32.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: f64)
fun scale() -> f64 {
    return N * 2.0;
}
initial {
    println("s=" + cast<string>(scale<2.5>()));
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
ExpectedStdout: EQUALS `s=5
`
ExpectedStderr: DISCARD
