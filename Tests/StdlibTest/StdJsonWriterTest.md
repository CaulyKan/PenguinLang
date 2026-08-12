# StdJsonWriterTest
## Description
End-to-end test of the stdlib `penguin.JsonWriter` from `EmperorPenguin/std/penguin/json.penguin` (NOT auto-loaded; passed via Compile.Args). Streaming builder: an object with an escaped string (`"`/`\`/`\n`/`\t`), an i64, a double, a bool, an array, and a null — asserted byte-exact. Also a nested array-of-object. The writer emits no whitespace and preserves insertion order (unlike HashMap-backed object serialization). Pass3-only (pointer-IR deps Vector/HashMap; EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let mut w = new penguin.JsonWriter();
    w.begin_object();
    w.key("name"); w.value_string("a\"b\\c\nd\te");
    w.key("n"); w.value_i64(42);
    w.key("pi"); w.value_double(3.5);
    w.key("ok"); w.value_bool(true);
    w.key("tags");
    w.begin_array();
    w.value_i64(1);
    w.value_i64(2);
    w.end_array();
    w.key("nil"); w.value_null();
    w.end_object();
    println("out=" + w.to_string());

    let mut a = new penguin.JsonWriter();
    a.begin_array();
    a.begin_object();
    a.key("z"); a.value_string("x");
    a.end_object();
    a.end_array();
    println("nested=" + a.to_string());
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
ExpectedStdout: EQUALS `out={"name":"a\"b\\c\nd\te","n":42,"pi":3.5,"ok":true,"tags":[1,2],"nil":null}
nested=[{"z":"x"}]
`
ExpectedStderr: DISCARD
