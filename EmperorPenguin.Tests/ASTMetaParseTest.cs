using BabyPenguin;

namespace EmperorPenguin.Tests;

public class ASTMetaParseTest
{
    private static readonly BatchResults Batch = BatchCompiler.InitParseBatch<ASTMetaParseTest>();

    private string ParseWithMethod(string source, string parseMethod)
    {
        var escaped = source.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var userCode = @$"
initial {{
    let source: string = ""{escaped}"";
    let lexer = new emperor.Lexer(source, """");
    let tokens = lexer.tokenize();
    let p = new emperor.Parser(tokens);
    let result = p.{parseMethod}();
    println(result.build_text());
}}";
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddFile(Path.Combine(BatchCompiler.AstDir, "SourceLocation.penguin"));
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

    // ==================== Meta function definition ====================

    [Fact]
    [BatchParseTest("#fun fib(n: u32) -> u32 { 0 }", "parse_metaFunctionDefinition", "#fun fib(n: u32) -> u32 { 0 }")]
    public void ParseMetaDef_Function() => Batch.Assert();

    [Fact]
    [BatchParseTest("#fun foo() -> void;", "parse_metaFunctionDefinition", "#fun foo() -> void;")]
    public void ParseMetaDef_FunctionNoBody() => Batch.Assert();

    // ==================== Meta call expression ====================

    [Fact]
    [BatchParseTest("#derive_clone(T)", "parse_expression", "#derive_clone(T)")]
    public void ParseMetaExpr_Call() => Batch.Assert();

    [Fact]
    [BatchParseTest("#compiler()", "parse_expression", "#compiler()")]
    public void ParseMetaExpr_Compiler() => Batch.Assert();

    [Fact]
    [BatchParseTest("#foo(a, b)", "parse_expression", "#foo(a, b)")]
    public void ParseMetaExpr_CallWithArgs() => Batch.Assert();

    // ==================== Meta if/for/while at declaration level ====================

    [Fact]
    [BatchParseTest("#if (true) { }", "parse_metaIfDefinition", "#if (true) { }")]
    public void ParseMetaDef_If() => Batch.Assert();

    [Fact]
    [BatchParseTest("#if (true) { } #else { let x: i64 = 0; }", "parse_metaIfDefinition", "#if (true) { } #else { let x: i64 = 0; }")]
    public void ParseMetaDef_IfElse() => Batch.Assert();

    [Fact]
    [BatchParseTest("#for (let i: u32 in 0..10) { }", "parse_metaForDefinition", "#for (let i: u32 in 0..10) { }")]
    public void ParseMetaDef_For() => Batch.Assert();

    [Fact]
    [BatchParseTest("#while (true) { }", "parse_metaWhileDefinition", "#while (true) { }")]
    public void ParseMetaDef_While() => Batch.Assert();

    // ==================== Meta if/for/while at statement level ====================

    [Fact]
    [BatchParseTest("#if (x) 1;", "parse_metaIfStatement", "#if (x) 1;")]
    public void ParseMetaStmt_If() => Batch.Assert();

    [Fact]
    [BatchParseTest("#for (let i in items) f(i);", "parse_metaForStatement", "#for (let i in items) f(i);")]
    public void ParseMetaStmt_For() => Batch.Assert();

    [Fact]
    [BatchParseTest("#while (x) 1;", "parse_metaWhileStatement", "#while (x) 1;")]
    public void ParseMetaStmt_While() => Batch.Assert();

    // ==================== Meta break/continue ====================

    [Fact]
    [BatchParseTest("#break;", "parse_metaBreakStatement", "#break;")]
    public void ParseMetaStmt_Break() => Batch.Assert();

    [Fact]
    [BatchParseTest("#break 42;", "parse_metaBreakStatement", "#break 42;")]
    public void ParseMetaStmt_BreakWithValue() => Batch.Assert();

    [Fact]
    [BatchParseTest("#continue;", "parse_metaContinueStatement", "#continue;")]
    public void ParseMetaStmt_Continue() => Batch.Assert();

    // ==================== Top-level meta call ====================

    [Fact]
    [BatchParseTest("#derive_clone(T);", "parse_metaCallDefinition", "#derive_clone(T);")]
    public void ParseMetaDef_CallDecl() => Batch.Assert();

    // ==================== Meta in statement context ====================

    [Fact]
    [BatchParseTest("fun f() -> void { let x = #fib(10); }", "parse_functionDefinition", "fun f() -> void { let x = #fib(10); }")]
    public void ParseMeta_MetaCallInLetInitializer() => Batch.Assert();

    // ==================== Top-level meta declarations via parse_namespaceDeclaration ====================

    [Fact]
    [BatchParseTest("#fun foo() { 1 }", "parse_namespaceDeclaration", "#fun foo() { 1 }")]
    public void ParseMetaDef_NamespaceDeclFun() => Batch.Assert();

    [Fact]
    [BatchParseTest("#derive_clone(T);", "parse_namespaceDeclaration", "#derive_clone(T);")]
    public void ParseMetaDef_NamespaceDeclCall() => Batch.Assert();

    // ==================== #elif test (parsing only, build_text uses #elif) ====================

    [Fact]
    [BatchParseTest("#if (true) { } #elif (false) { let y: string = \"\"; } #else { let x: i64 = 0; }", "parse_metaIfDefinition", "#if (true) { } #elif (false) { let y: string = \"\"; } #else { let x: i64 = 0; }")]
    public void ParseMetaDef_IfElifElse() => Batch.Assert();
}
