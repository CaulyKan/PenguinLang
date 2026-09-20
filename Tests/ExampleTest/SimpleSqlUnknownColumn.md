# SimpleSqlUnknownColumn
## Description
Negative path of the Examples/simple_sql compile-time SQL parser: an UPDATE naming a column the row class does not have fails AT COMPILE TIME — the meta layer reflects over the resolved row type (t.fields(), case-insensitive) and reports `error[E_META]: simple_sql: table STUDENT has no column 'nosuchcol'` (followed by the resolve error of the intentionally-emitted fallback assignment). Requires native Pass3 with the example's model + sql files and vector.penguin passed explicitly.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
// The data model and SQL meta layer come from the example's real files
// via Compile.Args; the program body is this single bad UPDATE.
initial {
    let x: i64 = #sql_execute("UPDATE STUDENT SET nosuchcol={} WHERE id={}") { "x", 1 };
    println(cast<string>(x));
}
```

## Compile
Args: `Examples/simple_sql/model.penguin Examples/simple_sql/sql.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
