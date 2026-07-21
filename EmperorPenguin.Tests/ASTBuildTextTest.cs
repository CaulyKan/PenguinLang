using BabyPenguin;

namespace EmperorPenguin.Tests;

public class ASTBuildTextTest
{
    private static readonly BatchResults Batch = BatchCompiler.InitBatch<ASTBuildTextTest>();

    [Fact]
    [BatchTest(@"
initial {
    let cond = new emperor.ConstantExpression("""");
    cond.value = ""true"";
    let then_expr = new emperor.ConstantExpression("""");
    then_expr.value = ""1"";
    let else_expr = new emperor.ConstantExpression("""");
    else_expr.value = ""2"";
    let if_expr = new emperor.IfExpression();
    if_expr.condition = new Option<emperor.Expression>.some(new emperor.Expression.constant(cond));
    if_expr.then_block = new Option<emperor.Expression>.some(new emperor.Expression.constant(then_expr));
    if_expr.else_block = new Option<emperor.Expression>.some(new emperor.Expression.constant(else_expr));
    let result = new emperor.Expression.if_expr(if_expr);
    println(result.build_text());
}
", "if (true) 1 else 2")]
    public void AST_IfExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let result = new emperor.IdentifierExpression("""");
    result.name = ""x"";
    println(result.build_text());
}
", "x")]
    public void AST_Identifier_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let args0 = new emperor.TypeSpecifier();
    args0.name = ""i64"";
    let args1 = new emperor.TypeSpecifier();
    args1.name = ""i32"";
    let result = new emperor.IdentifierExpression("""");
    result.name = ""x"";
    result.generic_args.push(args0);
    result.generic_args.push(args1);
    println(result.build_text());
}
", "x<i64, i32>")]
    public void AST_IdentifierWithGeneric_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let cond = new emperor.IdentifierExpression("""");
    cond.name = ""x"";
    let body = new emperor.ConstantExpression("""");
    body.value = ""42"";
    let w = new emperor.WhileExpression();
    w.condition = new Option<emperor.Expression>.some(new emperor.Expression.identifier(cond));
    w.body = new Option<emperor.Expression>.some(new emperor.Expression.constant(body));
    let result = new emperor.Expression.while_expr(w);
    println(result.build_text());
}
", "while (x) 42")]
    public void AST_WhileExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let stmt_expr = new emperor.ConstantExpression("""");
    stmt_expr.value = ""1"";
    let es = new emperor.ExpressionStatement();
    es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(stmt_expr));
    let block = new emperor.CodeBlockExpression();
    block.statements.push(new emperor.Statement.expression(es));
    let trailing = new emperor.ConstantExpression("""");
    trailing.value = ""2"";
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(trailing));
    let result = new emperor.Expression.code_block(block);
    println(result.build_text());
}
", "{ 1; 2 }")]
    public void AST_CodeBlockExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let inner = new emperor.IdentifierExpression("""");
    inner.name = ""x"";
    let cast_expr = new emperor.CastExpression();
    cast_expr.type_name = ""i64"";
    cast_expr.inner = new Option<emperor.Expression>.some(new emperor.Expression.identifier(inner));
    let result = new emperor.Expression.cast_expr(cast_expr);
    println(result.build_text());
}
", "cast<i64>(x)")]
    public void AST_CastExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let arg1 = new emperor.ConstantExpression("""");
    arg1.value = ""1"";
    let arg2 = new emperor.ConstantExpression("""");
    arg2.value = ""2"";
    let new_expr = new emperor.NewExpression();
    new_expr.type_name = ""Foo"";
    new_expr.arguments.push(new emperor.Expression.constant(arg1));
    new_expr.arguments.push(new emperor.Expression.constant(arg2));
    let result = new emperor.Expression.new_expr(new_expr);
    println(result.build_text());
}
", "new Foo(1, 2)")]
    public void AST_NewExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let expr = new emperor.IdentifierExpression("""");
    expr.name = ""f(1)"";
    let stmt = new emperor.ExpressionStatement();
    stmt.expression = new Option<emperor.Expression>.some(new emperor.Expression.identifier(expr));
    let s = new emperor.Statement.expression(stmt);
    println(s.build_text());
}
", "f(1);")]
    public void AST_ExpressionStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let target = new emperor.IdentifierExpression("""");
    target.name = ""x"";
    let value = new emperor.ConstantExpression("""");
    value.value = ""42"";
    let stmt = new emperor.AssignmentStatement();
    stmt.target = new Option<emperor.Expression>.some(new emperor.Expression.identifier(target));
    stmt.operator_value = new emperor.AssignmentOperator.Assign();
    stmt.value = new Option<emperor.Expression>.some(new emperor.Expression.constant(value));
    let s = new emperor.Statement.assignment(stmt);
    println(s.build_text());
}
", "x = 42;")]
    public void AST_AssignmentStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let cond = new emperor.IdentifierExpression("""");
    cond.name = ""x"";
    let then_expr = new emperor.ConstantExpression("""");
    then_expr.value = ""1"";
    let es = new emperor.ExpressionStatement();
    es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(then_expr));
    let stmt = new emperor.IfStatement();
    stmt.condition = new Option<emperor.Expression>.some(new emperor.Expression.identifier(cond));
    stmt.then_statement = new Option<emperor.Statement>.some(new emperor.Statement.expression(es));
    let s = new emperor.Statement.if_stmt(stmt);
    println(s.build_text());
}
", "if (x) 1;")]
    public void AST_IfStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let val = new emperor.ConstantExpression("""");
    val.value = ""42"";
    let stmt = new emperor.ReturnStatement();
    stmt.value = new Option<emperor.Expression>.some(new emperor.Expression.constant(val));
    let s = new emperor.Statement.return_stmt(stmt);
    println(s.build_text());
}
", "return 42;")]
    public void AST_ReturnStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let init = new emperor.ConstantExpression("""");
    init.value = ""0"";
    let stmt = new emperor.LetDeclarationStatement();
    stmt.is_mutable = true;
    stmt.variable_name = ""x"";
    stmt.variable_type = ""i64"";
    stmt.initializer = new Option<emperor.Expression>.some(new emperor.Expression.constant(init));
    let s = new emperor.Statement.let_decl(stmt);
    println(s.build_text());
}
", "let mut x: i64 = 0;")]
    public void AST_LetDeclarationStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ret_type = new emperor.TypeSpecifier();
    ret_type.name = ""i64"";
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""i64"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""get_answer"";
    fd.parameters.push(param);
    fd.return_type = new Option<emperor.TypeSpecifier>.some(ret_type);
    fd.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "fun get_answer(x: i64) -> i64 { 42 }")]
    public void AST_FunctionDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""1"";
    let init_body = new emperor.CodeBlockExpression();
    init_body.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let init_def = new emperor.InitialRoutineDefinition();
    init_def.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(init_body));
    let ns = new emperor.NamespaceDefinition("""");
    ns.name = ""foo"";
    ns.children.push(new emperor.Definition.initial_routine(init_def));
    let d = new emperor.Definition.namespace_def(ns);
    println(d.build_text());
}
", "namespace foo{ initial { 1 } }")]
    public void AST_NamespaceDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""hello"";
    let cls = new emperor.ClassDefinition("""");
    cls.name = ""Foo"";
    cls.members.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.class_def(cls);
    println(d.build_text());
}
", "class Foo{ fun hello(); }")]
    public void AST_ClassDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let init_body = new emperor.ConstantExpression("""");
    init_body.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(init_body));
    let init_def = new emperor.InitialRoutineDefinition();
    init_def.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let unit = new emperor.CompilationUnit();
    unit.definitions.push(new emperor.Definition.initial_routine(init_def));
    println(unit.build_text());
}
", "initial { 42 }")]
    public void AST_CompilationUnit_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let stmt = new emperor.BreakStatement();
    let s = new emperor.Statement.break_stmt(stmt);
    println(s.build_text());
}
", "break;")]
    public void AST_BreakStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let val = new emperor.ConstantExpression("""");
    val.value = ""42"";
    let stmt = new emperor.BreakStatement();
    stmt.value = new Option<emperor.Expression>.some(new emperor.Expression.constant(val));
    let s = new emperor.Statement.break_stmt(stmt);
    println(s.build_text());
}
", "break 42;")]
    public void AST_BreakWithValue_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let stmt = new emperor.ContinueStatement();
    let s = new emperor.Statement.continue_stmt(stmt);
    println(s.build_text());
}
", "continue;")]
    public void AST_ContinueStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let iterable = new emperor.IdentifierExpression("""");
    iterable.name = ""items"";
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""0"";
    let body_es = new emperor.ExpressionStatement();
    body_es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let stmt = new emperor.ForStatement();
    stmt.is_mutable = true;
    stmt.variable_name = ""item"";
    stmt.variable_type = ""i64"";
    stmt.iterable = new Option<emperor.Expression>.some(new emperor.Expression.identifier(iterable));
    stmt.body = new Option<emperor.Statement>.some(new emperor.Statement.expression(body_es));
    let s = new emperor.Statement.for_stmt(stmt);
    println(s.build_text());
}
", "for (let mut item: i64 in items) 0;")]
    public void AST_ForStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let variant1 = new emperor.FunctionDefinition("""");
    variant1.name = ""Red"";
    let variant2 = new emperor.FunctionDefinition("""");
    variant2.name = ""Green"";
    let variant3 = new emperor.FunctionDefinition("""");
    variant3.name = ""Blue"";
    let ed = new emperor.EnumDefinition("""");
    ed.name = ""Color"";
    ed.members.push(new emperor.Definition.function_def(variant1));
    ed.members.push(new emperor.Definition.function_def(variant2));
    ed.members.push(new emperor.Definition.function_def(variant3));
    let d = new emperor.Definition.enum_def(ed);
    println(d.build_text());
}
", "enum Color{ fun Red(); fun Green(); fun Blue(); }")]
    public void AST_EnumDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let cond = new emperor.IdentifierExpression("""");
    cond.name = ""x"";
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""1"";
    let body_es = new emperor.ExpressionStatement();
    body_es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let stmt = new emperor.WhileStatement();
    stmt.condition = new Option<emperor.Expression>.some(new emperor.Expression.identifier(cond));
    stmt.body = new Option<emperor.Statement>.some(new emperor.Statement.expression(body_es));
    let s = new emperor.Statement.while_stmt(stmt);
    println(s.build_text());
}
", "while (x) 1;")]
    public void AST_WhileStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let cond = new emperor.IdentifierExpression("""");
    cond.name = ""x"";
    let then_val = new emperor.ConstantExpression("""");
    then_val.value = ""1"";
    let then_es = new emperor.ExpressionStatement();
    then_es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(then_val));
    let else_val = new emperor.ConstantExpression("""");
    else_val.value = ""2"";
    let else_es = new emperor.ExpressionStatement();
    else_es.expression = new Option<emperor.Expression>.some(new emperor.Expression.constant(else_val));
    let stmt = new emperor.IfStatement();
    stmt.condition = new Option<emperor.Expression>.some(new emperor.Expression.identifier(cond));
    stmt.then_statement = new Option<emperor.Statement>.some(new emperor.Statement.expression(then_es));
    stmt.else_statement = new Option<emperor.Statement>.some(new emperor.Statement.expression(else_es));
    let s = new emperor.Statement.if_stmt(stmt);
    println(s.build_text());
}
", "if (x) 1; else 2;")]
    public void AST_IfStatementWithElse_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let stmt = new emperor.LetDeclarationStatement();
    stmt.is_mutable = false;
    stmt.variable_name = ""x"";
    stmt.variable_type = ""i64"";
    let s = new emperor.Statement.let_decl(stmt);
    println(s.build_text());
}
", "let x: i64;")]
    public void AST_LetDeclarationWithoutInitializer_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let target = new emperor.IdentifierExpression("""");
    target.name = ""x"";
    let value = new emperor.ConstantExpression("""");
    value.value = ""1"";
    let stmt = new emperor.AssignmentStatement();
    stmt.target = new Option<emperor.Expression>.some(new emperor.Expression.identifier(target));
    stmt.operator_value = new emperor.AssignmentOperator.PlusAssign();
    stmt.value = new Option<emperor.Expression>.some(new emperor.Expression.constant(value));
    let s = new emperor.Statement.assignment(stmt);
    println(s.build_text());
}
", "x += 1;")]
    public void AST_AssignmentWithOperator_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let default_val = new emperor.ConstantExpression("""");
    default_val.value = ""0"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""i64"";
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    param.default_value = new Option<emperor.Expression>.some(new emperor.Expression.constant(default_val));
    println(param.build_text());
}
", "x: i64 = 0")]
    public void AST_ParameterWithDefault_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let inner = new emperor.TypeSpecifier();
    inner.name = ""i64"";
    let ts = new emperor.TypeSpecifier();
    ts.name = ""List"";
    ts.generic_args.push(inner);
    println(ts.build_text());
}
", "List<i64>")]
    public void AST_TypeSpecifierWithGeneric_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let lambda = new emperor.LambdaFunctionExpression();
    lambda.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let result = new emperor.Expression.lambda_expr(lambda);
    println(result.build_text());
}
", "fun() { 42 }")]
    public void AST_LambdaFunctionExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let inner = new emperor.IdentifierExpression("""");
    inner.name = ""f(1)"";
    let spawn = new emperor.SpawnAsyncExpression();
    spawn.expression = new Option<emperor.Expression>.some(new emperor.Expression.identifier(inner));
    let result = new emperor.Expression.spawn_async(spawn);
    println(result.build_text());
}
", "async f(1)")]
    public void AST_SpawnAsyncExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let inner = new emperor.IdentifierExpression("""");
    inner.name = ""x"";
    let wait_node = new emperor.WaitExpression();
    wait_node.expression = new Option<emperor.Expression>.some(new emperor.Expression.identifier(inner));
    let result = new emperor.Expression.wait_expr(wait_node);
    println(result.build_text());
}
", "wait x")]
    public void AST_WaitExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""print"";
    let iface = new emperor.InterfaceDefinition("""");
    iface.name = ""IPrintable"";
    iface.members.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.interface_def(iface);
    println(d.build_text());
}
", "interface IPrintable{ fun print(); }")]
    public void AST_InterfaceDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""hello"";
    let impl_node = new emperor.InterfaceImplementation("""");
    impl_node.type_name = ""Foo"";
    impl_node.functions.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.impl_def(impl_node);
    println(d.build_text());
}
", "impl Foo { fun hello(); }")]
    public void AST_InterfaceImplementation_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let impl_node = new emperor.InterfaceForImplementation("""", """");
    impl_node.type_name = ""Foo"";
    impl_node.for_type_name = ""IBar"";
    let d = new emperor.Definition.impl_for_def(impl_node);
    println(d.build_text());
}
", "impl Foo for IBar;")]
    public void AST_InterfaceForImplementation_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ev = new emperor.EventDefinition("""");
    ev.name = ""Click"";
    let d = new emperor.Definition.event_def(ev);
    println(d.build_text());
}
", "event Click;")]
    public void AST_EventDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let on_node = new emperor.OnRoutineDefinition();
    on_node.event_name = ""click"";
    on_node.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let d = new emperor.Definition.on_routine_def(on_node);
    println(d.build_text());
}
", "on click { 42 }")]
    public void AST_OnRoutineDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let event_expr = new emperor.IdentifierExpression("""");
    event_expr.name = ""click"";
    let arg = new emperor.ConstantExpression("""");
    arg.value = ""42"";
    let emit_node = new emperor.EmitEventStatement();
    emit_node.event_expr = new Option<emperor.Expression>.some(new emperor.Expression.identifier(event_expr));
    emit_node.argument = new Option<emperor.Expression>.some(new emperor.Expression.constant(arg));
    let s = new emperor.Statement.emit_event(emit_node);
    println(s.build_text());
}
", "emit click(42);")]
    public void AST_EmitEventStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let void_node = new emperor.VoidLiteralExpression();
    void_node.value = ""void"";
    let result = new emperor.Expression.void_literal(void_node);
    println(result.build_text());
}
", "void")]
    public void AST_VoidLiteralExpression_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let val = new emperor.ConstantExpression("""");
    val.value = ""42"";
    let yield_node = new emperor.YieldStatement();
    yield_node.value = new Option<emperor.Expression>.some(new emperor.Expression.constant(val));
    let s = new emperor.Statement.yield_stmt(yield_node);
    println(s.build_text());
}
", "yield 42;")]
    public void AST_YieldStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let yield_node = new emperor.YieldStatement();
    let s = new emperor.Statement.yield_stmt(yield_node);
    println(s.build_text());
}
", "yield;")]
    public void AST_YieldStatementWithoutValue_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let expr = new emperor.IdentifierExpression("""");
    expr.name = ""e"";
    let signal_node = new emperor.SignalStatement();
    signal_node.expression = new Option<emperor.Expression>.some(new emperor.Expression.identifier(expr));
    let s = new emperor.Statement.signal_stmt(signal_node);
    println(s.build_text());
}
", "__signal e")]
    public void AST_SignalStatement_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""i64"";
    let type_ref = new emperor.TypeReferenceDefinition("""");
    type_ref.name = ""MyInt"";
    type_ref.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    let d = new emperor.Definition.type_ref_def(type_ref);
    println(d.build_text());
}
", "type MyInt = i64;")]
    public void AST_TypeReferenceDefinition_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp1 = new emperor.TemplateParameter();
    tp1.name = ""T"";
    tp1.type_constraint = ""type"";
    let tp2 = new emperor.TemplateParameter();
    tp2.name = ""N"";
    tp2.type_constraint = ""i64"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp1);
    decl.parameters.push(tp2);
    println(decl.build_text());
}
", "#template(T: type, N: i64)")]
    public void AST_TemplateParameterAndDeclaration_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compute"";
    fd.is_pure = true;
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "pure fun compute();")]
    public void AST_FunctionDefinition_WithPure_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compute"";
    fd.is_not_pure = true;
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "!pure fun compute();")]
    public void AST_FunctionDefinition_WithNotPure_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compute"";
    fd.is_async = true;
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "async fun compute();")]
    public void AST_FunctionDefinition_WithAsync_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compute"";
    fd.is_not_async = true;
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "!async fun compute();")]
    public void AST_FunctionDefinition_WithNotAsync_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""external_func"";
    fd.is_extern = true;
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "extern fun external_func();")]
    public void AST_FunctionDefinition_WithExtern_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    tp.type_constraint = ""type"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""identity"";
    fd.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    let ret_type = new emperor.TypeSpecifier();
    ret_type.name = ""T"";
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""T"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    fd.parameters.push(param);
    fd.return_type = new Option<emperor.TypeSpecifier>.some(ret_type);
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "#template(T: type) fun identity(x: T) -> T;")]
    public void AST_FunctionDefinition_WithTemplateDecl_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""new"";
    fd.is_new = true;
    let param = new emperor.Parameter("""");
    param.name = ""this"";
    param.is_this = true;
    param.is_mutable = true;
    fd.parameters.push(param);
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    fd.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "fun new(mut this) { 42 }")]
    public void AST_FunctionDefinition_WithIsNew_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compute"";
    fd.is_pure = true;
    fd.is_async = true;
    fd.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    let d = new emperor.Definition.function_def(fd);
    println(d.build_text());
}
", "#template(T) pure async fun compute();")]
    public void AST_FunctionDefinition_PureAsyncWithTemplate_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""i64"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    let body_id = new emperor.IdentifierExpression("""");
    body_id.name = ""x"";
    let lambda = new emperor.LambdaFunctionExpression();
    lambda.is_async = true;
    lambda.parameters.push(param);
    lambda.body = new Option<emperor.Expression>.some(new emperor.Expression.identifier(body_id));
    let result = new emperor.Expression.lambda_expr(lambda);
    println(result.build_text());
}
", "async_fun(x: i64) x")]
    public void AST_LambdaFunctionExpression_WithAsync_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    let body_id = new emperor.IdentifierExpression("""");
    body_id.name = ""x"";
    let lambda = new emperor.LambdaFunctionExpression();
    lambda.is_async = false;
    lambda.parameters.push(param);
    lambda.body = new Option<emperor.Expression>.some(new emperor.Expression.identifier(body_id));
    let result = new emperor.Expression.lambda_expr(lambda);
    println(result.build_text());
}
", "fun(x) x")]
    public void AST_LambdaFunctionExpression_Sync_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ts = new emperor.TypeSpecifier();
    ts.name = ""i64"";
    ts.is_mutable = true;
    println(ts.build_text());
}
", "mut i64")]
    public void AST_TypeSpecifier_WithMutability_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ts = new emperor.TypeSpecifier();
    ts.name = ""i64"";
    ts.is_not_mutable = true;
    println(ts.build_text());
}
", "!mut i64")]
    public void AST_TypeSpecifier_WithNotMutable_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ts = new emperor.TypeSpecifier();
    ts.name = ""i64"";
    ts.is_auto_mutability = true;
    println(ts.build_text());
}
", "auto i64")]
    public void AST_TypeSpecifier_WithAutoMutability_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param1 = new emperor.TypeSpecifier();
    param1.name = ""i64"";
    let param2 = new emperor.TypeSpecifier();
    param2.name = ""string"";
    let ret_type = new emperor.TypeSpecifier();
    ret_type.name = ""bool"";
    let ts = new emperor.TypeSpecifier();
    ts.is_function_type = true;
    ts.function_params.push(param1);
    ts.function_params.push(param2);
    ts.return_type = new Option<emperor.TypeSpecifier>.some(ret_type);
    println(ts.build_text());
}
", "fun<i64, string, bool>")]
    public void AST_TypeSpecifier_FunctionType_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param = new emperor.TypeSpecifier();
    param.name = ""i64"";
    let ret_type = new emperor.TypeSpecifier();
    ret_type.name = ""string"";
    let ts = new emperor.TypeSpecifier();
    ts.is_async_function_type = true;
    ts.function_params.push(param);
    ts.return_type = new Option<emperor.TypeSpecifier>.some(ret_type);
    println(ts.build_text());
}
", "async_fun<i64, string>")]
    public void AST_TypeSpecifier_AsyncFunctionType_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ts = new emperor.TypeSpecifier();
    ts.qualified_parts.push(""std"");
    ts.qualified_parts.push(""collections"");
    ts.qualified_parts.push(""List"");
    println(ts.build_text());
}
", "std.collections.List")]
    public void AST_TypeSpecifier_QualifiedName_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let arg1 = new emperor.TypeSpecifier();
    arg1.name = ""i64"";
    let arg2 = new emperor.TypeSpecifier();
    arg2.name = ""string"";
    let ts = new emperor.TypeSpecifier();
    ts.name = ""Map"";
    ts.generic_args.push(arg1);
    ts.generic_args.push(arg2);
    println(ts.build_text());
}
", "Map<i64, string>")]
    public void AST_TypeSpecifier_GenericArgs_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let ts = new emperor.TypeSpecifier();
    ts.name = ""i64"";
    ts.is_iterable = true;
    println(ts.build_text());
}
", "i64[]")]
    public void AST_TypeSpecifier_Iterable_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let arg = new emperor.TypeSpecifier();
    arg.name = ""i64"";
    let ts = new emperor.TypeSpecifier();
    ts.name = ""List"";
    ts.is_mutable = true;
    ts.generic_args.push(arg);
    ts.is_iterable = true;
    println(ts.build_text());
}
", "mut List<i64>[]")]
    public void AST_TypeSpecifier_MutableGenericIterable_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let cls = new emperor.ClassDefinition("""");
    cls.name = ""Container"";
    cls.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    let d = new emperor.Definition.class_def(cls);
    println(d.build_text());
}
", "#template(T) class Container{ }")]
    public void AST_ClassDefinition_WithTemplate_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let field = new emperor.ClassFieldDefinition("""");
    field.name = ""value"";
    let field_type = new emperor.TypeSpecifier();
    field_type.name = ""T"";
    field.type_spec = new Option<emperor.TypeSpecifier>.some(field_type);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""get_value"";
    let cls = new emperor.ClassDefinition("""");
    cls.name = ""Container"";
    cls.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    cls.members.push(new emperor.Definition.class_field(field));
    cls.members.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.class_def(cls);
    println(d.build_text());
}
", "#template(T) class Container{ value: T; fun get_value(); }")]
    public void AST_ClassDefinition_WithTemplateAndMembers_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let member = new emperor.EnumMemberDefinition("""");
    member.name = ""Some"";
    let member_type = new emperor.TypeSpecifier();
    member_type.name = ""T"";
    member.type_spec = new Option<emperor.TypeSpecifier>.some(member_type);
    let ed = new emperor.EnumDefinition("""");
    ed.name = ""Option"";
    ed.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    ed.members.push(new emperor.Definition.enum_member(member));
    let none_member = new emperor.EnumMemberDefinition("""");
    none_member.name = ""None"";
    ed.members.push(new emperor.Definition.enum_member(none_member));
    let d = new emperor.Definition.enum_def(ed);
    println(d.build_text());
}
", "#template(T) enum Option{ Some: T; None; }")]
    public void AST_EnumDefinition_WithTemplate_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let tp = new emperor.TemplateParameter();
    tp.name = ""T"";
    let decl = new emperor.TemplateDeclaration();
    decl.parameters.push(tp);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compare"";
    let param = new emperor.Parameter("""");
    param.name = ""other"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""T"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    fd.parameters.push(param);
    let ret_type = new emperor.TypeSpecifier();
    ret_type.name = ""i64"";
    fd.return_type = new Option<emperor.TypeSpecifier>.some(ret_type);
    let iface = new emperor.InterfaceDefinition("""");
    iface.name = ""Comparable"";
    iface.template_decl = new Option<emperor.TemplateDeclaration>.some(decl);
    iface.members.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.interface_def(iface);
    println(d.build_text());
}
", "#template(T) interface Comparable{ fun compare(other: T) -> i64; }")]
    public void AST_InterfaceDefinition_WithTemplate_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let target = new emperor.IdentifierExpression("""");
    target.name = ""event"";
    let member = new emperor.MemberAccessExpression();
    member.base_expr = new Option<emperor.Expression>.some(new emperor.Expression.identifier(target));
    member.member_name = ""click"";
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""42"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let on_node = new emperor.OnRoutineDefinition();
    on_node.event_expr = new Option<emperor.Expression>.some(new emperor.Expression.member_access(member));
    on_node.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let d = new emperor.Definition.on_routine_def(on_node);
    println(d.build_text());
}
", "on event.click { 42 }")]
    public void AST_OnRoutineDefinition_WithEventExpr_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let target = new emperor.IdentifierExpression("""");
    target.name = ""event"";
    let member = new emperor.MemberAccessExpression();
    member.base_expr = new Option<emperor.Expression>.some(new emperor.Expression.identifier(target));
    member.member_name = ""click"";
    let param = new emperor.Parameter("""");
    param.name = ""e"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""Event"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    let body_expr = new emperor.ConstantExpression("""");
    body_expr.value = ""0"";
    let block = new emperor.CodeBlockExpression();
    block.trailing_expr = new Option<emperor.Expression>.some(new emperor.Expression.constant(body_expr));
    let on_node = new emperor.OnRoutineDefinition();
    on_node.event_expr = new Option<emperor.Expression>.some(new emperor.Expression.member_access(member));
    on_node.parameter = new Option<emperor.Parameter>.some(param);
    on_node.body = new Option<emperor.Expression>.some(new emperor.Expression.code_block(block));
    let d = new emperor.Definition.on_routine_def(on_node);
    println(d.build_text());
}
", "on event.click(e: Event) { 0 }")]
    public void AST_OnRoutineDefinition_WithEventExprAndParam_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""string"";
    let member = new emperor.EnumMemberDefinition("""");
    member.name = ""Message"";
    member.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    let d = new emperor.Definition.enum_member(member);
    println(d.build_text());
}
", "Message: string;")]
    public void AST_EnumMemberDefinition_WithType_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let member = new emperor.EnumMemberDefinition("""");
    member.name = ""None"";
    let d = new emperor.Definition.enum_member(member);
    println(d.build_text());
}
", "None;")]
    public void AST_EnumMemberDefinition_WithoutType_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""i64"";
    let init_val = new emperor.ConstantExpression("""");
    init_val.value = ""0"";
    let field = new emperor.ClassFieldDefinition("""");
    field.name = ""count"";
    field.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    field.initializer = new Option<emperor.Expression>.some(new emperor.Expression.constant(init_val));
    let d = new emperor.Definition.class_field(field);
    println(d.build_text());
}
", "count: i64 = 0;")]
    public void AST_ClassFieldDefinition_WithTypeAndInitializer_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""string"";
    let field = new emperor.ClassFieldDefinition("""");
    field.name = ""name"";
    field.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    let d = new emperor.Definition.class_field(field);
    println(d.build_text());
}
", "name: string;")]
    public void AST_ClassFieldDefinition_WithTypeOnly_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""bool"";
    type_spec.is_mutable = true;
    let init_val = new emperor.BoolLiteralExpression();
    init_val.value = ""true"";
    let field = new emperor.ClassFieldDefinition("""");
    field.name = ""flag"";
    field.mutability = ""mut"";
    field.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    field.initializer = new Option<emperor.Expression>.some(new emperor.Expression.bool_literal(init_val));
    let d = new emperor.Definition.class_field(field);
    println(d.build_text());
}
", "flag: mut bool = true;")]
    public void AST_ClassFieldDefinition_MutableWithInitializer_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let type_spec = new emperor.TypeSpecifier();
    type_spec.name = ""List"";
    let arg = new emperor.TypeSpecifier();
    arg.name = ""i64"";
    type_spec.generic_args.push(arg);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""get"";
    let impl_node = new emperor.InterfaceImplementation("""");
    impl_node.type_spec = new Option<emperor.TypeSpecifier>.some(type_spec);
    impl_node.functions.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.impl_def(impl_node);
    println(d.build_text());
}
", "impl List<i64> { fun get(); }")]
    public void AST_InterfaceImplementation_WithTypeSpec_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let iface_spec = new emperor.TypeSpecifier();
    iface_spec.name = ""Comparable"";
    let for_spec = new emperor.TypeSpecifier();
    for_spec.name = ""List"";
    let arg = new emperor.TypeSpecifier();
    arg.name = ""i64"";
    for_spec.generic_args.push(arg);
    let fd = new emperor.FunctionDefinition("""");
    fd.name = ""compare"";
    let impl_node = new emperor.InterfaceForImplementation("""", """");
    impl_node.type_spec = new Option<emperor.TypeSpecifier>.some(iface_spec);
    impl_node.for_type_spec = new Option<emperor.TypeSpecifier>.some(for_spec);
    impl_node.functions.push(new emperor.Definition.function_def(fd));
    let d = new emperor.Definition.impl_for_def(impl_node);
    println(d.build_text());
}
", "impl Comparable for List<i64> { fun compare(); }")]
    public void AST_InterfaceForImplementation_WithTypeSpecs_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param = new emperor.Parameter("""");
    param.is_this = true;
    param.is_mutable = false;
    println(param.build_text());
}
", "this")]
    public void AST_Parameter_WithThis_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let param = new emperor.Parameter("""");
    param.is_this = true;
    param.is_mutable = true;
    println(param.build_text());
}
", "mut this")]
    public void AST_Parameter_WithMutableThis_BuildText() => Batch.Assert();

    [Fact]
    [BatchTest(@"
initial {
    let default_val = new emperor.ConstantExpression("""");
    default_val.value = ""42"";
    let param = new emperor.Parameter("""");
    param.name = ""x"";
    let param_type = new emperor.TypeSpecifier();
    param_type.name = ""i64"";
    param.type_spec = new Option<emperor.TypeSpecifier>.some(param_type);
    param.default_value = new Option<emperor.Expression>.some(new emperor.Expression.constant(default_val));
    println(param.build_text());
}
", "x: i64 = 42")]
    public void AST_Parameter_WithDefaultValue_BuildText() => Batch.Assert();
}
