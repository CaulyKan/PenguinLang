# SimpleSql
## Description
The Examples/simple_sql compile-time SQL parser, end to end: the demo seeds three in-memory tables (global std.Vector per row class in namespace db), then runs SELECT (WHERE placeholder bound to a runtime variable snapshot-captured by the generated predicate; WHERE with AND + SQL-inline string literal; OR across the SCORE table), UPDATE by id + verify re-select, INSERT + verify, and DELETE with the remaining table size. The SQL string literals are parsed by #fun meta calls at COMPILE TIME (tokenizer + recursive-descent WHERE parser + table/column reflection via resolve_type/t.fields()), and PenguinLang code is spliced at each call site. Requires native Pass3 with vector.penguin passed explicitly (the bootstrap pass3 has no auto-std); the whole example is compiled from its real files via Compile.Args, the Test Code block contributes no definitions.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
// The whole example is compiled via Compile.Args pointing at its real
// files: Examples/simple_sql/{main,model,sql}.penguin +
// EmperorPenguin/std/penguin/vector.penguin. This file contributes no
// definitions on top of them.
```

## Compile
Args: `Examples/simple_sql/main.penguin Examples/simple_sql/model.penguin Examples/simple_sql/sql.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `== seed ==
classes=3 students=6 scores=8
== select name={nm} ==
count=1
1:aa:class1
== select class_id=2 AND name='tom' ==
count=1
3:tom:class2
== select value>=90 OR subject='math' ==
count=5
1:math:95
3:math:72
4:math:91
6:chemistry:93
7:math:85
== update id=2 set name='bobby' ==
affected=1
== verify ==
count=1
2:bobby:class1
== insert (7,'newkid',3) ==
affected=1
== verify insert ==
count=1
7:newkid:class3
== delete class_id=2 ==
affected=2 remaining=5
`
ExpectedStderr: DISCARD
