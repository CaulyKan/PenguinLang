# simple_sql — a compile-time SQL parser

A metaprogramming example: SQL statements written as string literals are
parsed **at compile time**, verified against the real data model, and
turned into plain PenguinLang code spliced at the call site. There is no
SQL engine at runtime — after compilation the program is ordinary
`std.Vector` scans.

```penguin
let nm: string = "aa";
let r1: std.Vector<Student> =
    #sql_query("SELECT * FROM STUDENT WHERE name={}"){ nm }.into<std.Vector<Student>>();

let affected: i64 =
    #sql_execute("UPDATE STUDENT SET name={} WHERE id={}") { "bobby", 2 };
```

## How it works

`sql.penguin` is pure `#fun` meta code (JIT-executed by native
EmperorPenguin pass2/pass3 at compile time):

1. the SQL string is tokenized (`_sql_tokenize`);
2. tables and columns are resolved against the *bound* types —
   `compiler().resolve_type("Student")` + `t.fields()` reflection — so a
   typo in a table or column name is a **compile error**, not a runtime one;
3. the `{}` placeholders are matched against the `{...}` trailing block's
   arguments (each argument's expression text is spliced verbatim, so
   runtime variables work — the generated predicate snapshot-captures
   them);
4. the WHERE clause goes through a recursive-descent parser
   (`OR` < `AND` < `NOT` < `(cond)` / `col cmp value`);
5. the equivalent PenguinLang is built as text and returned through
   `compiler().create_expression`, splicing it at the call site.

### Generated code shapes

```penguin
// SELECT * FROM STUDENT WHERE name={}   (predicate pre-bound: see below)
{ let __pred: mut fun<bool, db.Student> = fun (r: db.Student) -> bool { return r.name == nm; };
  db.students.iter().filter(__pred) }        // caller appends .into<...>()

// UPDATE STUDENT SET name={} WHERE id={}   (rows are reference types:
//                                            mutation lands in the table)
{ let __affected: mut i64 = 0; let __i: mut i64 = 0;
  while (__i < cast<i64>(db.students.size())) {
    let mut r = db.students.at(cast<u64>(__i)).some;
    if (r.id == 2) { r.name = "bobby"; __affected = __affected + 1; }
    __i = __i + 1; }
  __affected }

// INSERT INTO STUDENT (id, name) VALUES ({}, {})
{ let mut r = new db.Student(); r.id = 7; r.name = "newkid";
  db.students.push(r); 1 }

// DELETE FROM STUDENT WHERE class_id={}
{ let __affected: mut i64 = 0;
  let __kept: mut std.Vector<db.Student> = new std.Vector<db.Student>();
  ... kept rows pushed, matching rows counted ...
  db.students = __kept; __affected }
```

The predicate lambda is bound to a local *before* being passed to
`filter()` because an inline capturing lambda whose call result is
immediately chained loses its capture fields on EmperorPenguin — see
`Tests/LambdaTest/LambdaCaptureChainedCall.md` (red sentinel).

## SQL subset (v1)

- `SELECT * FROM T [WHERE cond]` — projection, JOIN, ORDER BY, LIMIT and
  aggregates are left as extension points
- `UPDATE T SET a={}, b={} [WHERE cond]`, plus the shorthand
  `UPDATE T.a={} [WHERE cond]`
- `INSERT INTO T (a, b) VALUES ({}, {})` — column names required
- `DELETE FROM T [WHERE cond]`
- WHERE: `OR` < `AND` < `NOT` < `(cond)` / `col cmp value`;
  `cmp ∈ =, ==, !=, <>, <, <=, >, >=`; values are `{}` placeholders,
  numbers, `'strings'`/`"strings"`, `true`/`false`
- keywords, table names and column names are all case-insensitive

## Conventions

- row types and table globals live in the `db` namespace: table `STUDENT`
  resolves type `db.Student` and probes the variable `db.students` /
  `db.studentes` / `db.student`
- columns match class fields case-insensitively; the canonical field
  spelling is emitted
- `{}` count must equal the `{...}` block's argument count

## Build & run

From the repo root (see `simple_sql.penguins` for the exact commands):

```bash
# released emitter + repo driver (auto-std pulls std.Vector from the dynlib)
EmperorPenguin/emperor_penguin --emitter build/linux/emperor_penguin_llvm_emitter \
    Examples/simple_sql/simple_sql.penguins -o /tmp/simple_sql
/tmp/simple_sql

# bootstrap pass3 (no auto-std: pass vector.penguin explicitly)
build/bootstrap/pass3 Examples/simple_sql/main.penguin \
    Examples/simple_sql/model.penguin Examples/simple_sql/sql.penguin \
    EmperorPenguin/std/penguin/vector.penguin -o /tmp/simple_sql
EmperorPenguin/emperor_penguin link /tmp/simple_sql.ll -o /tmp/simple_sql && /tmp/simple_sql
```

The meta JIT exists only in native pass2/pass3, so the example (and its
tests, `Tests/ExampleTest/SimpleSql*.md`) applies to those compilers only.

## Files

- `model.penguin` — Student/Class/Score row classes + the three table
  globals + `db.seed()` (deterministic seeding, called from the demo's
  single `initial`)
- `sql.penguin` — the `#sql_query` / `#sql_execute` meta library
  (tokenizer, WHERE parser, reflection, codegen)
- `main.penguin` — the demo; its stdout is pinned byte-exact by
  `Tests/ExampleTest/SimpleSql.md`
