namespace BabyPenguin.SemanticPass
{
    public interface ICodeContainer : ISymbolContainer, ISemanticNode
    {
        List<BabyPenguinIR> Instructions { get; }

        SyntaxNode? CodeSyntaxNode { get; }

        IType ReturnTypeInfo { get; set; }

        void CompileSyntaxStatements()
        {
            if (CodeSyntaxNode is CodeBlock codeBlock)
            {
                foreach (var item in codeBlock.BlockItems)
                {
                    AddCodeBlockItem(item);
                }
            }
            else if (CodeSyntaxNode is Statement statement)
            {
                AddStatement(statement);
            }
            else if (CodeSyntaxNode is PenguinLangSyntax.NamespaceDefinition)
            {
                foreach (var decl in (CodeSyntaxNode as PenguinLangSyntax.NamespaceDefinition)!.Declarations)
                {
                    if (decl.InitializeExpression != null)
                    {
                        var symbol = Model.ResolveSymbol(decl.Name, scopeDepth: decl.Scope.ScopeDepth, scope: this);
                        AddExpression(decl.InitializeExpression, symbol);
                    }
                }
            }
        }

        public string PrintInstructionsTable()
        {
            var table = new ConsoleTable("Instruction", "OP1", "OP2", "Result", "Labels");
            foreach (var ins in Instructions)
            {
                table.AddRow(ins.StringCommand, ins.StringOP1, ins.StringOP2, ins.StringResult, ins.StringLabels);
            }
            return table.ToMarkDownString();
        }

        public void AddCodeBlockItem(CodeBlockItem item)
        {
            if (item.IsDeclaration)
            {
                AddLocalDeclearation(item.Declaration!, null);
            }
            else
            {
                AddStatement(item.Statement!);
            }
        }

        public void AddLocalDeclearation(Declaration item, int? paramIndex)
        {
            var typeName = item.TypeSpecifier!.Name; // TODO: type inference
            var symbol = AddVariableSymbol(item.Name, true, typeName, item.SourceLocation, item.Scope.ScopeDepth, paramIndex, item.IsReadonly, false);
            if (item.InitializeExpression != null)
            {
                AddExpression(item.InitializeExpression, symbol);
            }
        }

        public string CreateLabel() => $"{Name}_{counter++}";

        public class CodeContainerStorage
        {
            public Stack<CurrentWhileLoopInfo> CurrentWhileLoop { get; } = new Stack<CurrentWhileLoopInfo>();
        }

        public record CurrentWhileLoopInfo(string BeginLabel, string EndLabel) { }

        public CodeContainerStorage CodeContainerData { get; }

        public void AddStatement(Statement item)
        {
            switch (item.StatementType)
            {
                case Statement.Type.AssignmentStatement:
                    {
                        var rightVar = AddExpression(item.AssignmentStatement!.RightHandSide);
                        ISymbol? member = null;
                        ISymbol? target;
                        bool isMemberAccess = item.AssignmentStatement.LeftHandSide.IsMemberAccess;
                        if (!isMemberAccess)
                        {
                            target = Model.ResolveShortSymbol(item.AssignmentStatement.LeftHandSide.Identifier!.Name,
                                s => !s.IsClassMember,
                                scopeDepth: item.Scope.ScopeDepth, scope: this);
                        }
                        else
                        {
                            var ma = item.AssignmentStatement.LeftHandSide.MemberAccess!;
                            if (ma.PrimaryExpression.IsSimple)
                            {
                                target = Model.ResolveShortSymbol(ma.PrimaryExpression.Text,
                                    s => !s.IsClassMember, scopeDepth: item.Scope.ScopeDepth, scope: this);
                                if (target == null)
                                {
                                    throw new BabyPenguinException($"Cant resolve symbol '{ma.PrimaryExpression.Text}'", ma.PrimaryExpression.SourceLocation);
                                }
                            }
                            else
                            {
                                target = AddExpression(ma.PrimaryExpression);
                            }
                            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                            {
                                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                                var symbolName = target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name;
                                member = Model.ResolveSymbol(symbolName, scope: this);
                                if (member == null)
                                    throw new BabyPenguinException($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                                if (!isLastRound)
                                {
                                    if (target.TypeInfo.IsEnumType && !member.IsFunction)
                                    {
                                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                                        AddInstruction(new ReadEnumInstruction(target, temp));
                                        target = temp;
                                    }
                                    else
                                    {
                                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                                        AddInstruction(new ReadMemberInstruction(member, target, temp));
                                        target = temp;
                                    }
                                }
                            }
                        }

                        if (target == null)
                        {
                            throw new BabyPenguinException($"Cant resolve symbol '{item.AssignmentStatement.LeftHandSide}'", item.AssignmentStatement.LeftHandSide.SourceLocation);
                        }
                        else
                        {
                            if (item.AssignmentStatement.AssignmentOperator == AssignmentOperatorEnum.Assign)
                            {
                                if (!isMemberAccess)
                                    AddInstruction(new AssignmentInstruction(rightVar, target));
                                else
                                    AddInstruction(new WriteMemberInstruction(member!, rightVar, target));
                            }
                            else
                            {
                                var op = item.AssignmentStatement.AssignmentOperator switch
                                {
                                    AssignmentOperatorEnum.AddAssign => BinaryOperatorEnum.Add,
                                    AssignmentOperatorEnum.SubtractAssign => BinaryOperatorEnum.Subtract,
                                    AssignmentOperatorEnum.MultiplyAssign => BinaryOperatorEnum.Multiply,
                                    AssignmentOperatorEnum.DivideAssign => BinaryOperatorEnum.Divide,
                                    AssignmentOperatorEnum.ModuloAssign => BinaryOperatorEnum.Modulo,
                                    AssignmentOperatorEnum.BitwiseAndAssign => BinaryOperatorEnum.BitwiseAnd,
                                    AssignmentOperatorEnum.BitwiseOrAssign => BinaryOperatorEnum.BitwiseOr,
                                    AssignmentOperatorEnum.BitwiseXorAssign => BinaryOperatorEnum.BitwiseXor,
                                    AssignmentOperatorEnum.LeftShiftAssign => BinaryOperatorEnum.LeftShift,
                                    AssignmentOperatorEnum.RightShiftAssign => BinaryOperatorEnum.RightShift,
                                    _ => throw new NotImplementedException(),
                                };
                                if (!isMemberAccess)
                                {
                                    var temp = AllocTempSymbol(target.TypeInfo, item.SourceLocation);
                                    AddInstruction(new BinaryOperationInstruction(op, target, rightVar, temp));
                                    AddInstruction(new AssignmentInstruction(temp, target));
                                }
                                else
                                {
                                    var tempBeforeCalc = AddExpression(item.AssignmentStatement.LeftHandSide.MemberAccess!);
                                    var tempAfterCalc = AllocTempSymbol(ResolveBinaryOperationType(op, [tempBeforeCalc.TypeInfo, rightVar.TypeInfo], item.SourceLocation), item.SourceLocation);
                                    AddInstruction(new BinaryOperationInstruction(op, tempBeforeCalc, rightVar, tempAfterCalc));
                                    AddInstruction(new WriteMemberInstruction(member!, tempAfterCalc, target));
                                }
                                break;
                            }
                        }
                        break;
                    }
                case Statement.Type.ExpressionStatement:
                    {
                        AddExpression(item.ExpressionStatement!.Expression);
                        break;
                    }
                case Statement.Type.SubBlock:
                    {
                        foreach (var subItem in item.CodeBlock!.BlockItems)
                        {
                            AddCodeBlockItem(subItem);
                        }
                        break;
                    }
                case Statement.Type.IfStatement:
                    {
                        var ifStatement = item.IfStatement!;
                        var conditionVar = AddExpression(ifStatement.Condition);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            Reporter.Throw($"If condition must be bool type, but got '{conditionVar.TypeInfo}'", ifStatement.SourceLocation);
                        if (ifStatement.HasElse)
                        {
                            var elseLabel = CreateLabel();
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(elseLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement);
                            AddInstruction(new GotoInstruction(endifLabel));
                            AddInstruction(new NopInstuction().WithLabel(elseLabel));
                            AddStatement(ifStatement.ElseStatement!);
                            AddInstruction(new NopInstuction().WithLabel(endifLabel));
                        }
                        else
                        {
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(endifLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement);
                            AddInstruction(new NopInstuction().WithLabel(endifLabel));
                        }
                        break;
                    }
                case Statement.Type.WhileStatement:
                    {
                        var whileStatement = item.WhileStatement!;
                        var beginLabel = CreateLabel();
                        AddInstruction(new NopInstuction().WithLabel(beginLabel));
                        var conditionVar = AddExpression(whileStatement.Condition);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            Reporter.Throw($"While condition must be bool type, but got '{conditionVar.TypeInfo}'", whileStatement.SourceLocation);
                        var endLabel = CreateLabel();

                        CodeContainerData.CurrentWhileLoop.Push(new CurrentWhileLoopInfo(beginLabel, endLabel));

                        AddInstruction(new GotoInstruction(endLabel, conditionVar, false));
                        AddStatement(whileStatement.BodyStatement);
                        AddInstruction(new GotoInstruction(beginLabel));

                        CodeContainerData.CurrentWhileLoop.Pop();

                        AddInstruction(new NopInstuction().WithLabel(endLabel));
                        break;
                    }
                case Statement.Type.ForStatement:
                    throw new NotImplementedException();
                case Statement.Type.ReturnStatement:
                    {
                        var returnStatement = item.ReturnStatement!;
                        if (returnStatement.ReturnExpression != null)
                        {
                            var returnVar = AddExpression(returnStatement.ReturnExpression);
                            AddInstruction(new ReturnInstruction(returnVar));
                        }
                        else
                        {
                            AddInstruction(new ReturnInstruction(null));
                        }
                        break;
                    }
                case Statement.Type.JumpStatement:
                    {
                        var jumpStatement = item.JumpStatement!;
                        if (jumpStatement.JumpType == JumpStatement.Type.Break)
                        {
                            if (CodeContainerData.CurrentWhileLoop.Count == 0)
                                throw new BabyPenguinException("Break statement outside of while loop", jumpStatement.SourceLocation);
                            var currentWhileLoop = CodeContainerData.CurrentWhileLoop.Peek();
                            AddInstruction(new GotoInstruction(currentWhileLoop.EndLabel));
                        }
                        else if (jumpStatement.JumpType == JumpStatement.Type.Continue)
                        {
                            if (CodeContainerData.CurrentWhileLoop.Count == 0)
                                throw new BabyPenguinException("Continue statement outside of while loop", jumpStatement.SourceLocation);
                            var currentWhileLoop = CodeContainerData.CurrentWhileLoop.Peek();
                            AddInstruction(new GotoInstruction(currentWhileLoop.BeginLabel));
                        }
                        else
                            throw new NotImplementedException();
                        break;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public IType ResolveBinaryOperationType(BinaryOperatorEnum op, IEnumerable<IType> opType, SourceLocation sourceLocation)
        {
            var types = opType.ToList();
            if (types.Count == 0)
            {
                throw new NotImplementedException();
            }
            IType checkTypes(List<IType> types, bool allowImplicitConversion = true)
            {
                if (types.Find(i => i.IsClassType) != null)
                {
                    throw new BabyPenguinException($"Type {types.Find(i => i.IsClassType)} can't be used here", sourceLocation);
                }

                if (!allowImplicitConversion)
                {
                    foreach (var t in types.Skip(1))
                        if (t != types.First())
                            throw new BabyPenguinException($"Incompatible types in expression, expected '{types.First()}' but got '{t}'", sourceLocation);
                }
                else
                {
                    return types.Aggregate((a, b) =>
                    {
                        if (a.CanImplicitlyCastTo(b))
                            return b;
                        else if (b.CanImplicitlyCastTo(a))
                            return a;
                        else
                            throw new BabyPenguinException($"Incompatible types in expression, expected '{a}' but got '{b}'", sourceLocation);
                    });
                }
                return types.First();
            }

            switch (op)
            {
                case BinaryOperatorEnum.Add:
                case BinaryOperatorEnum.Subtract:
                case BinaryOperatorEnum.Multiply:
                case BinaryOperatorEnum.Divide:
                case BinaryOperatorEnum.Modulo:
                case BinaryOperatorEnum.BitwiseAnd:
                case BinaryOperatorEnum.BitwiseOr:
                case BinaryOperatorEnum.BitwiseXor:
                    if (types.Count > 1)
                        return checkTypes(types, true);
                    else
                        return types.First();
                case BinaryOperatorEnum.LessThan:
                case BinaryOperatorEnum.GreaterThan:
                case BinaryOperatorEnum.LessThanOrEqual:
                case BinaryOperatorEnum.GreaterThanOrEqual:
                case BinaryOperatorEnum.Equal:
                case BinaryOperatorEnum.NotEqual:
                    if (types.Count > 1)
                    {
                        checkTypes(types, true);
                        return BasicType.Bool;
                    }
                    else
                        return types.First();
                case BinaryOperatorEnum.Is:
                    if (types.Count > 1)
                    {
                        return BasicType.Bool;
                    }
                    else return types.First();
                case BinaryOperatorEnum.LogicalAnd:
                case BinaryOperatorEnum.LogicalOr:
                    if (types.Count > 1)
                    {
                        checkTypes(types, false);
                        if (types.All(t => t.IsBoolType))
                        {
                            return BasicType.Bool;
                        }
                        else
                        {
                            throw new BabyPenguinException($"Logical operators can only be used with bool types, but got '{types.First()}'", sourceLocation);
                        }
                    }
                    else
                        return types.First();
                case BinaryOperatorEnum.LeftShift:
                case BinaryOperatorEnum.RightShift:
                    if (types.Count > 1)
                    {
                        var shiftType = types[0];
                        if (!shiftType.IsIntType) throw new BabyPenguinException($"Shift expression requires integer type, but got '{shiftType}'", sourceLocation);
                        foreach (var sub in types.Skip(1))
                            if (!sub.IsIntType) throw new BabyPenguinException($"Shift expression requires integer type, but got '{sub}'", sourceLocation);
                        return shiftType;
                    }
                    else
                    {
                        return types.First();
                    }
            }
            throw new NotImplementedException();
        }

        public IType ResolveMemberAccessExpressionOwnerType(MemberAccessExpression expression)
        {
            var t = ResolveExpressionType(expression.PrimaryExpression);
            if (t == null)
                throw new BabyPenguinException($"Cant resolve owner type of member access expression", expression.SourceLocation);
            var ma = expression;
            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
            {
                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                var member = Model.ResolveSymbol(t.FullName + "." + ma.MemberIdentifiers[i].Name);
                if (member == null)
                    throw new BabyPenguinException($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                if (isLastRound) return t;
                t = member.TypeInfo;
            }
            throw new NotImplementedException();
        }

        public IType ResolveExpressionType(ISyntaxExpression expression)
        {

            switch (expression)
            {
                case Expression exp:
                    return ResolveExpressionType(exp.SubExpression);
                case LogicalOrExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LogicalOr, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case LogicalAndExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LogicalAnd, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case BitWiseOrExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseOr, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case BitwiseXorExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseXor, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case BitwiseAndExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.BitwiseAnd, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case EqualityExpression exp:
                    if (exp.Operator == BinaryOperatorEnum.Is)
                        // return ResolveBinaryOperationType(BinaryOperatorEnum.Is, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                        return exp.SubExpressions.Count == 1 ? ResolveExpressionType(exp.SubExpressions[0]) : BasicType.Bool;
                    else
                        return ResolveBinaryOperationType(BinaryOperatorEnum.Equal, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case RelationalExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.GreaterThan, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case ShiftExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.LeftShift, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case AdditiveExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.Add, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case MultiplicativeExpression exp:
                    return ResolveBinaryOperationType(BinaryOperatorEnum.Multiply, exp.SubExpressions.Select(ResolveExpressionType), expression.SourceLocation);
                case CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        var t = Model.ResolveType(exp.CastTypeSpecifier!.Name, scope: this);
                        if (t == null) throw new BabyPenguinException($"Cant resolve type '{exp.CastTypeSpecifier.Name}'", exp.CastTypeSpecifier.SourceLocation);
                        else return t;
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubUnaryExpression!);
                    }
                case UnaryExpression exp:
                    if (exp.HasUnaryOperator)
                    {
                        switch (exp.UnaryOperator)
                        {
                            case UnaryOperatorEnum.Deref:
                            case UnaryOperatorEnum.Ref:
                                throw new NotImplementedException();
                            case UnaryOperatorEnum.Minus:
                                var type = ResolveExpressionType(exp.SubExpression);
                                return type.Type switch
                                {
                                    TypeEnum.U8 => BasicType.I8,
                                    TypeEnum.U16 => BasicType.I16,
                                    TypeEnum.U32 => BasicType.I32,
                                    _ => type
                                };
                            case UnaryOperatorEnum.Plus:
                            case UnaryOperatorEnum.BitwiseNot:
                                return ResolveExpressionType(exp.SubExpression);
                            case UnaryOperatorEnum.LogicalNot:
                                return BasicType.Bool;
                        }
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubExpression);
                    }
                    break;
                case PostfixExpression exp:
                    switch (exp.PostfixExpressionType)
                    {
                        case PostfixExpression.Type.FunctionCall:
                            return ResolveExpressionType(exp.SubFunctionCallExpression!);
                        case PostfixExpression.Type.MemberAccess:
                            return ResolveExpressionType(exp.SubMemberAccessExpression!);
                        // case PostfixExpression.Type.Slicing:
                        //     return ResolveExpressionType(exp.SubSlicingExpression!);
                        case PostfixExpression.Type.PrimaryExpression:
                            return ResolveExpressionType(exp.SubPrimaryExpression!);
                        case PostfixExpression.Type.New:
                            return ResolveExpressionType(exp.SubNewExpression!);
                    }
                    break;
                case FunctionCallExpression exp:
                    {
                        IType funcType;
                        bool isInstanceCall = false;
                        if (exp.IsMemberAccess)
                        {
                            if (CheckMemberAccessExpressionIsStatic(exp.MemberAccessExpression!))
                            {
                                var symbol = Model.ResolveSymbol(exp.MemberAccessExpression!.Text, s => s.IsStatic, scope: this);
                                if (symbol == null)
                                    throw new BabyPenguinException($"Cant resolve symbol '{exp.MemberAccessExpression!.Text}'", exp.SourceLocation);
                                funcType = symbol.TypeInfo;
                                isInstanceCall = false;
                            }
                            else
                            {
                                funcType = ResolveExpressionType(exp.MemberAccessExpression!);
                                isInstanceCall = true;
                            }
                        }
                        else
                        {
                            funcType = ResolveExpressionType(exp.PrimaryExpression!);
                        }
                        if (!funcType.IsFunctionType)
                            throw new BabyPenguinException($"Function call expects function symbol, but got '{funcType}'", expression.SourceLocation);
                        var actualParamsCount = isInstanceCall ? exp.ArgumentsExpression.Count + 1 : exp.ArgumentsExpression.Count;
                        if (funcType.GenericArguments.Count - 1 != actualParamsCount)
                            throw new BabyPenguinException($"Function expects {funcType.GenericArguments.Count - 1} params, but got '{actualParamsCount}'", expression.SourceLocation);
                        for (int i = 0; i < funcType.GenericArguments.Count - 1; i++)
                        {
                            var expectedType = funcType.GenericArguments[i + 1];
                            IType actualType;
                            SourceLocation sourceLocation;
                            if (i == 0 && isInstanceCall)
                            {
                                actualType = ResolveMemberAccessExpressionOwnerType(exp.MemberAccessExpression!);
                                sourceLocation = exp.SourceLocation;
                            }
                            else
                            {
                                actualType = ResolveExpressionType(exp.ArgumentsExpression[isInstanceCall ? i - 1 : i]);
                                sourceLocation = exp.ArgumentsExpression[isInstanceCall ? i - 1 : i].SourceLocation;
                            }
                            if (!actualType.CanImplicitlyCastTo(expectedType))
                                throw new BabyPenguinException($"Function expects {expectedType} param, but got '{actualType}'", sourceLocation);
                        }
                        return funcType.GenericArguments.First();
                    }
                case MemberAccessExpression exp:
                    {
                        if (CheckMemberAccessExpressionIsStatic(exp))
                        {
                            var symbol = Model.ResolveSymbol(exp.Text, s => s.IsStatic, scope: this);
                            if (symbol == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{exp.Text}'", exp.SourceLocation);
                            return symbol.TypeInfo;
                        }
                        else
                        {
                            var t = ResolveExpressionType(exp.PrimaryExpression);
                            var ma = exp;
                            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                            {
                                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                                var symbolName = t.FullName + "." + ma.MemberIdentifiers[i].Name;
                                // var member = Model.ResolveSymbol(symbolName, s => s.IsEnum == t.IsEnumType, scope: this);
                                var member = Model.ResolveSymbol(symbolName, scope: this);

                                if (member == null)
                                    throw new BabyPenguinException($"Cant resolve symbol '{symbolName}'", ma.MemberIdentifiers[i].SourceLocation);
                                t = member.TypeInfo;
                            }
                            return t;
                        }
                    }
                case PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case PrimaryExpression.Type.Identifier:
                            var symbol = Model.ResolveShortSymbol(exp.Identifier!.Name,
                                s => !s.IsClassMember, scopeDepth: exp.Scope.ScopeDepth, scope: this);
                            if (symbol == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else
                                return symbol.TypeInfo;
                        case PrimaryExpression.Type.Constant:
                            var t = BasicType.ResolveLiteralType(exp.Literal!);
                            if (t == null)
                                throw new BabyPenguinException($"Cant resolve literal type '{exp.Literal}'", exp.SourceLocation);
                            else
                                return t;
                        case PrimaryExpression.Type.StringLiteral:
                            return BasicType.String;
                        case PrimaryExpression.Type.BoolLiteral:
                            return BasicType.Bool;
                        case PrimaryExpression.Type.ParenthesizedExpression:
                            return ResolveExpressionType(exp.ParenthesizedExpression!);
                    }
                    break;
                case NewExpression exp:
                    {
                        var res = Model.ResolveType(exp.TypeSpecifier.Name, scope: this);
                        if (res == null)
                        {
                            // if new enum, remove last dot and try again
                            var parts = NameComponents.SplitStringPreservingAngleBrackets(exp.TypeSpecifier.Name, '.');
                            if (parts.Count > 1)
                            {
                                var typeStr = parts.Take(parts.Count - 1).Aggregate((a, b) => a + "." + b);
                                res = Model.ResolveType(typeStr, scope: this);
                            }
                        }

                        if (res == null)
                            throw new BabyPenguinException($"Cant resolve type '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);
                        return res;
                    }
                default:
                    break;
            }
            throw new BabyPenguinException($"Unsupported expression type '{expression.GetType()}'", expression.SourceLocation);
        }

        public bool CheckMemberAccessExpressionIsStatic(MemberAccessExpression exp)
        {
            if (!exp.PrimaryExpression.IsSimple)
                return false;

            var symbol = Model.ResolveShortSymbol(exp.PrimaryExpression.Text,
                s => !s.IsClassMember, scopeDepth: exp.Scope.ScopeDepth, scope: this);

            if (symbol == null)
            {
                symbol = Model.ResolveSymbol(exp.Text, s => s.IsStatic || s.IsEnum, scope: this);
                if (symbol == null)
                    throw new BabyPenguinException($"Cant determine expression type for '{exp.Text}'", exp.SourceLocation);
                else
                    return true;
            }
            else
                return false;
        }

        public ISymbol AddMemberAccessExpression(MemberAccessExpression exp, ISymbol to, out ISymbol? owner)
        {
            ISymbol owner_var;
            if (CheckMemberAccessExpressionIsStatic(exp))
            {
                owner = null;
                var res = Model.ResolveSymbol(exp.Text, s => s.IsStatic, scope: this);
                if (res == null)
                    throw new BabyPenguinException($"Cant resolve symbol '{exp.Text}'", exp.SourceLocation);
                return res;
            }
            else
            {
                if (exp.PrimaryExpression.IsSimple)
                {
                    var temp = Model.ResolveShortSymbol(exp.PrimaryExpression.Text,
                        s => !s.IsClassMember, scopeDepth: exp.Scope.ScopeDepth, scope: this);

                    if (temp == null)
                        throw new BabyPenguinException($"Cant resolve symbol '{exp.PrimaryExpression.Text}'", exp.PrimaryExpression.SourceLocation);
                    owner_var = temp;
                }
                else
                {
                    owner_var = AddExpression(exp.PrimaryExpression);
                }
            }

            var ma = exp;
            ISymbol target = owner_var;
            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
            {
                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                var symbolName = target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name;
                // var member = Model.ResolveSymbol(symbolName, s => s.IsEnum == target.TypeInfo.IsEnumType, scope: this);
                var member = Model.ResolveSymbol(symbolName, scope: this);

                if (member == null)
                    throw new BabyPenguinException($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                if (isLastRound)
                {
                    if (target.TypeInfo.IsEnumType && !member.IsFunction)
                        AddInstruction(new ReadEnumInstruction(target, to));
                    else
                        AddInstruction(new ReadMemberInstruction(member, target, to));
                }
                else
                {
                    if (target.TypeInfo.IsEnumType && !member.IsFunction)
                    {
                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                        AddInstruction(new ReadEnumInstruction(target, temp));
                        target = temp;
                    }
                    else
                    {
                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                        AddInstruction(new ReadMemberInstruction(member, target, temp));
                        target = temp;
                    }
                }
            }
            owner = target;
            return to;
        }

        public ISymbol AddCastExpression(CastExpression exp, ISymbol to)
        {
            if (ResolveExpressionType(exp).FullName != to.TypeInfo.FullName)
                throw new BabyPenguinException($"Cant cast type '{ResolveExpressionType(exp).FullName}' to type '{to.TypeInfo.FullName}'", exp.SourceLocation);

            var temp_var = AddExpression(exp.SubUnaryExpression!);
            var type = temp_var.TypeInfo;
            if (type.IsInterfaceType)
            {
                AddInstruction(new CastInstruction(temp_var, to.TypeInfo, to));
            }
            else if (type is IClass cls)
            {
                if (to.TypeInfo.IsClassType)
                {
                    throw new BabyPenguinException($"Cant cast class type to class type", exp.SourceLocation);
                }
                else if (to.TypeInfo.IsInterfaceType)
                {
                    // class to interface conversion
                    if (!cls.ImplementedInterfaces.Any(i => i.FullName == to.TypeInfo.FullName))
                        throw new BabyPenguinException($"Cant cast class {cls.FullName} to interface '{to.TypeInfo.FullName}'", exp.SourceLocation);

                    AddInstruction(new CastInstruction(temp_var, to.TypeInfo, to));
                }
            }
            else
            {
                // TODO: deal with basic types
                AddInstruction(new CastInstruction(temp_var, to.TypeInfo, to));
            }

            return to;
        }

        public ISymbol AddExpression(ISyntaxExpression expression, ISymbol? to = null)
        {
            if (to != null)
            {
                var rightType = ResolveExpressionType(expression);
                if (!rightType.CanImplicitlyCastTo(to.TypeInfo))
                {
                    throw new BabyPenguinException($"Cant assign type '{rightType}' to type '{to.TypeInfo}'", expression.SourceLocation);
                }
            }
            else
            {
                to = AllocTempSymbol(ResolveExpressionType(expression), expression.SourceLocation);
            }

            switch (expression)
            {
                case Expression exp:
                    {
                        AddExpression(exp.SubExpression, to);
                    }
                    break;
                case LogicalOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.LogicalOr, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case LogicalAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.LogicalAnd, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case BitWiseOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseOr, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case BitwiseXorExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseXor, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case BitwiseAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.BitwiseAnd, a, b, res));
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case EqualityExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        if (exp.Operator != BinaryOperatorEnum.Is)
                        {
                            var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                            AddInstruction(new BinaryOperationInstruction(exp.Operator!.Value, tempVars[0], tempVars[1], to));
                        }
                        else
                        {
                            var leftVar = AddExpression(exp.SubExpressions[0]);
                            var rightVar = Model.ResolveSymbol(exp.SubExpressions[1].Text, s => s.IsEnum, scope: this) as EnumSymbol;
                            if (rightVar == null)
                                throw new BabyPenguinException($"Cant resolve enum symbol '{exp.SubExpressions[1].Text}'", exp.SubExpressions[1].SourceLocation);
                            var tempRightVar = AllocTempSymbol(BasicType.I32, exp.SourceLocation);
                            var tempLeftVar = AllocTempSymbol(BasicType.I32, exp.SourceLocation);
                            var enumValueSymbol = Model.ResolveSymbol(leftVar.TypeInfo.FullName + "._value");
                            if (enumValueSymbol == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{leftVar.TypeInfo.FullName}._value'", exp.SourceLocation);
                            AddInstruction(new ReadMemberInstruction(enumValueSymbol, leftVar, tempLeftVar));
                            AddInstruction(new AssignLiteralToSymbolInstruction(tempRightVar, BasicType.I32, rightVar.Value.ToString()));
                            AddInstruction(new BinaryOperationInstruction(BinaryOperatorEnum.Equal, tempLeftVar, tempRightVar, to));
                        }
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case RelationalExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case ShiftExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case AdditiveExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case MultiplicativeExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        this.AddInstruction(new AssignmentInstruction(resVar, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], to);
                    }
                    break;
                case CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        AddCastExpression(exp, to);
                    }
                    else
                    {
                        AddExpression(exp.SubUnaryExpression!, to);
                    }
                    break;
                case UnaryExpression exp:
                    if (exp.HasUnaryOperator)
                    {
                        var temp_var = AddExpression(exp.SubExpression);
                        AddInstruction(new UnaryOperationInstruction((UnaryOperatorEnum)exp.UnaryOperator!, temp_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpression, to);
                    }
                    break;
                case PostfixExpression exp:
                    switch (exp.PostfixExpressionType)
                    {
                        case PostfixExpression.Type.FunctionCall:
                            AddExpression(exp.SubFunctionCallExpression!, to);
                            break;
                        case PostfixExpression.Type.MemberAccess:
                            AddExpression(exp.SubMemberAccessExpression!, to);
                            break;
                        // case PostfixExpression.Type.Slicing:
                        //     AddExpression(exp.SubSlicingExpression!, to);
                        //     break;
                        case PostfixExpression.Type.PrimaryExpression:
                            AddExpression(exp.SubPrimaryExpression!, to);
                            break;
                        case PostfixExpression.Type.New:
                            AddExpression(exp.SubNewExpression!, to);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case FunctionCallExpression exp:
                    {
                        if (exp.IsMemberAccess)
                        {
                            var isStatic = CheckMemberAccessExpressionIsStatic(exp.MemberAccessExpression!);
                            var temp = AllocTempSymbol(ResolveExpressionType(exp.MemberAccessExpression!), expression.SourceLocation);
                            var func_var = AddMemberAccessExpression(exp.MemberAccessExpression!, temp, out ISymbol? owner_var);
                            if (!func_var.TypeInfo.IsFunctionType)
                                throw new BabyPenguinException($"Function call expects function symbol, but got '{func_var.TypeInfo}'", exp.MemberAccessExpression!.SourceLocation);
                            var param_vars = isStatic ? [] : new List<ISymbol> { owner_var! };
                            param_vars.AddRange(exp.ArgumentsExpression.Select(e => AddExpression(e)));
                            AddInstruction(new FunctionCallInstruction(func_var, param_vars, to));
                        }
                        else
                        {
                            var param_vars = exp.ArgumentsExpression.Select(e => AddExpression(e)).ToList();
                            ISymbol? fun_var;
                            if (exp.PrimaryExpression!.IsSimple)
                            {
                                fun_var = Model.ResolveShortSymbol(exp.PrimaryExpression!.Text, scopeDepth: exp.Scope.ScopeDepth, scope: this);
                                if (fun_var == null)
                                    throw new BabyPenguinException($"Cant resolve symbol '{exp.PrimaryExpression!.Text}'", exp.SourceLocation);
                            }
                            else
                            {
                                fun_var = AddExpression(exp.PrimaryExpression!);
                            }
                            if (!fun_var.TypeInfo.IsFunctionType)
                                throw new BabyPenguinException($"Function call expects function symbol, but got '{fun_var.TypeInfo}'", exp.PrimaryExpression!.SourceLocation);
                            AddInstruction(new FunctionCallInstruction(fun_var, param_vars, to));
                        }
                    }
                    break;
                case MemberAccessExpression exp:
                    {
                        AddMemberAccessExpression(exp, to, out ISymbol? owner_var);
                    }
                    break;
                case NewExpression exp:
                    {
                        AddInstruction(new NewInstanceInstruction(to));

                        string? enumOption = null;
                        var type = Model.ResolveType(exp.TypeSpecifier.Name, scope: this);
                        if (type == null)
                        {
                            // if new enum, remove last dot and try again
                            var parts = NameComponents.SplitStringPreservingAngleBrackets(exp.TypeSpecifier.Name, '.');
                            if (parts.Count > 1)
                            {
                                var typeStr = parts.Take(parts.Count - 1).Aggregate((a, b) => a + "." + b);
                                type = Model.ResolveType(typeStr, scope: this) as IEnum;
                                enumOption = parts.Last();
                            }
                        }

                        if (type == null)
                            throw new BabyPenguinException($"Cant resolve class '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);

                        if (type is IClass cls)
                        {
                            var constructorFunc = cls.Constructor;
                            if (constructorFunc == null)
                                throw new BabyPenguinException($"Cant find constructor of type '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);
                            var constructorSymbol = constructorFunc.FunctionSymbol;
                            if (constructorSymbol == null)
                                throw new BabyPenguinException($"Cant find constructor of type '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);
                            var paramVars = new List<ISymbol> { to };
                            paramVars.AddRange(exp.ArgumentsExpression.Select(e => AddExpression(e)));
                            AddInstruction(new FunctionCallInstruction(constructorSymbol, paramVars, null));
                        }
                        else if (type is IEnum enm)
                        {
                            var enumDecl = enm.EnumDeclarations.Find(i => i.Name == enumOption!) ??
                                throw new BabyPenguinException($"Cant find enum option '{enumOption}' in enum '{enm.FullName}'", exp.SourceLocation);

                            var tempValue = AllocTempSymbol(BasicType.I32, enumDecl.SourceLocation);
                            AddInstruction(new AssignLiteralToSymbolInstruction(tempValue, BasicType.I32, enumDecl.Value.ToString()));
                            AddInstruction(new WriteMemberInstruction(enm.ValueSymbol!, tempValue, to));
                            if (!enumDecl.TypeInfo.IsVoidType)
                            {
                                if (exp.ArgumentsExpression.Count != 1)
                                    throw new BabyPenguinException($"Enum '{enm.FullName}.{enumDecl.Name}' expects exactly 1 argument", exp.SourceLocation);
                                var enmValSymbol = AddExpression(exp.ArgumentsExpression[0]);
                                AddInstruction(new WriteEnumInstruction(enmValSymbol, to));
                            }
                            else
                            {
                                if (exp.ArgumentsExpression.Count != 0)
                                    throw new BabyPenguinException($"Enum '{enm.FullName}.{enumDecl.Name}' expects no argument", exp.SourceLocation);
                            }
                        }
                        else if (type is IInterface intf)
                        {
                            throw new BabyPenguinException($"Cant create instance of interface '{exp.TypeSpecifier.Name}'", exp.TypeSpecifier.SourceLocation);
                        }
                        else throw new NotImplementedException();
                    }
                    break;
                case PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case PrimaryExpression.Type.Identifier:
                            var symbol = Model.ResolveShortSymbol(exp.Identifier!.Name,
                                s => !s.IsClassMember, scopeDepth: exp.Scope.ScopeDepth, scope: this);
                            if (symbol == null) throw new BabyPenguinException($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else AddInstruction(new AssignmentInstruction(symbol, to));
                            break;
                        case PrimaryExpression.Type.Constant:
                            var t = BasicType.ResolveLiteralType(exp.Literal!);
                            if (t == null) throw new BabyPenguinException($"Cant resolve Type '{exp.Literal}'", exp.SourceLocation);
                            else AddInstruction(new AssignLiteralToSymbolInstruction(to, t, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.StringLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(to, BasicType.String, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.BoolLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(to, BasicType.Bool, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.ParenthesizedExpression:
                            AddExpression(exp.ParenthesizedExpression!, to);
                            break;
                        default:
                            break;
                    }
                    break;
            }
            return to;
        }

        public void AddInstruction(BabyPenguinIR instruction)
        {
            Instructions.Add(instruction);
        }
    }

    public class CodeGenerationPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            foreach (var obj in Model.FindAll(o => o is ICodeContainer || o is IType).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj is ICodeContainer container)
            {
                if (obj is ISemanticScope scp && scp.FindAncestorIncludingSelf(o => o is IType t && t.IsGeneric && !t.IsSpecialized) != null)
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Code generation pass for '{obj.FullName}' is skipped now because it is inside a generic type");
                }
                else
                {
                    Model.Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Generation code for '{obj.FullName}'...");
                    container.CompileSyntaxStatements();
                }
            }

            obj.PassIndex = PassIndex;
        }

        public string Report
        {
            get
            {
                StringBuilder sb = new();
                foreach (var obj in Model.FindAll(o => o is ICodeContainer))
                {
                    if (obj is ICodeContainer codeContainer && codeContainer.Instructions.Count > 0)
                    {
                        sb.AppendLine($"Compile Result For {obj.FullName}:");
                        sb.AppendLine(codeContainer.PrintInstructionsTable());
                    }
                }
                return sb.ToString();
            }
        }
    }
}