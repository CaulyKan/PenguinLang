using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace BabyPenguin.Parser
{
    /// <summary>
    /// File-scoped compile-time conditional-compilation splicer (batch 2.2).
    ///
    /// Runs on the ANTLR parse tree (after <see cref="PenguinParser.Parse"/>,
    /// before <see cref="SyntaxCompiler"/> builds the syntax-node tree) and
    /// resolves the meta directives in source order:
    ///
    ///   #define("K","V");                 records K in the per-file define store
    ///   #if (c) { defs } #elif (c) { defs } #else { defs }   (definition level)
    ///   #if (c) stmt #elif (c) stmt #else stmt               (statement level)
    ///   #while (c) { defs } / #while (c) stmt                (both levels)
    ///
    /// Semantics (aligned with EmperorPenguin's SemanticMetaRewrite, with the
    /// redesign's FILE-SCOPED option store):
    ///   - a code #define takes effect from its line to the END OF THE FILE
    ///     and never leaks across files; redefining the same key within one
    ///     file is E_DUPLICATE_SYMBOL;
    ///   - CLI defines (CLI --define K=V) are pre-seeded into every file's
    ///     store; a code #define OVERWRITES the CLI value without error;
    ///   - conditions fold #defined("K"), bare identifier NAME (its define
    ///     value), string/bool literals, !, &amp;&amp;, ||, ==/!= and
    ///     parentheses; anything else is E_UNSUPPORTED (#option("K") reads
    ///     were retired with the global option table in batch 2.3);
    ///   - a statement-level #define inside one function affects later
    ///     #if conditions in the SAME file (single ordered walk).
    ///
    /// Splicing happens at the parse-context level on purpose: the syntax
    /// tree groups definitions by kind (NamespaceDefinition.Functions /
    /// Classes / ...), so the only way to splice an #if branch containing
    /// mixed definition kinds in order is to expand the parse contexts in
    /// place — downstream builders then see exactly the tree the unrolled
    /// source would have produced, with original source locations intact.
    /// </summary>
    public static class MetaSplicer
    {
        private enum ContainerKind
        {
            /// <summary>compilation unit / namespace body (branch bodies stay wrapped in namespaceDeclaration)</summary>
            Namespace,
            Class,
            Enum,
            Interface,
            Impl,
        }

        private const int MaxWhileIterations = 10000;

        public static void Rewrite(CompilationUnitContext unit, string fileName, ErrorReporter reporter, IReadOnlyDictionary<string, string> cliDefines)
        {
            var state = new SplicerState(fileName, reporter, cliDefines);
            state.RewriteMetaList(unit, ContainerKind.Namespace);
        }

        private sealed class SplicerState(string fileName, ErrorReporter reporter, IReadOnlyDictionary<string, string> cliDefines)
        {
            public string FileName { get; } = fileName;
            public ErrorReporter Reporter { get; } = reporter;

            /// <summary>Per-file define store: CLI pre-seed layer + code #defines.</summary>
            public Dictionary<string, string> Store { get; } = new(cliDefines, StringComparer.Ordinal);

            /// <summary>Keys defined by CODE in this file (redefinition detection).</summary>
            public HashSet<string> CodeDefined { get; } = new(StringComparer.Ordinal);

            // ───────────────────────── definition level ─────────────────────────

            /// <summary>
            /// Walk a definition-list container (compilation unit, namespace,
            /// class/enum/interface/impl body) resolving meta declarations in
            /// place, recursing into every kept definition.
            /// </summary>
            public void RewriteMetaList(ParserRuleContext container, ContainerKind kind)
            {
                var children = container.children;
                if (children == null)
                    return;
                for (int i = 0; i < children.Count;)
                {
                    switch (children[i])
                    {
                        case MetaDeclarationContext meta:
                        {
                            // Bare member form (class/enum/interface/impl bodies reference
                            // metaDeclaration directly).
                            var lifted = LiftAll(ExpandMetaDeclaration(meta, kind), kind);
                            children.RemoveAt(i);
                            InsertAll(children, i, lifted);
                            // Do not advance: the first lifted item (if any) still needs
                            // the regular recursion below (it is never another bare
                            // MetaDeclarationContext, so no infinite loop).
                            continue;
                        }
                        case NamespaceDeclarationContext decl when kind == ContainerKind.Namespace:
                        {
                            // Wrapped form: namespaceDeclaration wraps either a real
                            // definition or a metaDeclaration (top-level / namespace body).
                            if (InnerDefs(decl).FirstOrDefault() is MetaDeclarationContext wrapped)
                            {
                                var picked = ExpandMetaDeclaration(wrapped, kind);
                                children.RemoveAt(i);
                                InsertAll(children, i, picked);
                                continue;
                            }
                            foreach (var def in InnerDefs(decl))
                                RewriteDef(def);
                            break;
                        }
                        case ParserRuleContext rule:
                            RewriteDef(rule);
                            break;
                    }
                    i++;
                }
            }

            /// <summary>Insert items at index (RuleContext.children is only an IList).</summary>
            private static void InsertAll(IList<IParseTree> children, int index, IReadOnlyList<IParseTree> items)
            {
                foreach (var item in items)
                    children.Insert(index++, item);
            }

            /// <summary>Non-terminal children of a namespaceDeclaration (skips 'export' / ';' terminals).</summary>
            private static IEnumerable<ParserRuleContext> InnerDefs(NamespaceDeclarationContext decl)
                => decl.children?.OfType<ParserRuleContext>() ?? [];

            /// <summary>Recurse into one (non-meta) definition context.</summary>
            private void RewriteDef(ParserRuleContext def)
            {
                switch (def)
                {
                    case NamespaceDefinitionContext ns:
                        RewriteMetaList(ns, ContainerKind.Namespace);
                        break;
                    case ClassDefinitionContext cls:
                        RewriteMetaList(cls, ContainerKind.Class);
                        break;
                    case EnumDefinitionContext enm:
                        RewriteMetaList(enm, ContainerKind.Enum);
                        break;
                    case InterfaceDefinitionContext iface:
                        RewriteMetaList(iface, ContainerKind.Interface);
                        break;
                    case InterfaceForImplementationContext implFor:
                        RewriteMetaList(implFor, ContainerKind.Impl);
                        break;
                    case InterfaceImplementationContext impl:
                        RewriteMetaList(impl, ContainerKind.Impl);
                        break;
                    case FunctionDefinitionContext fun:
                        if (fun.codeBlockExpression() is { } funBody)
                            RewriteCodeBlock(funBody);
                        break;
                    case InitialRoutineContext initial:
                        if (initial.codeBlockExpression() is { } initialBody)
                            RewriteCodeBlock(initialBody);
                        break;
                    case ConstructBlockContext construct:
                        if (construct.codeBlockExpression() is { } constructBody)
                            RewriteCodeBlock(constructBody);
                        break;
                }
            }

            /// <summary>
            /// Resolve one definition-level meta declaration to the picked
            /// branch's namespaceDeclaration list (empty for a consumed
            /// #define; a thrown BabyPenguinException for malformed input).
            /// </summary>
            private List<NamespaceDeclarationContext> ExpandMetaDeclaration(MetaDeclarationContext meta, ContainerKind kind)
            {
                if (meta.metaCallDeclaration() is { } call)
                {
                    ConsumeMetaDefine(
                        call.identifier()?.GetText(),
                        call.expression().ToList(),
                        SourceLocation.From(FileName, call));
                    return [];
                }

                if (meta.metaIfDeclaration() is { } metaIf)
                {
                    var location = SourceLocation.From(FileName, metaIf);
                    var condition = FoldRequiredBool(metaIf.expression(), "#if", location);

                    IEnumerable<NamespaceDeclarationContext> picked;
                    if (condition)
                    {
                        picked = metaIf.namespaceDeclaration();
                    }
                    else
                    {
                        MetaElifDeclarationContext? takenElif = null;
                        foreach (var elif in metaIf.metaElifDeclaration())
                        {
                            if (FoldRequiredBool(elif.expression(), "#elif", SourceLocation.From(FileName, elif)))
                            {
                                takenElif = elif;
                                break;
                            }
                        }
                        picked = takenElif != null
                            ? takenElif.namespaceDeclaration()
                            : metaIf.metaElseDeclaration()?.namespaceDeclaration() ?? [];
                    }
                    return picked.ToList();
                }

                if (meta.metaWhileDeclaration() is { } metaWhile)
                {
                    var location = SourceLocation.From(FileName, metaWhile);
                    var result = new List<NamespaceDeclarationContext>();
                    int iteration = 0;
                    while (iteration < MaxWhileIterations)
                    {
                        if (!FoldRequiredBool(metaWhile.expression(), "#while", location))
                            return result;
                        result.AddRange(metaWhile.namespaceDeclaration());
                        iteration++;
                    }
                    throw new BabyPenguinException(
                        $"meta #while did not converge (exceeded {MaxWhileIterations} iterations)", location, ErrorCode.E_UNSUPPORTED);
                }

                throw new BabyPenguinException("unsupported meta declaration", SourceLocation.From(FileName, meta), ErrorCode.E_UNSUPPORTED);
            }

            /// <summary>
            /// Namespace bodies keep the picked namespaceDeclaration wrappers;
            /// every other container unwraps them so the member contexts sit
            /// directly in the container's children (the per-kind visitors
            /// scan children by type). Unknown member kinds are rejected
            /// rather than silently dropped.
            /// </summary>
            private List<IParseTree> LiftAll(List<NamespaceDeclarationContext> picked, ContainerKind kind)
            {
                if (kind == ContainerKind.Namespace)
                    return [.. picked];

                var lifted = new List<IParseTree>();
                foreach (var decl in picked)
                {
                    foreach (var def in InnerDefs(decl))
                        lifted.Add(LiftMember(def, kind));
                }
                return lifted;
            }

            private ParserRuleContext LiftMember(ParserRuleContext def, ContainerKind kind)
            {
                var location = SourceLocation.From(FileName, def);
                var allowed = def switch
                {
                    FunctionDefinitionContext => true,
                    InterfaceImplementationContext => kind is ContainerKind.Class or ContainerKind.Enum or ContainerKind.Interface,
                    InitialRoutineContext => kind == ContainerKind.Class,
                    ConstructBlockContext => kind == ContainerKind.Class,
                    _ => false,
                };
                if (!allowed)
                    throw new BabyPenguinException(
                        $"this definition kind inside a #if/#while branch is not supported in a {kind.ToString().ToLower()} body (only functions are gated here)",
                        location, ErrorCode.E_UNSUPPORTED);
                // The lifted member may itself wrap nested meta — recurse so
                // ordering stays source-accurate.
                RewriteDef(def);
                return def;
            }

            // ───────────────────────── statement level ─────────────────────────

            /// <summary>
            /// Resolve meta statements inside a code block in place (a meta
            /// statement splices to zero or more code-block items).
            /// </summary>
            public void RewriteCodeBlock(CodeBlockExpressionContext block)
            {
                var children = block.children;
                if (children == null)
                    return;
                for (int i = 0; i < children.Count;)
                {
                    if (children[i] is CodeBlockItemContext item && item.statement() is { } statement)
                    {
                        if (IsMetaStatement(statement))
                        {
                            var pickedItems = ExpandMetaStatementToItems(statement, block);
                            children.RemoveAt(i);
                            InsertAll(children, i, pickedItems);
                            i += pickedItems.Count;
                            continue;
                        }
                        RewriteNestedStatement(statement);
                    }
                    i++;
                }
            }

            private static bool IsMetaStatement(StatementContext statement)
                => statement.metaIfStatement() != null
                   || statement.metaWhileStatement() != null
                   || IsDefineStatement(statement);

            /// <summary>#define("K","V"); at statement level parses as expressionStatement(metaCallExpression).</summary>
            private static bool IsDefineStatement(StatementContext statement)
                => statement.expressionStatement() != null
                   && statement.expressionStatement().expression() != null
                   && Unwrap(statement.expressionStatement().expression()) is MetaCallExpressionContext metaCall
                   && metaCall.identifier()?.GetText() == "define";

            /// <summary>
            /// Expand a meta statement into code-block items. A braced body is
            /// flattened (its items are re-wrapped for the OUTER block, nested
            /// meta resolved recursively); a single statement is wrapped as
            /// one item.
            /// </summary>
            private List<IParseTree> ExpandMetaStatementToItems(StatementContext statement, CodeBlockExpressionContext outerBlock)
            {
                var result = new List<IParseTree>();

                if (statement.metaIfStatement() is { } metaIf)
                {
                    var location = SourceLocation.From(FileName, metaIf);
                    if (!FoldRequiredBool(metaIf.expression(), "#if", location))
                    {
                        foreach (var elif in metaIf.metaElifStatement())
                        {
                            if (FoldRequiredBool(elif.expression(), "#elif", SourceLocation.From(FileName, elif)))
                            {
                                FlattenStatementInto(elif.statement(), outerBlock, result);
                                return result;
                            }
                        }
                        if (metaIf.statement().Length > 1)
                            FlattenStatementInto(metaIf.statement()[^1], outerBlock, result);
                    }
                    else
                    {
                        FlattenStatementInto(metaIf.statement(0), outerBlock, result);
                    }
                    return result;
                }

                if (statement.metaWhileStatement() is { } metaWhile)
                {
                    var location = SourceLocation.From(FileName, metaWhile);
                    int iteration = 0;
                    while (iteration < MaxWhileIterations)
                    {
                        if (!FoldRequiredBool(metaWhile.expression(), "#while", location))
                            return result;
                        FlattenStatementInto(metaWhile.statement(), outerBlock, result);
                        iteration++;
                    }
                    throw new BabyPenguinException(
                        $"meta #while did not converge (exceeded {MaxWhileIterations} iterations)", location, ErrorCode.E_UNSUPPORTED);
                }

                if (IsDefineStatement(statement))
                {
                    var call = (MetaCallExpressionContext)Unwrap(statement.expressionStatement().expression())!;
                    ConsumeMetaDefine(
                        call.identifier()?.GetText(),
                        call.expression().ToList(),
                        SourceLocation.From(FileName, call));
                    return result;
                }

                throw new BabyPenguinException("unsupported meta statement", SourceLocation.From(FileName, statement), ErrorCode.E_UNSUPPORTED);
            }

            /// <summary>
            /// Append one picked branch statement to `result` as code-block
            /// items: braced bodies are flattened (nested meta resolves
            /// recursively); a single statement is wrapped as-is.
            /// </summary>
            private void FlattenStatementInto(StatementContext statement, CodeBlockExpressionContext outerBlock, List<IParseTree> result)
            {
                if (statement.codeBlockExpression() is { } innerBlock)
                {
                    // The spliced block's own items lift to the OUTER block;
                    // a trailing expression is dropped (matches EmperorPenguin's
                    // flatten_statement_into, which only moves statements).
                    // Non-statement items (let declarations, type references)
                    // carry no meta and are moved verbatim.
                    var children = innerBlock.children;
                    if (children == null)
                        return;
                    foreach (var child in children.ToList())
                    {
                        if (child is not CodeBlockItemContext item)
                            continue;
                        if (item.statement() is not { } innerStatement)
                        {
                            result.Add(item);
                            continue;
                        }
                        if (IsMetaStatement(innerStatement))
                        {
                            result.AddRange(ExpandMetaStatementToItems(innerStatement, outerBlock));
                        }
                        else
                        {
                            RewriteNestedStatement(innerStatement);
                            result.Add(WrapItem(innerStatement, outerBlock));
                        }
                    }
                    return;
                }

                if (IsMetaStatement(statement))
                {
                    result.AddRange(ExpandMetaStatementToItems(statement, outerBlock));
                    return;
                }

                RewriteNestedStatement(statement);
                result.Add(WrapItem(statement, outerBlock));
            }

            private static CodeBlockItemContext WrapItem(StatementContext statement, CodeBlockExpressionContext block)
            {
                var wrapper = new CodeBlockItemContext(block, PenguinLangParser.RULE_codeBlockItem);
                wrapper.AddChild(statement);
                // Hand-built contexts get no Start/Stop from the parser —
                // SyntaxNode.Build reads them for source text/location.
                wrapper.Start = statement.Start;
                wrapper.Stop = statement.Stop;
                return wrapper;
            }

            /// <summary>
            /// Recurse into a kept (non-meta) statement's nested control-flow
            /// bodies: if / while / for / try bodies and direct sub-blocks.
            /// </summary>
            private void RewriteNestedStatement(StatementContext statement)
            {
                if (statement.codeBlockExpression() is { } block)
                {
                    RewriteCodeBlock(block);
                    return;
                }

                if (statement.expressionStatement() is { } exprStatement)
                {
                    // `{ ... };` parses as an expression statement whose
                    // outermost expression is a code block (Unwrap descends
                    // through the single-child chains to it).
                    if (Unwrap(exprStatement.expression()) is CodeBlockExpressionContext exprBlock)
                        RewriteCodeBlock(exprBlock);
                    return;
                }

                if (statement.ifStatement() is { } ifStatement)
                {
                    foreach (var body in ifStatement.statement())
                        RewriteSubStatement(body);
                    return;
                }

                if (statement.whileStatement() is { } whileStatement)
                {
                    RewriteSubStatement(whileStatement.statement());
                    return;
                }

                if (statement.forStatement() is { } forStatement)
                {
                    RewriteSubStatement(forStatement.statement());
                    return;
                }

                if (statement.tryStatement() is { } tryStatement)
                {
                    foreach (var body in tryStatement.statement())
                        RewriteSubStatement(body);
                    return;
                }
            }

            /// <summary>
            /// A statement in single-statement position (if/while/for/try
            /// body): a meta statement here collapses to ONE statement — a
            /// picked braced block stays a block, a consumed #define becomes
            /// an empty `;`.
            /// </summary>
            private void RewriteSubStatement(StatementContext statement)
            {
                if (!IsMetaStatement(statement))
                {
                    RewriteNestedStatement(statement);
                    return;
                }

                var replacement = ExpandMetaStatementSingle(statement);
                if (replacement != statement && statement.Parent is ParserRuleContext parent
                    && parent.children != null)
                {
                    var index = parent.children.IndexOf(statement);
                    if (index >= 0)
                        parent.children[index] = replacement;
                }
            }

            /// <summary>Single-statement-position variant of the meta expansion.</summary>
            private StatementContext ExpandMetaStatementSingle(StatementContext statement)
            {
                if (statement.metaIfStatement() is { } metaIf)
                {
                    var location = SourceLocation.From(FileName, metaIf);
                    StatementContext? picked = null;
                    if (FoldRequiredBool(metaIf.expression(), "#if", location))
                    {
                        picked = metaIf.statement(0);
                    }
                    else
                    {
                        foreach (var elif in metaIf.metaElifStatement())
                        {
                            if (FoldRequiredBool(elif.expression(), "#elif", SourceLocation.From(FileName, elif)))
                            {
                                picked = elif.statement();
                                break;
                            }
                        }
                        if (picked == null && metaIf.statement().Length > 1)
                            picked = metaIf.statement()[^1];
                    }
                    return CollapseToSingleStatement(picked, metaIf);
                }

                if (statement.metaWhileStatement() is { } metaWhile)
                {
                    var location = SourceLocation.From(FileName, metaWhile);
                    // The condition cannot change mid-splice (a redefinition
                    // inside the body would be E_DUPLICATE_SYMBOL), so a
                    // single-statement #while is either zero iterations or divergent.
                    if (!FoldRequiredBool(metaWhile.expression(), "#while", location))
                        return MakeEmptyStatement(statement.Parent as ParserRuleContext, metaWhile);
                    throw new BabyPenguinException(
                        $"meta #while did not converge (exceeded {MaxWhileIterations} iterations)", location, ErrorCode.E_UNSUPPORTED);
                }

                if (IsDefineStatement(statement))
                {
                    var call = (MetaCallExpressionContext)Unwrap(statement.expressionStatement().expression())!;
                    ConsumeMetaDefine(
                        call.identifier()?.GetText(),
                        call.expression().ToList(),
                        SourceLocation.From(FileName, call));
                    return MakeEmptyStatement(statement.Parent as ParserRuleContext, call);
                }

                throw new BabyPenguinException("unsupported meta statement", SourceLocation.From(FileName, statement), ErrorCode.E_UNSUPPORTED);
            }

            private StatementContext CollapseToSingleStatement(StatementContext? picked, ParserRuleContext origin)
            {
                if (picked == null)
                {
                    // Every branch false with no #else: the single-statement
                    // position collapses to an empty `;` statement.
                    return MakeEmptyStatement(null, origin);
                }
                if (IsMetaStatement(picked))
                    return ExpandMetaStatementSingle(picked);
                if (picked.codeBlockExpression() is { } block)
                {
                    // Keep the braced block as the single statement; splice its contents.
                    RewriteCodeBlock(block);
                    return picked;
                }
                RewriteNestedStatement(picked);
                return picked;
            }

            private static StatementContext MakeEmptyStatement(ParserRuleContext? parent, ParserRuleContext? origin)
            {
                var empty = new StatementContext(parent, PenguinLangParser.RULE_statement);
                var semicolon = new CommonToken(-1, ";");
                empty.AddChild(new TerminalNodeImpl(semicolon));
                // Hand-built contexts get no Start/Stop from the parser —
                // SyntaxNode.Build reads them (and their InputStream) for
                // source text/location, so anchor to the origin meta
                // statement's real tokens; the synthetic ';' only supplies
                // the text (GetText() == ";") for the Empty-statement visitor.
                empty.Start = origin?.Start ?? semicolon;
                empty.Stop = origin?.Stop ?? semicolon;
                return empty;
            }

            // ───────────────────────── define store ─────────────────────────

            private void ConsumeMetaDefine(string? name, List<ExpressionContext> arguments, SourceLocation location)
            {
                if (name != "define")
                    throw new BabyPenguinException(
                        $"unknown meta call #{name} in definition position (only #define(\"K\",\"V\") is supported here)",
                        location, ErrorCode.E_UNSUPPORTED);
                if (arguments.Count != 2
                    || FoldString(arguments[0]) is not { } key
                    || FoldString(arguments[1]) is not { } value)
                    throw new BabyPenguinException(
                        "malformed #define: expected #define(\"KEY\",\"VALUE\"); with two string literals",
                        location, ErrorCode.E_UNSUPPORTED);
                if (CodeDefined.Contains(key))
                    throw new BabyPenguinException(
                        $"#define: '{key}' is already defined in this file (file-scoped redefinition is an error)",
                        location, ErrorCode.E_DUPLICATE_SYMBOL);
                CodeDefined.Add(key);
                // Overwriting the CLI seed layer is allowed (code wins over --define).
                Store[key] = value;
            }

            // ───────────────────────── condition folding ─────────────────────────

            private bool FoldRequiredBool(ExpressionContext? expression, string construct, SourceLocation location)
                => FoldBool(expression) ?? throw new BabyPenguinException(
                    $"meta {construct} condition is not a compile-time constant", location, ErrorCode.E_UNSUPPORTED);

            /// <summary>
            /// Fold a compile-time boolean. Supports bool literals,
            /// #defined("K"), !e, parenthesization, &amp;&amp;/|| chains and
            /// ==/!= over foldable strings (bare define name /
            /// string literal) or foldable bools. Mirrors EmperorPenguin's
            /// eval_meta_bool/eval_meta_string.
            /// </summary>
            private bool? FoldBool(ParserRuleContext? expression)
            {
                switch (Unwrap(expression))
                {
                    case BoolLiteralContext boolLiteral:
                        return boolLiteral.GetText() == "true";
                    case MetaCallExpressionContext metaCall:
                        return metaCall.identifier()?.GetText() == "defined"
                               && metaCall.expression().Length == 1
                               && FoldString(metaCall.expression(0)) is { } key
                            ? Store.ContainsKey(key)
                            : null;
                    case PrimaryExpressionContext primary:
                        // Parenthesized primary: '(' expression ')'. (Single-child
                        // primaries never reach this case — Unwrap descends
                        // through them to the leaf context above.)
                        if (primary.ChildCount == 3 && primary.GetChild(0).GetText() == "(")
                            return FoldBool(primary.expression());
                        return null;
                    case UnaryExpressionContext unary:
                    {
                        if (unary.unaryOperator()?.GetText() == "!" && unary.postfixExpression() != null)
                            return FoldBool(Unwrap(unary.postfixExpression())) switch
                            {
                                true => false,
                                false => true,
                                _ => (bool?)null,
                            };
                        return null;
                    }
                    case LogicalAndExpressionContext logicalAnd:
                    {
                        foreach (var operand in logicalAnd.bitwiseOrExpression())
                        {
                            var folded = FoldBool(operand);
                            if (folded == false) return false;
                            if (folded == null) return null;
                        }
                        return true;
                    }
                    case LogicalOrExpressionContext logicalOr:
                    {
                        foreach (var operand in logicalOr.logicalAndExpression())
                        {
                            var folded = FoldBool(operand);
                            if (folded == true) return true;
                            if (folded == null) return null;
                        }
                        return false;
                    }
                    case EqualityExpressionContext equality:
                    {
                        var operands = equality.relationalExpression();
                        var op = equality.equalityOperator()?.GetText();
                        if (operands.Length != 2 || (op != "==" && op != "!="))
                            return null;
                        // String comparison first: NAME == "v" (bare define name).
                        var leftString = FoldString(operands[0]);
                        var rightString = FoldString(operands[1]);
                        if (leftString != null && rightString != null)
                            return (leftString == rightString) == (op == "==");
                        var leftBool = FoldBool(operands[0]);
                        var rightBool = FoldBool(operands[1]);
                        if (leftBool != null && rightBool != null)
                            return (leftBool == rightBool) == (op == "==");
                        return null;
                    }
                    default:
                        return null;
                }
            }

            /// <summary>
            /// Fold a compile-time string: a string literal, or a bare
            /// identifier that names a define (redesign §3: NAME == "v"; the
            /// batch 2.3 replacement for the retired #option("K") read).
            /// Returns null when not foldable / the key is undefined.
            /// </summary>
            private string? FoldString(ParserRuleContext? expression)
            {
                switch (Unwrap(expression))
                {
                    case PrimaryExpressionContext primary:
                        // String-literal primary (adjacent literals concatenate;
                        // its child is a terminal so Unwrap stops here).
                        if (primary.StringLiteral().Length > 0)
                            return StripQuotes(primary.GetText());
                        // Parenthesized primary.
                        if (primary.ChildCount == 3 && primary.GetChild(0).GetText() == "(")
                            return FoldString(primary.expression());
                        return null;
                    case IdentifierContext identifier:
                        // Bare define name (redesign §3: NAME == "v").
                        return Store.TryGetValue(identifier.GetText(), out var defineValue) ? defineValue : null;
                    default:
                        return null;
                }
            }

            private static string StripQuotes(string quoted)
                => quoted.Length >= 2 ? quoted[1..^1] : quoted;

            /// <summary>
            /// Descend single-child expression chains (relational → additive →
            /// … → postfix → primary) so every fold entry sees the innermost
            /// meaningful context. Returns null for null input.
            /// </summary>
            private static ParserRuleContext? Unwrap(ParserRuleContext? context)
            {
                var current = context;
                while (current != null
                       && current.ChildCount == 1
                       && current.GetChild(0) is ParserRuleContext onlyChild)
                {
                    current = onlyChild;
                }
                return current;
            }
        }
    }
}
