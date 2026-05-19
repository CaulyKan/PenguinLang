using BabyPenguin;

namespace EmperorPenguin.Tests;

public class ASTRoundTripParseTest
{
    private static readonly BatchResults Batch = BatchCompiler.InitParseBatch<ASTRoundTripParseTest>();

    private string ParseWithMethod(string source, string parseMethod)
    {
        var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var userCode = @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let p = new parser.Parser(tokens);
    let result = p.{parseMethod}();
    println(result.build_text());
}}";
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddFile(Path.Combine(BatchCompiler.AstDir, "Token.penguin"));
        compiler.AddFile(Path.Combine(BatchCompiler.AstDir, "Lexer.penguin"));
        compiler.AddFile(Path.Combine(BatchCompiler.AstDir, "AST.penguin"));
        compiler.AddFile(Path.Combine(BatchCompiler.AstDir, "Parser.penguin"));
        compiler.AddSource(userCode);
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("VM timed out in ParseWithMethod");
        return vm.CollectOutput().Trim();
    }

    private string ParseRoundTrip(string source)
    {
        var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var userCode = @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new parser.Lexer(source);
    let tokens = lexer.tokenize();
    let p = new parser.Parser(tokens);
    let result = p.parse_compilationUnit();
    println(result.build_text());
}}";
        var compiler = new SemanticCompiler(new ErrorReporter());
        foreach (var f in Directory.GetFiles(BatchCompiler.AstDir, "*.penguin"))
            compiler.AddFile(f);
        compiler.AddSource(userCode);
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("VM timed out in ParseRoundTrip");
        return vm.CollectOutput().Trim();
    }

    // ==================== Expression round-trip tests ====================

    [Fact]
    [BatchParseTest("(1+2)", "parse_expression", "(1 + 2)")]
    public void ParseRoundTrip_Parenthesized() => Batch.Assert();

    [Fact]
    [BatchParseTest("((1+2))", "parse_expression", "((1 + 2))")]
    public void ParseRoundTrip_DeeplyNestedParenthesized() => Batch.Assert();

    [Fact]
    [BatchParseTest("(42)", "parse_expression", "(42)")]
    public void ParseRoundTrip_ParenthesizedSingleValue() => Batch.Assert();

    [Fact]
    [BatchParseTest("cast<i64>(x)", "parse_expression", "cast<i64>(x)")]
    public void ParseRoundTrip_CastExpression() => Batch.Assert();

    [Fact]
    [BatchParseTest("cast<f64>(1+2)", "parse_expression", "cast<f64>(1 + 2)")]
    public void ParseRoundTrip_CastWithExpression() => Batch.Assert();

    [Fact]
    [BatchParseTest("cast<i64>((1+2))", "parse_expression", "cast<i64>((1 + 2))")]
    public void ParseRoundTrip_CastWithParenthesized() => Batch.Assert();

    [Fact]
    [BatchParseTest("true", "parse_expression", "true")]
    public void ParseRoundTrip_BoolTrue() => Batch.Assert();

    [Fact]
    [BatchParseTest("false", "parse_expression", "false")]
    public void ParseRoundTrip_BoolFalse() => Batch.Assert();

    [Fact]
    [BatchParseTest("1+2", "parse_expression", "1 + 2")]
    public void ParseRoundTrip_BinaryAdd() => Batch.Assert();

    [Fact]
    [BatchParseTest("5-3", "parse_expression", "5 - 3")]
    public void ParseRoundTrip_BinarySubtract() => Batch.Assert();

    [Fact]
    [BatchParseTest("2*3", "parse_expression", "2 * 3")]
    public void ParseRoundTrip_BinaryMultiply() => Batch.Assert();

    [Fact]
    [BatchParseTest("10/3", "parse_expression", "10 / 3")]
    public void ParseRoundTrip_BinaryDivide() => Batch.Assert();

    [Fact]
    [BatchParseTest("10%3", "parse_expression", "10 % 3")]
    public void ParseRoundTrip_BinaryModulo() => Batch.Assert();

    [Fact]
    [BatchParseTest("x>10", "parse_expression", "x > 10")]
    public void ParseRoundTrip_BinaryGreaterThan() => Batch.Assert();

    [Fact]
    [BatchParseTest("x<10", "parse_expression", "x < 10")]
    public void ParseRoundTrip_BinaryLessThan() => Batch.Assert();

    [Fact]
    [BatchParseTest("x<=10", "parse_expression", "x <= 10")]
    public void ParseRoundTrip_BinaryLessEqual() => Batch.Assert();

    [Fact]
    [BatchParseTest("x>=10", "parse_expression", "x >= 10")]
    public void ParseRoundTrip_BinaryGreaterEqual() => Batch.Assert();

    [Fact]
    [BatchParseTest("x==y", "parse_expression", "x == y")]
    public void ParseRoundTrip_BinaryEqual() => Batch.Assert();

    [Fact]
    [BatchParseTest("x!=y", "parse_expression", "x != y")]
    public void ParseRoundTrip_BinaryNotEqual() => Batch.Assert();

    [Fact]
    [BatchParseTest("a&&b", "parse_expression", "a && b")]
    public void ParseRoundTrip_BinaryLogicalAnd() => Batch.Assert();

    [Fact]
    [BatchParseTest("a||b", "parse_expression", "a || b")]
    public void ParseRoundTrip_BinaryLogicalOr() => Batch.Assert();

    [Fact]
    [BatchParseTest("a&b", "parse_expression", "a & b")]
    public void ParseRoundTrip_BinaryBitwiseAnd() => Batch.Assert();

    [Fact]
    [BatchParseTest("a|b", "parse_expression", "a | b")]
    public void ParseRoundTrip_BinaryBitwiseOr() => Batch.Assert();

    [Fact]
    [BatchParseTest("a^b", "parse_expression", "a ^ b")]
    public void ParseRoundTrip_BinaryBitwiseXor() => Batch.Assert();

    [Fact]
    [BatchParseTest("x is y", "parse_expression", "x is y")]
    public void ParseRoundTrip_BinaryIs() => Batch.Assert();

    [Fact]
    [BatchParseTest("-x", "parse_expression", "-x")]
    public void ParseRoundTrip_UnaryNegate() => Batch.Assert();

    [Fact]
    [BatchParseTest("!x", "parse_expression", "!x")]
    public void ParseRoundTrip_UnaryNot() => Batch.Assert();

    [Fact]
    [BatchParseTest("~x", "parse_expression", "~x")]
    public void ParseRoundTrip_UnaryBitwiseNot() => Batch.Assert();

    [Fact]
    [BatchParseTest("-(1+2)", "parse_expression", "-(1 + 2)")]
    public void ParseRoundTrip_UnaryNegateComplex() => Batch.Assert();

    [Fact]
    [BatchParseTest("!(x==1)", "parse_expression", "!(x == 1)")]
    public void ParseRoundTrip_UnaryNotComplex() => Batch.Assert();

    [Fact]
    [BatchParseTest("--x", "parse_expression", "--x")]
    public void ParseRoundTrip_DoubleNegate() => Batch.Assert();

    [Fact]
    [BatchParseTest("a.b", "parse_expression", "a.b")]
    public void ParseRoundTrip_MemberAccess() => Batch.Assert();

    [Fact]
    [BatchParseTest("a.b.c", "parse_expression", "a.b.c")]
    public void ParseRoundTrip_ChainedMemberAccess() => Batch.Assert();

    [Fact]
    [BatchParseTest("f()", "parse_expression", "f()")]
    public void ParseRoundTrip_FunctionCallNoArgs() => Batch.Assert();

    [Fact]
    [BatchParseTest("f(1)", "parse_expression", "f(1)")]
    public void ParseRoundTrip_FunctionCallOneArg() => Batch.Assert();

    [Fact]
    [BatchParseTest("f(1,2,3)", "parse_expression", "f(1, 2, 3)")]
    public void ParseRoundTrip_FunctionCallMultipleArgs() => Batch.Assert();

    [Fact]
    [BatchParseTest("a.b.c(1)", "parse_expression", "a.b.c(1)")]
    public void ParseRoundTrip_ChainedMemberThenCall() => Batch.Assert();

    [Fact]
    [BatchParseTest("f(1).g(2)", "parse_expression", "f(1).g(2)")]
    public void ParseRoundTrip_CallThenMemberThenCall() => Batch.Assert();

    [Fact]
    [BatchParseTest("1+2+3+4", "parse_expression", "1 + 2 + 3 + 4")]
    public void ParseRoundTrip_ChainedAddition() => Batch.Assert();

    [Fact]
    [BatchParseTest("1+2*3", "parse_expression", "1 + 2 * 3")]
    public void ParseRoundTrip_MixedPrecedence() => Batch.Assert();

    [Fact]
    [BatchParseTest("1+2*3-4/5", "parse_expression", "1 + 2 * 3 - 4 / 5")]
    public void ParseRoundTrip_ComplexPrecedence() => Batch.Assert();

    [Fact]
    [BatchParseTest("if (1) { 2 }", "parse_expression", "if (1) { 2 }")]
    public void ParseRoundTrip_IfNoElse() => Batch.Assert();

    [Fact]
    [BatchParseTest("if (1) { 2 } else { 3 }", "parse_expression", "if (1) { 2 } else { 3 }")]
    public void ParseRoundTrip_IfWithElse() => Batch.Assert();

    [Fact]
    [BatchParseTest("if (a && b) { 1 } else { 0 }", "parse_expression", "if (a && b) { 1 } else { 0 }")]
    public void ParseRoundTrip_IfWithComplexCondition() => Batch.Assert();

    [Fact]
    [BatchParseTest("while (x) { 42 }", "parse_expression", "while (x) { 42 }")]
    public void ParseRoundTrip_WhileSimple() => Batch.Assert();

    [Fact]
    [BatchParseTest("while (x > 0) { 42 }", "parse_expression", "while (x > 0) { 42 }")]
    public void ParseRoundTrip_WhileWithCondition() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ }", "parse_expression", "{ }")]
    public void ParseRoundTrip_CodeBlockEmpty() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ 1; 2; 3 }", "parse_expression", "{ 1; 2; 3 }")]
    public void ParseRoundTrip_CodeBlockStatements() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ 1; 2; 3; 4 }", "parse_expression", "{ 1; 2; 3; 4 }")]
    public void ParseRoundTrip_CodeBlockStatementsOnly() => Batch.Assert();

    // ==================== Nested block expression tests ====================

    [Fact]
    [BatchParseTest("{ if (1) { 2 } 3 }", "parse_expression", "{ if (1) { 2 } 3 }")]
    public void ParseRoundTrip_CodeBlockWithIfAndTrailingExpr() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ if (a) { 1 } if (b) { 2 } }", "parse_expression", "{ if (a) { 1 } if (b) { 2 } }")]
    public void ParseRoundTrip_CodeBlockWithMultipleIfs() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ while (x) { 1 } while (y) { 2 } }", "parse_expression", "{ while (x) { 1 } while (y) { 2 } }")]
    public void ParseRoundTrip_CodeBlockWithMultipleWhiles() => Batch.Assert();

    // ==================== Complex expression tests ====================

    [Fact]
    [BatchParseTest("while (x) { if (1) { 2 } }", "parse_expression", "while (x) { if (1) { 2 } }")]
    public void ParseRoundTrip_NestedWhileIf() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ if (1) { 2 } 3 }", "parse_expression", "{ if (1) { 2 } 3 }")]
    public void ParseRoundTrip_NestedIfInCodeBlock() => Batch.Assert();

    [Fact]
    [BatchParseTest("while (x > 0 && x < 100) { x }", "parse_expression", "while (x > 0 && x < 100) { x }")]
    public void ParseRoundTrip_WhileWithBinaryCondition() => Batch.Assert();

    [Fact]
    [BatchParseTest("if (a) { if (b) { 1 } } else { 2 }", "parse_expression", "if (a) { if (b) { 1 } } else { 2 }")]
    public void ParseRoundTrip_IfElseNestedCodeBlocks() => Batch.Assert();

    [Fact]
    [BatchParseTest("{ if (1) { 2 } while (x) { 3 } 4 }", "parse_expression", "{ if (1) { 2 } while (x) { 3 } 4 }")]
    public void ParseRoundTrip_CodeBlockWithIfAndWhile() => Batch.Assert();

    [Fact]
    [BatchParseTest("a||b&&c|d^e&f==g!=h<i>j+ k-l*m/n", "parse_expression", "a || b && c | d ^ e & f == g != h < i > j + k - l * m / n")]
    public void ParseRoundTrip_ComplexBinaryAllLevels() => Batch.Assert();

    [Fact]
    [BatchParseTest("-a+b.c(1)*3", "parse_expression", "-a + b.c(1) * 3")]
    public void ParseRoundTrip_ComplexExpression() => Batch.Assert();

    [Fact]
    [BatchParseTest("(1+2)*(3-4)", "parse_expression", "(1 + 2) * (3 - 4)")]
    public void ParseRoundTrip_ParenthesizedWithBinary() => Batch.Assert();

    [Fact]
    [BatchParseTest("cast<i64>(x)+1", "parse_expression", "cast<i64>(x) + 1")]
    public void ParseRoundTrip_CastInsideBinary() => Batch.Assert();

    [Fact]
    [BatchParseTest("-a.b", "parse_expression", "-a.b")]
    public void ParseRoundTrip_UnaryWithMemberAccess() => Batch.Assert();

    // ==================== Type specifier tests ====================

    [Fact]
    [BatchParseTest("List<i64>", "parse_typeSpecifier", "List<i64>")]
    public void ParseType_ListGeneric() => Batch.Assert();

    [Fact]
    [BatchParseTest("Map<i64, string>", "parse_typeSpecifier", "Map<i64, string>")]
    public void ParseType_MultipleGenericArgs() => Batch.Assert();

    [Fact]
    [BatchParseTest("List<List<i64>>", "parse_typeSpecifier", "List<List<i64>>")]
    public void ParseType_NestedGeneric() => Batch.Assert();

    [Fact]
    [BatchParseTest("mut i64", "parse_typeSpecifier", "mut i64")]
    public void ParseType_Mutable() => Batch.Assert();

    [Fact]
    [BatchParseTest("foo.bar.Baz", "parse_typeSpecifier", "foo.bar.Baz")]
    public void ParseType_Qualified() => Batch.Assert();

    // ==================== Function parameter tests ====================

    [Fact]
    [BatchParseTest("fun(x: i64) { 42 }", "parse_lambdaFunctionExpression", "fun(x: i64) { 42 }")]
    public void ParseLambda_WithTypedParam() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun(x: i64, y: string) { 42 }", "parse_lambdaFunctionExpression", "fun(x: i64, y: string) { 42 }")]
    public void ParseLambda_WithMultipleParams() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun() { 42 }", "parse_lambdaFunctionExpression", "fun() { 42 }")]
    public void ParseLambda_NoParams() => Batch.Assert();

    // ==================== Return type tests ====================

    [Fact]
    [BatchParseTest("fun(x: i64) -> i64 { x }", "parse_lambdaFunctionExpression", "fun(x: i64) -> i64 { x }")]
    public void ParseLambda_WithReturnType() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun() -> string { \"hello\" }", "parse_lambdaFunctionExpression", "fun() -> string { \"hello\" }")]
    public void ParseLambda_WithStringReturnType() => Batch.Assert();

    // ==================== Statement round-trip tests ====================

    [Fact]
    [BatchParseTest("let x = 1;", "parse_statement", "let x = 1;")]
    public void ParseStatement_Let() => Batch.Assert();

    [Fact]
    [BatchParseTest("let x: i64 = 42;", "parse_statement", "let x: i64 = 42;")]
    public void ParseStatement_LetWithType() => Batch.Assert();

    [Fact]
    [BatchParseTest("let mut y = 1 + 2;", "parse_statement", "let mut y = 1 + 2;")]
    public void ParseStatement_LetMut() => Batch.Assert();

    [Fact]
    [BatchParseTest("f(1);", "parse_statement", "f(1);")]
    public void ParseStatement_Expression() => Batch.Assert();

    [Fact]
    [BatchParseTest("x = 42;", "parse_statement", "x = 42;")]
    public void ParseStatement_Assignment() => Batch.Assert();

    [Fact]
    [BatchParseTest("return 42;", "parse_returnStatement", "return 42;")]
    public void ParseStatement_ReturnWithValue() => Batch.Assert();

    [Fact]
    [BatchParseTest("return;", "parse_returnStatement", "return;")]
    public void ParseStatement_ReturnVoid() => Batch.Assert();

    [Fact]
    [BatchParseTest("break;", "parse_jumpStatement", "break;")]
    public void ParseStatement_Break() => Batch.Assert();

    [Fact]
    [BatchParseTest("continue;", "parse_jumpStatement", "continue;")]
    public void ParseStatement_Continue() => Batch.Assert();

    [Fact]
    [BatchParseTest("if (x) { 1 } else { 2 }", "parse_ifStatement", "if (x) { 1 }; else { 2 };")]
    public void ParseStatement_If() => Batch.Assert();

    [Fact]
    [BatchParseTest("while (x > 0) { x = x - 1; }", "parse_whileStatement", "while (x > 0) { x = x - 1; };")]
    public void ParseStatement_While() => Batch.Assert();

    [Fact]
    [BatchParseTest("for (let item in items) { f(item); }", "parse_forStatement", "for (let item in items) { f(item); };")]
    public void ParseStatement_For() => Batch.Assert();

    // ==================== Definition round-trip tests ====================

    [Fact]
    [BatchParseTest("initial { 42 }", "parse_initialRoutine", "initial { 42 }")]
    public void ParseDef_InitialRoutine() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun hello() { 1 }", "parse_functionDefinition", "fun hello() { 1 }")]
    public void ParseDef_FunctionDefinition() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun add(x: i64, y: i64) -> i64 { x }", "parse_functionDefinition", "fun add(x: i64, y: i64) -> i64 { x }")]
    public void ParseDef_FunctionWithParamsAndReturn() => Batch.Assert();

    [Fact]
    [BatchParseTest("fun hello(mut this) { 1 }", "parse_functionDefinition", "fun hello(mut this) { 1 }")]
    public void ParseDef_FunctionWithThisParam() => Batch.Assert();

    [Fact]
    [BatchParseTest("async fun compute() -> i64 { 42 }", "parse_functionDefinition", "async fun compute() -> i64 { 42 }")]
    public void ParseDef_AsyncFunction() => Batch.Assert();

    [Fact]
    [BatchParseTest("class Foo { x; }", "parse_classDefinition", "class Foo{ x; }")]
    public void ParseDef_ClassDefinition() => Batch.Assert();

    [Fact]
    [BatchParseTest("enum Color { Red; Green; Blue; }", "parse_enumDefinition", "enum Color{ Red; Green; Blue; }")]
    public void ParseDef_EnumDefinition() => Batch.Assert();

    [Fact]
    [BatchParseTest("namespace foo { initial { 42 } }", "parse_namespaceDefinition", "namespace foo{ initial { 42 } }")]
    public void ParseDef_NamespaceDefinition() => Batch.Assert();

    // ==================== Lambda/New/Async/Wait ====================

    [Fact]
    [BatchParseTest("fun() { 42 }", "parse_lambdaFunctionExpression", "fun() { 42 }")]
    public void ParseExpr_LambdaNoParams() => Batch.Assert();

    [Fact]
    [BatchParseTest("new Foo()", "parse_expression", "new Foo()")]
    public void ParseExpr_NewExpression() => Batch.Assert();

    [Fact]
    [BatchParseTest("async f(1)", "parse_expression", "async f(1)")]
    public void ParseExpr_AsyncExpression() => Batch.Assert();

    [Fact]
    [BatchParseTest("wait x", "parse_expression", "wait x")]
    public void ParseExpr_WaitExpression() => Batch.Assert();

    // ==================== Interface/Impl/Event/On ====================

    [Fact]
    [BatchParseTest("interface IPrintable { fun print(); }", "parse_interfaceDefinition", "interface IPrintable{ fun print(); }")]
    public void ParseDef_InterfaceDefinition() => Batch.Assert();

    [Fact]
    [BatchParseTest("interface IEvents { event Click; fun onClick(); }", "parse_interfaceDefinition", "interface IEvents{ event Click; fun onClick(); }")]
    public void ParseDef_InterfaceWithEvent() => Batch.Assert();

    [Fact]
    [BatchParseTest("impl Foo;", "parse_interfaceImplementation", "impl Foo;")]
    public void ParseDef_ImplDirect() => Batch.Assert();

    [Fact]
    [BatchParseTest("impl Foo for IBar { fun hello() { 1 } }", "parse_interfaceImplementation", "impl Foo for IBar { fun hello() { 1 } }")]
    public void ParseDef_ImplFor() => Batch.Assert();

    [Fact]
    [BatchParseTest("event Click;", "parse_eventDefinition", "event Click;")]
    public void ParseDef_EventDefinition() => Batch.Assert();

    [Fact]
    [BatchParseTest("event Click: i64;", "parse_eventDefinition", "event Click: i64;")]
    public void ParseDef_EventWithTyped() => Batch.Assert();

    [Fact]
    [BatchParseTest("on click { 42 }", "parse_onRoutine", "on click { 42 }")]
    public void ParseDef_OnRoutine() => Batch.Assert();

    [Fact]
    [BatchParseTest("on click(x: i64) { 42 }", "parse_onRoutine", "on click { 42 }")]
    public void ParseDef_OnRoutineWithParam() => Batch.Assert();

    [Fact]
    public void ParseStmt_EmitEvent()
        => Assert.Contains("emit", ParseWithMethod("emit obj.click(42);", "parse_emitEventStatement"));

    [Fact]
    public void ParseStmt_EmitNoArg()
        => Assert.Contains("emit", ParseWithMethod("emit obj.click();", "parse_emitEventStatement"));

    // ==================== Full program round-trip tests (contains-based) ====================

    [Fact]
    [BatchParseTest("namespace app { fun main() { 42 } }", "parse_compilationUnit", "namespace app{ fun main() { 42 } }")]
    public void ParseRoundTrip_FullProgram() => Batch.Assert();

    [Fact]
    [BatchParseTest("class Foo { fun hello() { 1 } }", "parse_compilationUnit", "class Foo{ fun hello() { 1 } }")]
    public void ParseRoundTrip_ClassWithImpl() => Batch.Assert();

    [Fact]
    [BatchParseTest("interface IFoo { fun bar(); } initial { 42 }", "parse_compilationUnit", "interface IFoo{ fun bar(); } initial { 42 }")]
    public void ParseRoundTrip_CompilationUnitWithInterface() => Batch.Assert();

    [Fact]
    [BatchParseTest("namespace test { pure async fun compute() -> i64 { yield 42; } }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_YieldStatement()
    {
        var result = Batch.GetResult();
        Assert.Contains("yield", result);
        Assert.Contains("pure async fun compute", result);
    }

    [Fact]
    [BatchParseTest("on event.click(e: Event) { __signal e; }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_SignalStatement() => Assert.Contains("__signal", Batch.GetResult());

    [Fact]
    [BatchParseTest("pure async fun compute() -> i64 { 42 }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_FunctionWithSpecifiers() => Assert.Contains("pure async fun compute()", Batch.GetResult());

    [Fact]
    [BatchParseTest("class Container { fun new(mut this) {} }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_TemplateClass() => Assert.Contains("class Container", Batch.GetResult());

    [Fact]
    [BatchParseTest("enum Result { Ok: i64; Err: string; }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_EnumWithTypedMembers()
    {
        var result = Batch.GetResult();
        Assert.Contains("enum Result", result);
        Assert.Contains("Ok", result);
        Assert.Contains("Err", result);
    }

    [Fact]
    public void ParseRoundTrip_AsyncLambda()
        => Assert.Contains("async_fun", ParseRoundTrip("initial { async_fun() { 42 } }"));

    [Fact]
    [BatchParseTest("impl Comparable for MyType where (T: Comparable) { fun compare() {} }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_ImplWithWhere() => Assert.Contains("impl Comparable for MyType", Batch.GetResult());

    [Fact]
    [BatchParseTest("on event.click { 42 }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_OnDottedEvent() => Assert.Contains("on event.click", Batch.GetResult());

    [Fact]
    [BatchParseTest("type MyInt = i64; initial { 42 }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_TypeReference() => Assert.Contains("type MyInt", Batch.GetResult());

    [Fact]
    [BatchParseTest("namespace app { class Foo { fun new(mut this) {} fun get() -> i64 { 42 } } }", "parse_compilationUnit", "")]
    public void ParseRoundTrip_FullProgramWithClass()
    {
        var result = Batch.GetResult();
        Assert.Contains("namespace app", result);
        Assert.Contains("class Foo", result);
        Assert.Contains("fun new", result);
        Assert.Contains("fun get", result);
    }

    [Fact]
    [BatchParseTest("namespace test { fun main() { let x: i64 = 42; } }", "parse_compilationUnit", "")]
    public void Gap_ParseNamespaceWithFunction()
    {
        var result = Batch.GetResult();
        Assert.Contains("namespace test", result);
        Assert.Contains("fun main", result);
    }

    [Fact]
    [BatchParseTest("impl IFoo for Bar { fun baz() {} }", "parse_compilationUnit", "")]
    public void Gap_ParseImplFor()
    {
        var result = Batch.GetResult();
        Assert.Contains("impl IFoo for Bar", result);
        Assert.Contains("fun baz", result);
    }

    [Fact]
    [BatchParseTest("type MyInt = mut i64; initial { 42 }", "parse_compilationUnit", "")]
    public void Gap_ParseTypeReference()
    {
        var result = Batch.GetResult();
        Assert.Contains("type MyInt", result);
        Assert.Contains("mut i64", result);
    }

    [Fact]
    [BatchParseTest("initial { for (let i in 0..10) { f(i); } }", "parse_compilationUnit", "")]
    public void Gap_ParseForStatement()
    {
        var result = Batch.GetResult();
        Assert.Contains("for", result);
        Assert.Contains("let i", result);
    }

    [Fact]
    [BatchParseTest("#template(T: type) class Container { value: auto T; }", "parse_compilationUnit", "")]
    public void Gap_ParseTemplateDeclaration()
    {
        var result = Batch.GetResult();
        Assert.Contains("#template", result);
        Assert.Contains("class Container", result);
    }

    [Fact]
    [BatchParseTest("interface IPrintable { fun print(); }", "parse_compilationUnit", "")]
    public void Gap_ParseInterfaceWithMembers()
    {
        var result = Batch.GetResult();
        Assert.Contains("interface IPrintable", result);
        Assert.Contains("fun print", result);
    }

    [Fact]
    [BatchParseTest("initial { let x: i64 = (1 + 2) * 3 - 4 / 2; }", "parse_compilationUnit", "")]
    public void Gap_ParseComplexExpression() => Assert.Contains("1 + 2", Batch.GetResult());

    // ==================== Full program parameter round-trip tests ====================

    [Fact]
    [BatchParseTest("namespace app { fun add(x: i64, y: i64) -> i64 { x } }", "parse_compilationUnit", "")]
    public void ParseFullProgram_FunctionWithTypedParams()
    {
        var result = Batch.GetResult();
        Assert.Contains("fun add", result);
        Assert.Contains("x: i64", result);
        Assert.Contains("y: i64", result);
    }

    [Fact]
    [BatchParseTest("class Foo { fun new(mut this) {} fun get() -> i64 { 42 } }", "parse_compilationUnit", "")]
    public void ParseFullProgram_FunctionWithThisParam()
    {
        var result = Batch.GetResult();
        Assert.Contains("fun new", result);
        Assert.Contains("mut this", result);
    }

    [Fact]
    public void ParseFullProgram_GenericTypeInLet()
        => Assert.Contains("List<i64>", ParseRoundTrip("initial { let x: List<i64> = new List<i64>(); }"));
}
