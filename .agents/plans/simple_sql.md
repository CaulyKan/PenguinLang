# Examples/simple_sql — 编译期 SQL 解析器(元编程示例)

> 状态:已批准,待实施。分支:`feat/simple-sql-example`(实施时创建)。
> 参考模式:`Tests/MetaProgramming/MetaPrintf.md`(R4 复合:#fun + unstructured_ast 尾块 +
> parse_arguments/get_ast/build_text + create_expression 拼接)。

## 目标

在 `Examples/simple_sql/` 新增一个示例项目:定义 Student/Class/Score 数据模型,初始化三张内存表
(全局 `std.Vector<T>` + 测试数据),并实现一套**编译期 SQL 解析器**——`#sql_query`/`#sql_execute`
元调用在编译时解析 SQL 字符串字面量、用反射校验表/列,然后生成对内存 Vector 的查询/改写代码拼接回
调用点。

**约束(已确认)**:元编程 JIT 只存在于原生 Pass2/Pass3(`#fun` + `std.Vector` 都是 BabyPenguin
不支持的),因此本示例与测试只 Apply To Pass3(Pass2 编好后 `--probe` 验证,绿则一并列入)。

## 用户语法 → 实际语法(两处适配)

用户示意与真实 API 有两处偏差,按真实 API 适配:

```
// SELECT:返回过滤迭代器,用户用 into<> 物化(Vector 的既定用法,
// 见 Tests/BuiltinTest/IteratorCombinatorsIntoVector.md)
let results: std.Vector<Student> =
    #sql_query("SELECT * FROM STUDENT WHERE STUDENT.name={}"){ "aa" }
        .into<std.Vector<Student>>();

// UPDATE / INSERT / DELETE:直接返回受影响行数(i64)
let affected: i64 = #sql_execute("UPDATE STUDENT SET name={} WHERE id={}") { "bob", 1 };
```

(`.into_vector()` 不存在,等价物是 `.into<std.Vector<Student>>()`;`Vector<Student>` 实际拼写
`std.Vector<Student>`。)

## 支持的 SQL 子集(v1)

- `SELECT * FROM T [WHERE cond]`(仅 `*`,投影留作扩展)
- `UPDATE T SET col={}, ... [WHERE cond]`,并兼容用户示意中的简写 `UPDATE T.col={} [WHERE cond]`
- `INSERT INTO T (col1, col2) VALUES ({}, {})`(列名必填,保证不依赖构造器参数序)
- `DELETE FROM T [WHERE cond]`
- WHERE 表达式(递归下降):`OR` < `AND` < `NOT` < `(cond)` / `col cmp value`;
  `cmp ∈ =, ==, !=, <>, <, <=, >, >=`(SQL `=`→`==`,`<>`→`!=`);
  `value ∈ {} 占位符 | 数字 | '字符串'/"字符串" | true/false`
- 关键字大小写不敏感;表/列名大小写不敏感匹配

**名称解析约定**:

- 表名 → 类型:`STUDENT` → `compiler().resolve_type("Student")`(首字母大写化后查,查不到
  `compiler().error`)
- 表名 → 运行时表变量:`STUDENT` → 依次探测 `resolve_symbol("students"/"studentes"/"student")`
  (小写 + `s`、+ `es`、裸小写,如 `classes`);都失败则报友好编译错误说明命名约定
- 列校验:对该 `BoundType` 遍历 `t.fields()`,列不存在即 `compiler().error`;`{}` 占位符数量必须
  等于 `{...}` 块中实参个数(`parse_arguments` 解析)

## 文件结构

```
Examples/simple_sql/
  simple_sql.penguins   # [Project] sources=["model.penguin","sql.penguin","main.penguin"]
                        # (发布版编译器经 auto-std 自动带 Vector)
  model.penguin         # Student/Class/Score 类 + 三张全局表 + initial 播种数据
  sql.penguin           # SQL 元编程库(全部 #fun + 一个 #class)
  main.penguin          # 演示:SELECT(WHERE/AND/OR)→ UPDATE/INSERT/DELETE → 再 SELECT 验证
  README.md             # 运行方式(release 编译器 / pass3 + 显式 vector 源)
Tests/ExampleTest/SimpleSql.md   # 端到端字节精确测试(仿 TinyRiscvHelloWorld 的
                                 # Compile.Args 传源文件方式)
```

## model.penguin(数据模型)

```penguin
class Student {
    impl __builtin.IReferenceType;   // 显式引用语义:at() 取出的是引用,UPDATE 经它改字段才生效
    id: mut i64 = 0;                 // 字段一律 mut,供 UPDATE 赋值
    name: mut string = "";
    class_id: mut i64 = 0;
    fun new(mut this) {}             // 零参构造:INSERT 代码生成与 into<C>() 都依赖它
}
class Class  { ... id/name/teacher ... }   // 若 `Class` 与关键字/内建冲突则改名 ClassInfo(表名同步)
class Score  { ... student_id/subject/value: i64 ... }

let students: mut std.Vector<Student> = new std.Vector<Student>();
let classes:  mut std.Vector<Class>   = new std.Vector<Class>();
let scores:   mut std.Vector<Score>   = new std.Vector<Score>();
```

播种放在 model.penguin 的 `initial`(`push(new Student)` 后逐字段赋值)。

**布局风险**:跨文件顶层全局变量能否被 main.penguin 中生成代码按非限定名解析,第 2 步先用
"手写等价查询代码"冒烟验证;不行就把全局表声明收进 main.penguin 或统一 namespace。

## sql.penguin(SQL 元库)

全部为 `#fun`(可被编译期 JIT 执行;`#fun` 间互相调用去掉 `#` 前缀直接调),参照 MetaPrintf +
argparse 的既有手法:

```penguin
#fun sql_query(sql: string, params: unstructured_ast) -> ast   // SELECT 入口,返回迭代器表达式
#fun sql_execute(sql: string, params: unstructured_ast) -> ast  // UPDATE/INSERT/DELETE,返回 i64 表达式

// 内部工具(均 #fun;元侧用 _utils.List<T>,无 Map):
#class _SqlCond { text: mut string = ""; next: mut i64 = 0; }   // 递归下降的 (表达式文本, 下一 token 下标) 返回值
_sql_tokenize(sql) -> _utils.List<string>     // 按空白/括号/运算符切词,引号串保持单 token(string_char_at 逐字符扫描)
_sql_lower / _sql_title / _sql_is_ident       // 大小写归一、标识符校验
_sql_arg_texts(params) -> _utils.List<string> // parse_arguments → get_ast → items[i].build_text()(MetaPrintf 手法)
_sql_resolve_table(table) -> string           // resolve_type("Student") 校验 + resolve_symbol 探测表变量名
_sql_field_exists(t, col) -> bool             // 遍历 t.fields(),大小写不敏感
_sql_parse_cond(tokens, pos, args, row_type, row_var) -> _SqlCond
                                              // OR→||、AND→&&、NOT→!、`=`→`==`、`<>`→`!=`;
                                              // TABLE.col/bare col → r.col(先经 _sql_field_exists 校验);
                                              // {} → 依次取 args[i]
```

**代码生成模板**(`compiler().create_expression`,生成文本在拼接点重绑定,可引用全局表变量与
`{...}` 实参):

```penguin
// SELECT * FROM T [WHERE c]  →
students.iter().filter(fun (r: Student) -> bool { return r.name == "aa"; })
// 无 WHERE 则只生成 students.iter();用户在其后接 .into<std.Vector<Student>>()

// UPDATE T SET a={} [WHERE c]  → 块表达式(argparse 已验证 create_expression("{ ... }") 可行)
{ let __affected: i64 = 0;
  let __i: mut i64 = 0;
  while (__i < cast<i64>(students.size())) {
    let mut r = students.at(cast<u64>(__i)).some;   // 引用语义,改字段即改表
    if (r.id == 1) { r.name = "bob"; __affected = __affected + 1; }
    __i = __i + 1; }
  __affected }

// INSERT INTO T (id, name) VALUES ({}, {})  →
{ let mut r = new Student(); r.id = 9; r.name = "new";
  students.push(r); 1 }

// DELETE FROM T [WHERE c]  → 重建保留向量后整体回赋(mut 全局可赋值,避开 resize_to 收缩语义不明)
{ let __affected: i64 = 0;
  let __kept: mut std.Vector<Student> = new std.Vector<Student>();
  ... 按下标扫描,不满足谓词的 push 进 __kept,满足的计数 ...
  students = __kept; __affected }
```

## main.penguin(演示,输出即测试期望)

1. 播种:3 个班、6 名学生、若干成绩
2. `SELECT * WHERE name={}`(参数为变量,展示运行期值经 build_text 拼接 + lambda 按值捕获)
3. `WHERE class_id={} AND name="tom"`(AND + SQL 内联字符串字面量)、
   `WHERE value>=90 OR subject="math"`(跨表 Score)
4. UPDATE 改名并打印 affected;再 SELECT 验证
5. INSERT 新生;SELECT 验证
6. DELETE 满足谓词的行;打印 affected + 剩余 size

打印用 `println(cast<string>(...))` 拼接,stdout 逐字节写进测试。

## 实施步骤(每步一个 commit,分支 `feat/simple-sql-example`)

1. **准备**:建分支;检查 `build/bootstrap/pass3` 存在,缺失则 `make bootstrap`;
   写 PenguinLang 代码时调用 `penguinang-coding` skill
2. **模型冒烟**:model.penguin + main.penguin 用**手写**
   `students.iter().filter(fun (r: Student) -> bool {...}).into<...>()` 跑通
   (pass3 + `emperor_penguin link`),顺带验证:跨文件全局表解析、
   `let mut r = v.at(i).some; r.f = x;` 引用改写、`Class` 标识符、块表达式在初始化位置绑定
   ——这四点是生成代码的前提,有坑在此步排掉(踩到编译器 bug 按 AGENTS 规范落 Tests/ 回归用例)
3. **词法 + WHERE 解析**:sql.penguin 的 tokenize/parse_cond 系列 `#fun` 写完并用小型 `#fun`
   自测(如编译期把解析结果 `compiler().info()` 打出来人工核对)
4. **#sql_query**:SELECT 生成 + main.penguin 换用 SQL 语法跑通
5. **#sql_execute**:UPDATE/INSERT/DELETE 三条模板
6. **测试落档**:`Tests/ExampleTest/SimpleSql.md`
   (Compile.Args 显式列 4 个源文件:`Examples/simple_sql/{main,model,sql}.penguin
   EmperorPenguin/std/penguin/vector.penguin`;Apply To: Pass3;stdout EQUALS);
   可选加一个负向用例(未知列 → 编译失败)
7. **收尾**:README、`--probe --compilers pass2` 探测(Pass2 绿则 Apply To 加 Pass2)、
   全量验证、最终 commit

## 验证命令

```bash
# 快速直跑(冒烟)
build/bootstrap/pass3 Examples/simple_sql/main.penguin Examples/simple_sql/model.penguin \
  Examples/simple_sql/sql.penguin EmperorPenguin/std/penguin/vector.penguin -o /tmp/simple_sql
EmperorPenguin/emperor_penguin link /tmp/simple_sql.ll -o /tmp/simple_sql && /tmp/simple_sql

# 套件(日志按 AGENTS 规范 tee 落盘)
dotnet run --project Tests/PenguinTestRunner.csproj -- --filter "ExampleTest/SimpleSql*" --compilers pass3 2>&1 | tee /tmp/simple_sql_test.log
# 探测 pass2
dotnet run --project Tests/PenguinTestRunner.csproj -- --probe --compilers pass2 --filter "ExampleTest/SimpleSql*" 2>&1 | tee /tmp/simple_sql_pass2.log
```

## 风险与退路

| 风险 | 退路 |
|---|---|
| 块表达式 `{...}` 在 `let x = #sql_execute(...){...};` 初始化位置不绑定 | 第 2 步先行冒烟;失败则改为谓词无循环的写法或调整演示为语句位拼接(生成代码内直接 println),保底让元调用在表达式位只生成单表达式 |
| `let mut r = vec.at(i).some; r.field = v;` 不可行 | `impl __builtin.IReferenceType` 已强制引用语义,应可行;不行则 UPDATE 退化为 `set(i, 重建对象)` |
| 顶层全局表跨文件解析失败 | 全局表与演示 initial 收进同一文件/同一 namespace |
| `Class` 标识符冲突 | 改名 `ClassInfo`,表名 CLASSINFO |
| `resolve_symbol` 探测不到表变量 | 去掉探测,靠拼接点自然绑定报错(错误信息稍差,功能不受影响) |

## 明确不做

- 不改 docs 站点(避免双语双树开销),仅示例内 README
- SELECT 列投影 / JOIN / ORDER BY / LIMIT / 聚合(留作扩展点)
- `make test` 自动纳入新用例,CI 无需改动
