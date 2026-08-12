# StdJsonValueTest
## Description
End-to-end test of the stdlib JSON DOM from `EmperorPenguin/std/penguin/json.penguin` (NOT auto-loaded; passed via Compile.Args): `penguin.parse_json` builds a `JsonValue` tree; `get()/as_*()` read primitives, arrays, and nested objects; kind predicates (`is_int`/`is_null`/`is_object`); ordered `to_json` on arrays. Exercises the recursive-descent `JsonReader` (numbers, strings, booleans, null, nesting). Pass3-only (pointer-IR deps Vector/HashMap; EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let v = penguin.parse_json("{\"a\":1,\"b\":[true,\"x\"],\"c\":{\"d\":2.5},\"s\":\"hi\",\"n\":null}");
    println("a=" + cast<string>(v.get("a").some.as_i64()));
    println("s=" + v.get("s").some.as_string());
    let b = v.get("b").some.as_array();
    println("b0=" + cast<string>(b.at(0).some.as_bool()) + " b1=" + b.at(1).some.as_string());
    let c = v.get("c").some.as_object();
    println("d=" + cast<string>(c.get("d").some.as_f64()));
    println("is_obj=" + cast<string>(v.is_object()));
    println("arr=" + v.get("b").some.to_json());

    let num = penguin.parse_json("42");
    println("num_is_int=" + cast<string>(num.is_int()));
    println("num=" + cast<string>(num.as_i64()));
    let fl = penguin.parse_json("3.25");
    println("fl=" + cast<string>(fl.as_f64()));
    let tr = penguin.parse_json("true");
    println("tr=" + cast<string>(tr.as_bool()));
    let nl = penguin.parse_json("null");
    println("nl_is_null=" + cast<string>(nl.is_null()));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/json.penguin EmperorPenguin/std/penguin/hashmap.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a=1
s=hi
b0=true b1=x
d=2.5
is_obj=true
arr=[true,"x"]
num_is_int=true
num=42
fl=3.25
tr=true
nl_is_null=true
`
ExpectedStderr: DISCARD
