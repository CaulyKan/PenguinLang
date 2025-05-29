namespace BabyPenguin.SemanticInterface
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
        }

        public string PrintInstructionsTable()
        {
            var table = new ConsoleTable("Instruction", "OP1", "OP2", "Result", "Labels", "Location");
            foreach (var ins in Instructions)
            {
                table.AddRow(ins.StringCommand, ins.StringOP1, ins.StringOP2, ins.StringResult, ins.StringLabels, ins.SourceLocation);
            }
            return table.ToMarkDownString();
        }

        public void AddCodeBlockItem(CodeBlockItem item)
        {
            switch (item.Type)
            {
                case CodeBlockItem.CodeBlockItemType.Statement:
                    AddStatement(item.Statement!);
                    break;
                case CodeBlockItem.CodeBlockItemType.Declaration:
                    AddLocalDeclearation(item.Declaration!, null);
                    break;
                case CodeBlockItem.CodeBlockItemType.TypeReference:
                    // no need to emit instructions, do nothing
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public ISymbol AddLocalDeclearation(Declaration item, int? paramIndex)
        {
            var typeName = item.TypeSpecifier!.Name; // TODO: type inference
            var symbol = Model.ResolveShortSymbol(item.Name, scopeDepth: item.ScopeDepth, scope: this);
            if (symbol == null)
            {
                throw new BabyPenguinException($"Cant resolve symbol '{item.Name}'", item.SourceLocation);
            }
            if (item.InitializeExpression != null)
            {
                AddExpression(item.InitializeExpression, true, symbol);
            }
            return symbol;
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
                case Statement.Type.Empty:
                    break;
                case Statement.Type.AssignmentStatement:
                    {
                        var rightVar = AddExpression(item.AssignmentStatement!.RightHandSide!, false);
                        ISymbol? member = null;
                        ISymbol? target;
                        bool isMemberAccess = item.AssignmentStatement.LeftHandSide!.IsMemberAccess;
                        if (!isMemberAccess)
                        {
                            target = Model.ResolveShortSymbol(item.AssignmentStatement.LeftHandSide.Identifier!.Name,
                                s => !s.IsClassMember,
                                scopeDepth: item.ScopeDepth, scope: this);
                        }
                        else
                        {
                            var ma = item.AssignmentStatement.LeftHandSide.MemberAccess!;
                            if (ma.PrimaryExpression!.IsSimple)
                            {
                                target = Model.ResolveShortSymbol(ma.PrimaryExpression.Text,
                                    s => !s.IsClassMember, scopeDepth: item.ScopeDepth, scope: this);
                                if (target == null)
                                {
                                    throw new BabyPenguinException($"Cant resolve symbol '{ma.PrimaryExpression.Text}'", ma.PrimaryExpression.SourceLocation);
                                }
                            }
                            else
                            {
                                target = AddExpression(ma.PrimaryExpression, false);
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
                                        AddInstruction(new ReadEnumInstruction(item.AssignmentStatement.LeftHandSide.SourceLocation, target, temp));
                                        target = temp;
                                    }
                                    else
                                    {
                                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                                        AddInstruction(new ReadMemberInstruction(item.AssignmentStatement.LeftHandSide.SourceLocation, member, target, temp, member.IsFunction && !member.IsStatic));
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
                                {
                                    AddAssignmentExpression(new(rightVar), target, false, null, item.SourceLocation);
                                }
                                else
                                {
                                    var isInitial = this.Name == "new" && target.Name == "this";
                                    AddAssignmentExpression(new(rightVar), member!, isInitial, target, item.SourceLocation);
                                }
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
                                    AddInstruction(new BinaryOperationInstruction(item.SourceLocation, op, target, rightVar, temp));
                                    AddAssignmentExpression(new(temp), target, false, null, item.SourceLocation);
                                }
                                else
                                {
                                    var tempBeforeCalc = AddExpression(item.AssignmentStatement.LeftHandSide.MemberAccess!, false);
                                    var tempAfterCalc = AllocTempSymbol(ResolveBinaryOperationType(op, [tempBeforeCalc.TypeInfo, rightVar.TypeInfo], item.SourceLocation), item.SourceLocation);
                                    AddInstruction(new BinaryOperationInstruction(item.SourceLocation, op, tempBeforeCalc, rightVar, tempAfterCalc));
                                    AddInstruction(new WriteMemberInstruction(item.SourceLocation, member!, tempAfterCalc, target));
                                }
                                break;
                            }
                        }
                        break;
                    }
                case Statement.Type.ExpressionStatement:
                    {
                        AddExpression(item.ExpressionStatement!.Expression!, false);
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
                        var conditionVar = AddExpression(ifStatement.Condition!, false);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            throw new BabyPenguinException($"If condition must be bool type, but got '{conditionVar.TypeInfo}'", ifStatement.SourceLocation);
                        if (ifStatement.HasElse)
                        {
                            var elseLabel = CreateLabel();
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(ifStatement.Condition!.SourceLocation, elseLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement!);
                            AddInstruction(new GotoInstruction(ifStatement.Condition!.SourceLocation, endifLabel));
                            AddInstruction(new NopInstuction(ifStatement.ElseStatement!.SourceLocation).WithLabel(elseLabel));
                            AddStatement(ifStatement.ElseStatement!);
                            AddInstruction(new NopInstuction(ifStatement.SourceLocation.EndLocation).WithLabel(endifLabel));
                        }
                        else
                        {
                            var endifLabel = CreateLabel();
                            AddInstruction(new GotoInstruction(ifStatement.Condition!.SourceLocation, endifLabel, conditionVar, false));
                            AddStatement(ifStatement.MainStatement!);
                            AddInstruction(new NopInstuction(ifStatement.SourceLocation.EndLocation).WithLabel(endifLabel));
                        }
                        break;
                    }
                case Statement.Type.WhileStatement:
                    {
                        var whileStatement = item.WhileStatement!;
                        var beginLabel = CreateLabel();
                        AddInstruction(new NopInstuction(whileStatement.Condition!.SourceLocation).WithLabel(beginLabel));
                        var conditionVar = AddExpression(whileStatement.Condition!, false);
                        if (!conditionVar.TypeInfo.IsBoolType)
                            throw new BabyPenguinException($"While condition must be bool type, but got '{conditionVar.TypeInfo}'", whileStatement.SourceLocation);
                        var endLabel = CreateLabel();

                        CodeContainerData.CurrentWhileLoop.Push(new CurrentWhileLoopInfo(beginLabel, endLabel));

                        AddInstruction(new GotoInstruction(whileStatement.Condition!.SourceLocation, endLabel, conditionVar, false));
                        AddStatement(whileStatement.BodyStatement!);
                        AddInstruction(new GotoInstruction(whileStatement.Condition!.SourceLocation, beginLabel));

                        CodeContainerData.CurrentWhileLoop.Pop();

                        AddInstruction(new NopInstuction(whileStatement.SourceLocation.EndLocation).WithLabel(endLabel));
                        break;
                    }
                case Statement.Type.ForStatement:
                    {
                        var forStatement = item.ForStatement!;
                        var iteratorSymbol = AddExpression(forStatement.Expression!, false);
                        var iteratorSymbolType = iteratorSymbol.TypeInfo;

                        // expecting an iterator symbol
                        if (iteratorSymbolType.GenericArguments.FirstOrDefault() is IType iterType)
                        {
                            var iteratorType = Model.ResolveType($"__builtin.IIterator<{iterType.FullName}>");
                            if (iteratorSymbolType.FullName == iteratorType!.FullName)
                            {
                                // OK
                            }
                            else if (iteratorSymbolType.FullName != iteratorType!.FullName && iteratorSymbolType.CanImplicitlyCastTo(iteratorType))
                            {
                                var temp = AllocTempSymbol(iteratorType, forStatement.Declaration!.SourceLocation);
                                AddCastExpression(new(iteratorSymbol), temp, forStatement.Declaration!.SourceLocation);
                                iteratorSymbolType = iteratorType;
                                iteratorSymbol = temp;
                            }
                            else
                                throw new BabyPenguinException($"For loop requires an iterator of type __builtin.IIterator<?>, but got '{iteratorSymbol.TypeInfo}'", forStatement.SourceLocation);
                        }
                        else
                            throw new BabyPenguinException($"For loop requires an iterator of type __builtin.IIterator<?>, but got '{iteratorSymbol.TypeInfo}'", forStatement.SourceLocation);

                        var iterItemSymbol = AddLocalDeclearation(forStatement.Declaration!, null);

                        // prepare begin label
                        var beginLabel = CreateLabel();
                        var endLabel = CreateLabel();
                        AddInstruction(new NopInstuction(forStatement.Declaration!.SourceLocation).WithLabel(beginLabel));
                        CodeContainerData.CurrentWhileLoop.Push(new CurrentWhileLoopInfo(beginLabel, endLabel));

                        // call .next on IIterator 
                        var nextMethodSymbol = Model.ResolveSymbol(iteratorSymbol.TypeInfo.FullName + ".next", scope: this) as FunctionSymbol;
                        if (nextMethodSymbol == null)
                            throw new BabyPenguinException($"Can't resolve 'next' method of iterator type '{iteratorSymbol.TypeInfo.FullName}'", forStatement.SourceLocation);
                        var nextMethodImplSymbol = AllocTempSymbol(nextMethodSymbol.TypeInfo, forStatement.Expression!.SourceLocation);
                        AddInstruction(new ReadMemberInstruction(forStatement.Declaration!.SourceLocation, nextMethodSymbol, iteratorSymbol, nextMethodImplSymbol, false));

                        // .next result should be an Option
                        var nextResult = AllocTempSymbol(nextMethodSymbol.ReturnTypeInfo, forStatement.Expression.SourceLocation);
                        AddInstruction(new FunctionCallInstruction(forStatement.Declaration!.SourceLocation, nextMethodImplSymbol, [iteratorSymbol], nextResult));
                        var optionValueSymbol = Model.ResolveSymbol(nextResult.TypeInfo.FullName + "._value");
                        if (optionValueSymbol == null)
                            throw new BabyPenguinException($"Cant resolve symbol '{nextResult.TypeInfo.FullName}._value'", forStatement.Expression.SourceLocation);

                        // compare Option value is none
                        var tempRightVar = AllocTempSymbol(BasicType.I32, forStatement.Expression.SourceLocation);
                        var tempLeftVar = AllocTempSymbol(BasicType.I32, forStatement.Expression.SourceLocation);
                        AddInstruction(new ReadMemberInstruction(forStatement.Declaration!.SourceLocation, optionValueSymbol, nextResult, tempLeftVar, false));
                        var noneEnumValue = (nextResult.TypeInfo as IEnum)?.EnumDeclarations.Find(i => i.Name == "none")?.Value;
                        if (noneEnumValue == null)
                            throw new BabyPenguinException($"Can't resolve 'none' enum value of type '{nextResult.TypeInfo.FullName}'", forStatement.Expression.SourceLocation);
                        AddInstruction(new AssignLiteralToSymbolInstruction(forStatement.Declaration!.SourceLocation, tempRightVar, BasicType.I32, noneEnumValue.ToString()!));
                        var condVar = AllocTempSymbol(BasicType.Bool, forStatement.Expression.SourceLocation);
                        AddInstruction(new BinaryOperationInstruction(forStatement.Declaration!.SourceLocation, BinaryOperatorEnum.Equal, tempLeftVar, tempRightVar, condVar));

                        // loop body
                        AddInstruction(new GotoInstruction(forStatement.Declaration!.SourceLocation, endLabel, condVar, true));
                        AddInstruction(new ReadEnumInstruction(forStatement.Declaration!.SourceLocation, nextResult, iterItemSymbol));
                        AddStatement(forStatement.BodyStatement!);
                        AddInstruction(new GotoInstruction(forStatement.SourceLocation.EndLocation, beginLabel));

                        CodeContainerData.CurrentWhileLoop.Pop();
                        AddInstruction(new NopInstuction(forStatement.SourceLocation.EndLocation).WithLabel(endLabel));
                        break;
                    }
                case Statement.Type.ReturnStatement:
                    {
                        var returnStatement = item.ReturnStatement!;
                        ReturnStatus? returnStatus = returnStatement.ReturnType switch
                        {
                            ReturnStatement.ReturnTypeEnum.Normal => null,
                            ReturnStatement.ReturnTypeEnum.YieldNotFinished => ReturnStatus.YieldNotFinished,
                            ReturnStatement.ReturnTypeEnum.YieldFinished => ReturnStatus.YieldFinished,
                            ReturnStatement.ReturnTypeEnum.Blocked => ReturnStatus.Blocked,
                            _ => throw new NotImplementedException(),
                        };
                        if (returnStatement.ReturnExpression != null)
                        {
                            var returnVar = AddExpression(returnStatement.ReturnExpression, false);
                            if (returnVar.TypeInfo != ReturnTypeInfo)
                            {
                                if (returnVar.TypeInfo.CanImplicitlyCastTo(ReturnTypeInfo))
                                {
                                    var temp = AllocTempSymbol(ReturnTypeInfo, returnStatement.SourceLocation);
                                    AddAssignmentExpression(new(returnVar), temp, false, null, returnStatement.SourceLocation);
                                    returnVar = temp;
                                    AddInstruction(new ReturnInstruction(item.SourceLocation, returnVar, returnStatus ?? ReturnStatus.YieldFinished));
                                }
                                else if (returnVar.TypeInfo.IsVoidType && (this as IFunction)?.IsGenerator == true)
                                {
                                    // allow 'return;' to jump out generator functions
                                    AddInstruction(new ReturnInstruction(item.SourceLocation, null, returnStatus ?? ReturnStatus.Finished));
                                }
                                else
                                    throw new BabyPenguinException($"Return type mismatch, expected '{ReturnTypeInfo}' but got '{returnVar.TypeInfo}'", returnStatement.SourceLocation);
                            }
                            else
                            {
                                AddInstruction(new ReturnInstruction(item.SourceLocation, returnVar, returnStatus ?? ReturnStatus.YieldFinished));
                            }
                        }
                        else
                        {
                            AddInstruction(new ReturnInstruction(item.SourceLocation, null, returnStatus ?? ReturnStatus.Finished));
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
                            AddInstruction(new GotoInstruction(item.SourceLocation, currentWhileLoop.EndLabel));
                        }
                        else if (jumpStatement.JumpType == JumpStatement.Type.Continue)
                        {
                            if (CodeContainerData.CurrentWhileLoop.Count == 0)
                                throw new BabyPenguinException("Continue statement outside of while loop", jumpStatement.SourceLocation);
                            var currentWhileLoop = CodeContainerData.CurrentWhileLoop.Peek();
                            AddInstruction(new GotoInstruction(item.SourceLocation, currentWhileLoop.BeginLabel));
                        }
                        else
                            throw new NotImplementedException();
                        break;
                    }
                case Statement.Type.YieldStatement:
                    {
                        var yieldStatement = item.YieldStatement!;
                        if (yieldStatement.YieldExpression == null)
                        {
                            if (this.ReturnTypeInfo.IsVoidType)
                                AddInstruction(new ReturnInstruction(item.SourceLocation, null, ReturnStatus.YieldNotFinished));
                            else throw new BabyPenguinException($"Yield statement without an expression requires function return type to be void, but got '{ReturnTypeInfo}'", yieldStatement.SourceLocation);
                        }
                        else
                        {
                            var returnVar = AddExpression(yieldStatement.YieldExpression, false);
                            if (ReturnTypeInfo.GenericType?.FullName != "__builtin.IGenerator<?>")
                                throw new BabyPenguinException($"This yield statement requires function return type to be IGenerator<?>, but got '{ReturnTypeInfo}'", yieldStatement.SourceLocation);

                            var returnType = ReturnTypeInfo.GenericType.GenericArguments.First();
                            if (returnType == null)
                                throw new BabyPenguinException($"This yield statement requires function return type to be IGenerator<?>, but got '{ReturnTypeInfo}'", yieldStatement.SourceLocation);

                            if (returnVar.TypeInfo.CanImplicitlyCastTo(returnType))
                            {
                                var temp = AllocTempSymbol(returnType, yieldStatement.SourceLocation);
                                AddAssignmentExpression(new(returnVar), temp, false, null, yieldStatement.SourceLocation);
                                returnVar = temp;
                            }
                            else
                                throw new BabyPenguinException($"The function return type is {ReturnTypeInfo.FullName}, but yield returns '{returnVar.TypeInfo.FullName}'", yieldStatement.SourceLocation);
                            AddInstruction(new ReturnInstruction(item.SourceLocation, returnVar, ReturnStatus.YieldNotFinished));
                        }

                        break;
                    }
                case Statement.Type.SignalStatement:
                    {
                        var signalStatement = item.SignalStatement!;
                        var signalValue = AddExpression(signalStatement.SignalExpression!, false);
                        if (signalValue.TypeInfo.IsIntType)
                        {
                            AddInstruction(new SignalInstruction(item.SourceLocation, signalValue, null));
                        }
                        else
                        {
                            throw new BabyPenguinException($"Signal statement requires an integer type, but got '{signalValue.TypeInfo}'", signalStatement.SourceLocation);
                        }
                    }
                    break;
                case Statement.Type.EmitEventStatement:
                    {
                        var emitEventStatement = item.EmitEventStatement!;
                        if (emitEventStatement.EventExpression == null)
                            throw new BabyPenguinException($"Event expression is required", emitEventStatement.SourceLocation);
                        var eventSymbol = AddExpression(emitEventStatement.EventExpression, false);
                        var notifySymbol = Model.ResolveSymbol($"{eventSymbol.TypeInfo.FullName}.notify") ??
                            throw new BabyPenguinException($"Can't resolve 'notify' method of event type '{eventSymbol.TypeInfo.FullName}'", emitEventStatement.SourceLocation);
                        var paramSymbol = emitEventStatement.ArgumentExpression == null ?
                            AllocTempSymbol(BasicType.Void, emitEventStatement.SourceLocation) :
                            AddExpression(emitEventStatement.ArgumentExpression, false);

                        if (eventSymbol.TypeInfo.GenericType?.FullName != "__builtin.Event<?>")
                            throw new BabyPenguinException($"This emit event statement requires event type to be __builtin.Event<?>, but got '{eventSymbol.TypeInfo}'", emitEventStatement.EventExpression.SourceLocation);
                        if (paramSymbol.TypeInfo.FullName != eventSymbol.TypeInfo.GenericArguments.First().FullName)
                        {
                            var temp = AllocTempSymbol(eventSymbol.TypeInfo.GenericArguments.First(), emitEventStatement.SourceLocation);
                            AddCastExpression(new(paramSymbol), temp, emitEventStatement.SourceLocation);
                            paramSymbol = temp;
                        }

                        AddInstruction(new FunctionCallInstruction(emitEventStatement.SourceLocation, notifySymbol, [eventSymbol, paramSymbol], null));
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

        public void ResolveMemberAccessExpressionSymbol(MemberAccessExpression expression, out IType? ownerType, out ISymbol targetSymbol)
        {
            // global static variable
            if (Model.ResolveSymbol(expression.Text, s => s.IsStatic, scope: this) is ISymbol symbol)
            {
                targetSymbol = symbol;
                ownerType = null;
            }
            else
            {
                ISymbol? member = null;
                IType? t = null;
                for (int i = -1; i < expression.MemberIdentifiers.Count; i++)
                {
                    try
                    {
                        if (i == -1)
                        {
                            t = ResolveExpressionType(expression.PrimaryExpression!);
                            break;
                        }
                        else
                        {
                            t = Model.ResolveType(string.Join(".",
                                [expression.PrimaryExpression!.Text, .. expression.MemberIdentifiers.Select(i => i.Text).Take(i + 1)]), scope: this);
                            if (t != null) break;
                        }
                    }
                    catch (BabyPenguinException)
                    {
                        // ignore and try next
                    }
                }

                if (t == null)
                    throw new BabyPenguinException($"Cant resolve owner type of member access expression", expression.SourceLocation);

                if (t is TypeReferenceType typeReference)
                {
                    // find function symbol like 'Foo.foo'
                    var type = typeReference.TypeReference;
                    var ma = expression;
                    for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                    {
                        var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                        if (Model.ResolveType(t.FullName + "." + ma.MemberIdentifiers[i].Name) is IType newType)
                        {
                            type = newType;
                        }
                        else
                        {
                            var funcSymbol = Model.ResolveShortSymbol(ma.MemberIdentifiers[i].Name, s => s.IsFunction, scope: type as ISemanticScope);
                            if (funcSymbol != null && isLastRound)
                            {
                                ownerType = null;
                                targetSymbol = funcSymbol;
                                return;
                            }
                            else break;
                        }
                    }
                    throw new BabyPenguinException($"Since '{type}' is a type, '{expression.Text} is expected to be a function'");
                }
                else
                {
                    ownerType = t;
                    var ma = expression;
                    for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
                    {
                        var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                        member = Model.ResolveSymbol(ownerType.FullName + "." + ma.MemberIdentifiers[i].Name);

                        if (member == null && ownerType is IVTableContainer cls)
                        {
                            // try implicit conversion to interface
                            var candidates = cls.ImplementedInterfaces.Select(
                                intf => Model.ResolveShortSymbol(ma.MemberIdentifiers[i].Name, scope: intf)
                            ).Where(s => s != null).ToList();
                            if (candidates.Count > 1)
                                throw new BabyPenguinException($"Ambiguous interface method for {ma.MemberIdentifiers[i].Name}, please explicitly specify cast to interface", expression.SourceLocation);
                            else if (candidates.Count == 1)
                                member = candidates[0];
                        }

                        if (member == null)
                            throw new BabyPenguinException($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);

                        if (isLastRound)
                        {

                            break;
                        }
                        ownerType = member.TypeInfo;
                    }

                    targetSymbol = member ?? throw new BabyPenguinException($"Cant resolve symbol {expression}'", expression.SourceLocation);
                }
            }
        }

        public IType ResolveExpressionType(ISyntaxExpression expression)
        {

            switch (expression)
            {
                case Expression exp:
                    return ResolveExpressionType(exp.SubExpression!);
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
                                var type = ResolveExpressionType(exp.SubExpression!);
                                return type.Type switch
                                {
                                    TypeEnum.U8 => BasicType.I8,
                                    TypeEnum.U16 => BasicType.I16,
                                    TypeEnum.U32 => BasicType.I32,
                                    _ => type
                                };
                            case UnaryOperatorEnum.Plus:
                            case UnaryOperatorEnum.BitwiseNot:
                                return ResolveExpressionType(exp.SubExpression!);
                            case UnaryOperatorEnum.LogicalNot:
                                return BasicType.Bool;
                        }
                    }
                    else
                    {
                        return ResolveExpressionType(exp.SubExpression!);
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
                        case PostfixExpression.Type.Wait:
                            return ResolveExpressionType(exp.SubWaitExpression!);
                        case PostfixExpression.Type.SpawnAsync:
                            return ResolveExpressionType(exp.SubSpawnAsyncExpression!);
                    }
                    break;
                case FunctionCallExpression exp:
                    {
                        IType funcType;
                        // bool isInstanceCall = false;
                        if (exp.IsMemberAccess)
                        {
                            funcType = ResolveExpressionType(exp.MemberAccessExpression!);
                        }
                        else
                        {
                            funcType = ResolveExpressionType(exp.PrimaryExpression!);
                        }

                        return funcType.GenericArguments.First();
                    }
                case MemberAccessExpression exp:
                    {
                        // if (CheckMemberAccessExpressionIsStatic(exp))
                        // {
                        //     var symbol = Model.ResolveSymbol(exp.Text, s => s.IsStatic, scope: this);
                        //     if (symbol == null)
                        //         throw new BabyPenguinException($"Cant resolve symbol '{exp.Text}'", exp.SourceLocation);
                        //     return symbol.TypeInfo;
                        // }
                        // else
                        // {
                        //     ResolveMemberAccessExpressionSymbol(exp, out _, out ISymbol symbol);
                        //     return symbol.TypeInfo;
                        // }
                        ResolveMemberAccessExpressionSymbol(exp, out _, out ISymbol symbol);
                        if (symbol is FunctionSymbol fs && !fs.IsStatic)
                        {
                            // pack fat pointer, remove first argument
                            return (fs.IsAsync ? BasicType.AsyncFun : BasicType.Fun).Specialize([.. symbol.TypeInfo.GenericArguments.Take(1), .. symbol.TypeInfo.GenericArguments.Skip(2)]);
                        }
                        else
                            return symbol.TypeInfo;
                    }
                case PrimaryExpression exp:
                    switch (exp.PrimaryExpressionType)
                    {
                        case PrimaryExpression.Type.Identifier:
                            var symbol = Model.ResolveShortSymbol(exp.Identifier!.Name,
                                s => !s.IsClassMember, scopeDepth: exp.ScopeDepth, scope: this);
                            if (symbol != null) return symbol.TypeInfo;
                            else
                            {
                                var type = Model.ResolveType(exp.Identifier.Name, scope: this);
                                if (type != null) return new TypeReferenceType(type);
                            }
                            throw new BabyPenguinException($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
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
                        case PrimaryExpression.Type.VoidLiteral:
                            return BasicType.Void;
                        case PrimaryExpression.Type.ParenthesizedExpression:
                            return ResolveExpressionType(exp.ParenthesizedExpression!);
                        case PrimaryExpression.Type.LambdaFunction:
                            return ResolveExpressionType(exp.LambdaFunction!);
                    }
                    break;
                case NewExpression exp:
                    {
                        var res = Model.ResolveType(exp.TypeSpecifier!.Name, scope: this);
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
                case WaitExpression exp:
                    {
                        if (exp.Expression == null)
                        {
                            return BasicType.Void;
                        }
                        else
                        {
                            var waitExpType = ResolveExpressionType(exp.Expression);
                            if (waitExpType.FullName == "void") return BasicType.Void;

                            if (waitExpType.IsFutureType)
                            {
                                var futureType = waitExpType.GetImplementedInterfaceType("__builtin.IFuture<?>", exp.Expression.SourceLocation) ?? throw new BabyPenguinException($"Type '{waitExpType.FullName}' does not implement __builtin.IFuture<?> interface", exp.SourceLocation);

                                return futureType.GenericArguments.FirstOrDefault() ?? BasicType.Void;
                            }
                            else
                            {
                                return waitExpType;
                            }
                        }
                    }
                case SpawnAsyncExpression exp:
                    {
                        return ResolveSpawnAsyncExpressionType(exp);
                    }
                case LambdaFunctionExpression exp:
                    {
                        var returnType = Model.ResolveType(exp.ReturnType!.Name, scope: this) ?? throw new BabyPenguinException($"Cant resolve type '{exp.ReturnType.Name}'", exp.ReturnType.SourceLocation);
                        var parameters = exp.Parameters.Select(p => Model.ResolveType(p.Text, scope: this) ?? throw new BabyPenguinException($"Cant resolve type '{p.Text}'", p.SourceLocation)).ToList();
                        var funType = (exp.IsAsync ? BasicType.AsyncFun : BasicType.Fun).Specialize([returnType, .. parameters]);
                        return funType;
                    }
                default:
                    break;
            }
            throw new BabyPenguinException($"Unsupported expression type '{expression.GetType()}'", expression.SourceLocation);
        }

        IType ResolveSpawnAsyncExpressionType(SpawnAsyncExpression exp)
        {
            // TODO: change to lambda expression
            if (exp.Expression == null) throw new BabyPenguinException($"async expression must not be empty.");
            var funcReturnType = ResolveExpressionType(exp.Expression);
            IType futureType = funcReturnType;

            // generator?
            if (funcReturnType.GenericType?.FullName == "__builtin.IGenerator<?>" && funcReturnType.GenericArguments.FirstOrDefault() is IType argType)
                futureType = argType;

            return Model.ResolveType($"__builtin.IFuture<{futureType!.FullName}>")!;
        }


        public bool CheckMemberAccessExpressionIsStatic(MemberAccessExpression exp)
        {
            if (!exp.PrimaryExpression!.IsSimple)
                return false;

            var symbol = Model.ResolveShortSymbol(exp.PrimaryExpression.Text,
                s => !s.IsClassMember, scopeDepth: exp.ScopeDepth, scope: this);

            if (symbol == null)
            {
                symbol = Model.ResolveSymbol(exp.Text, s => s.IsStatic || s.IsEnum, scope: this);
                if (symbol == null)
                {
                    symbol = Model.ResolveSymbol(exp.Text, s => !s.IsStatic, scope: this);
                    if (symbol == null)
                        throw new BabyPenguinException($"Cant determine expression type for '{exp.Text}'", exp.SourceLocation);
                    else return false;
                }
                else
                    return true;
            }
            else
                return false;
        }

        public ISymbol SchedulerAddSimpleJob(ISymbol functionSymbol, SourceLocation sourceLocation, ISymbol targetSymbol)
        {
            if (targetSymbol.TypeInfo.GenericType?.FullName != "__builtin.IFuture<?>")
                throw new BabyPenguinException($"expecting symbol '{targetSymbol.FullName}' to be of type '__builtin.IFuture<?>'", sourceLocation);
            var futureResultType = targetSymbol.TypeInfo.GenericArguments.FirstOrDefault();
            if (futureResultType == null)
                throw new BabyPenguinException($"can't get generic argument of symbol '{targetSymbol.FullName}'", sourceLocation);
            if (functionSymbol is not FunctionSymbol && functionSymbol is not FunctionVariableSymbol)
                throw new BabyPenguinException($"expecting symbol '{functionSymbol.FullName}' to be a function symbol", sourceLocation);

            var DefaultRoutineType = Model.ResolveType($"__builtin._DefaultRoutine<{futureResultType.FullName}>") ?? throw new BabyPenguinException($"type '__builtin._DefaultRoutine<{futureResultType.FullName}>' is not found.");
            var DefaultRoutineConstructor = Model.ResolveSymbol($"__builtin._DefaultRoutine<{futureResultType.FullName}>.new") ?? throw new BabyPenguinException($"symbol '__builtin._DefaultRoutine<{futureResultType.FullName}>.new' is not found.");

            var trueSymbol = AllocTempSymbol(BasicType.Bool, sourceLocation);
            var routineSymbol = AllocTempSymbol(DefaultRoutineType, sourceLocation);
            AddInstruction(new AssignLiteralToSymbolInstruction(sourceLocation, trueSymbol, BasicType.Bool, "true"));
            AddInstruction(new NewInstanceInstruction(sourceLocation, routineSymbol));
            AddInstruction(new FunctionCallInstruction(sourceLocation, DefaultRoutineConstructor, [routineSymbol, functionSymbol, trueSymbol], null));
            AddInstruction(new CastInstruction(sourceLocation, routineSymbol, targetSymbol.TypeInfo, targetSymbol));
            return targetSymbol;
        }

        public ISymbol AddMemberAccessExpression(MemberAccessExpression exp, ISymbol to, out ISymbol? owner, out ISymbol? ownerBeforeImplicitConversion_)
        {
            IType? ownerType;
            ISymbol? ownerBeforeImplicitConversion = null;
            ISymbol symbol;

            ResolveMemberAccessExpressionSymbol(exp, out ownerType, out symbol);

            if (ownerType == null)
            {
                // static access
                owner = null;
                ownerBeforeImplicitConversion_ = null;
                AddAssignmentExpression(new(symbol), to, false, null, exp.SourceLocation);
                return to;
            }

            var ownerSymbol = AddExpression(exp.PrimaryExpression!, false);
            var ma = exp;
            ISymbol target = ownerSymbol;
            for (int i = 0; i < ma.MemberIdentifiers.Count; i++)
            {
                var isLastRound = i == ma.MemberIdentifiers.Count - 1;
                var symbolName = target.TypeInfo.FullName + "." + ma.MemberIdentifiers[i].Name;
                // var member = Model.ResolveSymbol(symbolName, s => s.IsEnum == target.TypeInfo.IsEnumType, scope: this);
                var member = Model.ResolveSymbol(symbolName, scope: this);

                if (member == null && target.TypeInfo is IVTableContainer cls)
                {
                    // try implicit conversion to interface
                    var implicitSymbol = cls.ImplementedInterfaces.Select(intf => Model.ResolveShortSymbol(ma.MemberIdentifiers[i].Name, scope: intf)).First(s => s != null);
                    var implicitSymbolType = implicitSymbol?.Parent as IType ?? throw new BabyPenguinException($"can't get parent of symbol {implicitSymbol?.FullName}");
                    var temp = AllocTempSymbol(implicitSymbolType, exp.SourceLocation);  // we are sure implicitSymbol exists in resolveExpressionType
                    AddCastExpression(new(target), temp, exp.SourceLocation);
                    member = implicitSymbol;
                    if (isLastRound)
                        ownerBeforeImplicitConversion = target;
                    target = temp;
                }

                if (member == null)
                    throw new BabyPenguinException($"Cant resolve symbol '{ma.MemberIdentifiers[i].Name}'", ma.MemberIdentifiers[i].SourceLocation);
                if (isLastRound)
                {
                    if (target.TypeInfo.IsEnumType && !member.IsFunction)
                        AddInstruction(new ReadEnumInstruction(exp.SourceLocation, target, to));
                    else
                        AddInstruction(new ReadMemberInstruction(exp.SourceLocation, member, target, to, member.IsFunction && !member.IsStatic));
                }
                else
                {
                    if (target.TypeInfo.IsEnumType && !member.IsFunction)
                    {
                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                        AddInstruction(new ReadEnumInstruction(exp.SourceLocation, target, temp));
                        target = temp;
                    }
                    else
                    {
                        var temp = AllocTempSymbol(member!.TypeInfo, member.SourceLocation);
                        AddInstruction(new ReadMemberInstruction(exp.SourceLocation, member, target, temp, member.IsFunction && !member.IsStatic));
                        target = temp;
                    }
                }
            }
            owner = target;
            ownerBeforeImplicitConversion_ = ownerBeforeImplicitConversion ?? owner;
            return to;
        }

        public void AddCastExpression(Or<CastExpression, ISymbol> from, ISymbol to, SourceLocation sourceLocation)
        {
            IType type;
            ISymbol tempSymbol;
            if (from.IsLeft)
            {
                var exp = from.Left!;
                if (ResolveExpressionType(exp).FullName != to.TypeInfo.FullName)
                    throw new BabyPenguinException($"Cant cast type '{ResolveExpressionType(exp).FullName}' to type '{to.TypeInfo.FullName}'", exp.SourceLocation);

                tempSymbol = AddExpression(exp.SubUnaryExpression!, false);
                type = tempSymbol.TypeInfo;
            }
            else
            {
                tempSymbol = from.Right!;
                type = tempSymbol.TypeInfo;
            }

            if (type.FullName == to.TypeInfo.FullName)
            {
                AddInstruction(new AssignmentInstruction(sourceLocation, tempSymbol, to));
                return;
            }

            if (type.IsInterfaceType)
            {
                AddInstruction(new CastInstruction(sourceLocation, tempSymbol, to.TypeInfo, to));
            }
            else if (type is IClass cls)
            {
                if (to.TypeInfo.IsClassType)
                {
                    throw new BabyPenguinException($"Cant cast class type to class type", sourceLocation);
                }
                else if (to.TypeInfo.IsInterfaceType)
                {
                    // class to interface conversion
                    if (!cls.ImplementedInterfaces.Any(i => i.FullName == to.TypeInfo.FullName))
                        throw new BabyPenguinException($"Cant cast class {cls.FullName} to interface '{to.TypeInfo.FullName}'", sourceLocation);

                    AddInstruction(new CastInstruction(sourceLocation, tempSymbol, to.TypeInfo, to));
                }
            }
            else
            {
                // TODO: deal with basic types
                AddInstruction(new CastInstruction(sourceLocation, tempSymbol, to.TypeInfo, to));
            }
        }

        public void AddAssignmentExpression(Or<ISyntaxExpression, ISymbol> from, ISymbol to, bool isInitial, ISymbol? memberOwner = null, SourceLocation? sourceLocation = null)
        {
            var fromSymbol = from.IsLeft ? AddExpression(from.Left!, false) : from.Right!;
            if (fromSymbol.TypeInfo.FullName != to.TypeInfo.FullName)
            {
                if (fromSymbol.TypeInfo.CanImplicitlyCastTo(to.TypeInfo))
                {
                    var temp = AllocTempSymbol(to.TypeInfo, sourceLocation ?? SourceLocation.Empty());
                    AddInstruction(new CastInstruction(sourceLocation ?? SourceLocation.Empty(), fromSymbol, to.TypeInfo, temp));
                    fromSymbol = temp;
                }
                else
                    throw new BabyPenguinException($"Cant assign type '{fromSymbol.TypeInfo.FullName}' to type '{to.TypeInfo.FullName}'", sourceLocation);
            }

            if (to.IsReadonly && !isInitial)
            {
                throw new BabyPenguinException($"Cant assign to readonly symbol '{to.FullName}'", sourceLocation);
            }
            else if (fromSymbol.IsReadonly != to.IsReadonly && !to.IsTemp)
            {
                if (fromSymbol.TypeInfo is IVTableContainer vc &&
                    vc.ImplementedInterfaces.FirstOrDefault(i => i.FullName == $"__builtin.ICopy<{to.TypeInfo.FullName}>") is IInterface ICopy)
                {
                    var temp = AllocTempSymbol(to.TypeInfo, sourceLocation ?? SourceLocation.Empty());
                    var copyFunc = Model.ResolveSymbol(ICopy.FullName + ".copy", s => s.IsFunction) ??
                        throw new BabyPenguinException($"Cant resolve function '{ICopy.FullName}.copy'", sourceLocation);
                    AddInstruction(new FunctionCallInstruction(sourceLocation ?? SourceLocation.Empty(), copyFunc, [fromSymbol], temp));
                    fromSymbol = temp;
                }
                else
                {
                    var fromStr = fromSymbol.IsReadonly ? "readonly" : "non-readonly";
                    var toStr = to.IsReadonly ? "readonly" : "non-readonly";
                    // TODO: change warning to error later
                    Reporter.Write(DiagnosticLevel.Warning, $"Cant assign {fromStr} symbol '{fromSymbol.FullName}' to {toStr} symbol '{to.FullName}'", sourceLocation ?? SourceLocation.Empty());
                    // throw new BabyPenguinException($"Cant assign {fromStr} symbol '{fromSymbol.FullName}' to {toStr} symbol '{to.FullName}'", sourceLocation);
                }
            }

            if (memberOwner != null)
            {
                AddInstruction(new WriteMemberInstruction(sourceLocation ?? SourceLocation.Empty(), to, fromSymbol, memberOwner));
            }
            else
            {
                AddInstruction(new AssignmentInstruction(sourceLocation ?? SourceLocation.Empty(), fromSymbol, to));
            }
        }

        public ISymbol AddExpression(ISyntaxExpression expression, bool isVariableInitializer, ISymbol? targetSymbol = null)
        {
            ISymbol to;
            if (targetSymbol != null)
            {
                var rightType = ResolveExpressionType(expression);
                if (rightType.FullName == targetSymbol.TypeInfo.FullName)
                {
                    to = targetSymbol;
                }
                else if (rightType.CanImplicitlyCastTo(targetSymbol.TypeInfo))
                {
                    to = AllocTempSymbol(ResolveExpressionType(expression), expression.SourceLocation);
                }
                else
                {
                    throw new BabyPenguinException($"Cant assign type '{rightType.FullName}' to type '{targetSymbol.TypeInfo.FullName}'", expression.SourceLocation);
                }
            }
            else
            {
                to = AllocTempSymbol(ResolveExpressionType(expression), expression.SourceLocation);
            }


            ISymbol ensureType(ISymbol a, IType type, SourceLocation sourceLocation)
            {
                if (type.FullName != a.TypeInfo.FullName)
                {
                    var temp = AllocTempSymbol(type, sourceLocation);
                    AddAssignmentExpression(new(a), temp, false, null, sourceLocation);
                    return temp;
                }
                else
                    return a;
            }

            List<ISymbol> convertParams(IType funcType, List<ISymbol> paramVars, SourceLocation sourceLocation)
            {
                if (!funcType.IsFunctionType)
                    throw new BabyPenguinException($"Function call expects function symbol, but got '{funcType}'", sourceLocation);

                if (funcType.GenericArguments.Count - 1 != paramVars.Count)
                    throw new BabyPenguinException($"Function call expects {funcType.GenericArguments.Count - 1} parameters, but got {paramVars.Count}", sourceLocation);

                return paramVars.Select((p, i) =>
                {
                    var expectedParamType = funcType.GenericArguments.ElementAt(i + 1);
                    if (p.TypeInfo.FullName != expectedParamType.FullName)
                    {
                        if (p.TypeInfo.CanImplicitlyCastTo(expectedParamType))
                        {
                            var casted = AllocTempSymbol(expectedParamType, sourceLocation);
                            AddInstruction(new CastInstruction(sourceLocation, p, expectedParamType, casted));
                            return casted;
                        }
                        else throw new BabyPenguinException($"Cant cast type '{p.TypeInfo}' to '{expectedParamType}'", sourceLocation);
                    }
                    else
                        return p;
                }).ToList();
            }

            switch (expression)
            {
                case Expression exp:
                    {
                        AddExpression(exp.SubExpression!, isVariableInitializer, to);
                    }
                    break;
                case LogicalOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.LogicalOr, a, b, res));
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case LogicalAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.LogicalAnd, a, b, res));
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case BitWiseOrExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.BitwiseOr, a, b, res));
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case BitwiseXorExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.BitwiseXor, a, b, res));
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case BitwiseAndExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.BitwiseAnd, a, b, res));
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case EqualityExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        if (exp.Operator != BinaryOperatorEnum.Is)
                        {
                            var type = ResolveExpressionType(exp.SubExpressions.First());
                            var a = ensureType(AddExpression(exp.SubExpressions[0], false), type, expression.SourceLocation);
                            var b = ensureType(AddExpression(exp.SubExpressions[1], false), type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, exp.Operator!.Value, a, b, to));
                        }
                        else
                        {
                            var leftVar = AddExpression(exp.SubExpressions[0], false);
                            var rightVar = Model.ResolveSymbol(exp.SubExpressions[1].Text, s => s.IsEnum, scope: this) as EnumSymbol;
                            if (rightVar == null)
                                throw new BabyPenguinException($"Cant resolve enum symbol '{exp.SubExpressions[1].Text}'", exp.SubExpressions[1].SourceLocation);
                            var tempRightVar = AllocTempSymbol(BasicType.I32, exp.SourceLocation);
                            var tempLeftVar = AllocTempSymbol(BasicType.I32, exp.SourceLocation);
                            var enumValueSymbol = Model.ResolveSymbol(leftVar.TypeInfo.FullName + "._value");
                            if (enumValueSymbol == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{leftVar.TypeInfo.FullName}._value'", exp.SourceLocation);
                            AddInstruction(new ReadMemberInstruction(exp.SourceLocation, enumValueSymbol, leftVar, tempLeftVar, false));
                            AddInstruction(new AssignLiteralToSymbolInstruction(exp.SourceLocation, tempRightVar, BasicType.I32, rightVar.Value.ToString()));
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, BinaryOperatorEnum.Equal, tempLeftVar, tempRightVar, to));
                        }
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case RelationalExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, tempVars.First().TypeInfo, expression.SourceLocation);
                            b = ensureType(b, tempVars.First().TypeInfo, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case ShiftExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case AdditiveExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case MultiplicativeExpression exp:
                    if (exp.SubExpressions.Count > 1)
                    {
                        var type = ResolveExpressionType(exp);
                        var tempVars = exp.SubExpressions.Select(e => AddExpression(e, false)).ToList();
                        var ops = exp.Operators.GetEnumerator(); ops.MoveNext();
                        var resVar = tempVars.Aggregate((a, b) =>
                        {
                            a = ensureType(a, type, expression.SourceLocation);
                            b = ensureType(b, type, expression.SourceLocation);
                            var res = AllocTempSymbol(type, expression.SourceLocation);
                            AddInstruction(new BinaryOperationInstruction(exp.SourceLocation, ops.Current, a, b, res));
                            ops.MoveNext();
                            return res;
                        });
                        AddAssignmentExpression(new(resVar), to, isVariableInitializer, null, expression.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubExpressions[0], isVariableInitializer, to);
                    }
                    break;
                case CastExpression exp:
                    if (exp.IsTypeCast)
                    {
                        AddCastExpression(exp, to, exp.SourceLocation);
                    }
                    else
                    {
                        AddExpression(exp.SubUnaryExpression!, isVariableInitializer, to);
                    }
                    break;
                case UnaryExpression exp:
                    if (exp.HasUnaryOperator)
                    {
                        var temp_var = AddExpression(exp.SubExpression!, false);
                        AddInstruction(new UnaryOperationInstruction(exp.SourceLocation, (UnaryOperatorEnum)exp.UnaryOperator!, temp_var, to));
                    }
                    else
                    {
                        AddExpression(exp.SubExpression!, isVariableInitializer, to);
                    }
                    break;
                case PostfixExpression exp:
                    switch (exp.PostfixExpressionType)
                    {
                        case PostfixExpression.Type.FunctionCall:
                            AddExpression(exp.SubFunctionCallExpression!, isVariableInitializer, to);
                            break;
                        case PostfixExpression.Type.MemberAccess:
                            AddExpression(exp.SubMemberAccessExpression!, isVariableInitializer, to);
                            break;
                        // case PostfixExpression.Type.Slicing:
                        //     AddExpression(exp.SubSlicingExpression!, to);
                        //     break;
                        case PostfixExpression.Type.PrimaryExpression:
                            AddExpression(exp.SubPrimaryExpression!, isVariableInitializer, to);
                            break;
                        case PostfixExpression.Type.New:
                            AddExpression(exp.SubNewExpression!, isVariableInitializer, to);
                            break;
                        case PostfixExpression.Type.Wait:
                            AddExpression(exp.SubWaitExpression!, isVariableInitializer, to);
                            break;
                        case PostfixExpression.Type.SpawnAsync:
                            AddExpression(exp.SubSpawnAsyncExpression!, isVariableInitializer, to);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case FunctionCallExpression exp:
                    {
                        if (exp.IsMemberAccess)
                        {
                            var temp = AllocTempSymbol(ResolveExpressionType(exp.MemberAccessExpression!), expression.SourceLocation);
                            var funcVar = AddMemberAccessExpression(exp.MemberAccessExpression!, temp, out _, out ISymbol? ownerVarBeforeImplicitConversion);
                            // var isStatic = ownerVarBeforeImplicitConversion == null;
                            // var paramVars = isStatic ? [] : new List<ISymbol> { ownerVarBeforeImplicitConversion! };
                            var paramVars = exp.ArgumentsExpression.Select(e => AddExpression(e, false)).ToList();
                            paramVars = convertParams(funcVar.TypeInfo, paramVars, exp.SourceLocation);
                            AddInstruction(new FunctionCallInstruction(exp.SourceLocation, funcVar, paramVars, to));
                        }
                        else
                        {
                            var paramVars = exp.ArgumentsExpression.Select(e => AddExpression(e, false)).ToList();
                            ISymbol? funVar;
                            if (exp.PrimaryExpression!.IsSimple)
                            {
                                funVar = Model.ResolveShortSymbol(exp.PrimaryExpression!.Text, scopeDepth: exp.ScopeDepth, scope: this);
                            }
                            else
                            {
                                funVar = AddExpression(exp.PrimaryExpression!, false);
                            }
                            if (funVar == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{exp.PrimaryExpression!.Text}'", exp.SourceLocation);
                            paramVars = convertParams(funVar.TypeInfo, paramVars, exp.SourceLocation);
                            if (!funVar.TypeInfo.IsFunctionType)
                                throw new BabyPenguinException($"Function call expects function symbol, but got '{funVar.TypeInfo}'", exp.PrimaryExpression!.SourceLocation);
                            AddInstruction(new FunctionCallInstruction(exp.SourceLocation, funVar, paramVars, to));
                        }
                    }
                    break;
                case MemberAccessExpression exp:
                    {
                        AddMemberAccessExpression(exp, to, out ISymbol? owner_var, out _);
                    }
                    break;
                case NewExpression exp:
                    {
                        AddInstruction(new NewInstanceInstruction(exp.SourceLocation, to));

                        string? enumOption = null;
                        var type = Model.ResolveType(exp.TypeSpecifier!.Name, scope: this);
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
                            paramVars.AddRange(exp.ArgumentsExpression.Select(e => AddExpression(e, false)));
                            paramVars = convertParams(constructorSymbol.TypeInfo, paramVars, exp.SourceLocation);
                            AddInstruction(new FunctionCallInstruction(exp.SourceLocation, constructorSymbol, paramVars, null));
                        }
                        else if (type is IEnum enm)
                        {
                            var enumDecl = enm.EnumDeclarations.Find(i => i.Name == enumOption!) ??
                                throw new BabyPenguinException($"Cant find enum option '{enumOption}' in enum '{enm.FullName}'", exp.SourceLocation);

                            var tempValue = AllocTempSymbol(BasicType.I32, enumDecl.SourceLocation);
                            AddInstruction(new AssignLiteralToSymbolInstruction(exp.SourceLocation, tempValue, BasicType.I32, enumDecl.Value.ToString()));
                            AddInstruction(new WriteMemberInstruction(exp.SourceLocation, enm.ValueSymbol!, tempValue, to));
                            if (!enumDecl.TypeInfo.IsVoidType)
                            {
                                if (exp.ArgumentsExpression.Count != 1)
                                    throw new BabyPenguinException($"Enum '{enm.FullName}.{enumDecl.Name}' expects exactly 1 argument", exp.SourceLocation);
                                var enmValSymbol = AddExpression(exp.ArgumentsExpression[0], false);
                                if (enumDecl.TypeInfo.FullName != enmValSymbol.TypeInfo.FullName)
                                {
                                    if (enmValSymbol.TypeInfo.CanImplicitlyCastTo(enumDecl.TypeInfo))
                                    {
                                        var temp = AllocTempSymbol(enumDecl.TypeInfo, exp.SourceLocation);
                                        AddInstruction(new CastInstruction(exp.SourceLocation, enmValSymbol, enumDecl.TypeInfo, temp));
                                        enmValSymbol = temp;
                                    }
                                    else throw new BabyPenguinException($"Cant cast type '{enmValSymbol.TypeInfo}' to '{enumDecl.TypeInfo}'", exp.SourceLocation);
                                }
                                AddInstruction(new WriteEnumInstruction(exp.SourceLocation, enmValSymbol, to));
                            }
                            else
                            {
                                if (exp.ArgumentsExpression.Count == 1 && ResolveExpressionType(exp.ArgumentsExpression[0]).IsVoidType)
                                {
                                    // ok if pass only one void to one void
                                    var voidSymbol = AllocTempSymbol(BasicType.Void, exp.ArgumentsExpression[0].SourceLocation);
                                    AddInstruction(new WriteEnumInstruction(exp.SourceLocation, voidSymbol, to));
                                }
                                else if (exp.ArgumentsExpression.Count != 0)
                                {
                                    throw new BabyPenguinException($"Enum '{enm.FullName}.{enumDecl.Name}' expects no argument", exp.SourceLocation);
                                }
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
                                s => !s.IsClassMember, scopeDepth: exp.ScopeDepth, scope: this);
                            if (symbol == null)
                                throw new BabyPenguinException($"Cant resolve symbol '{exp.Identifier!.Name}'", exp.SourceLocation);
                            else
                                AddAssignmentExpression(new(symbol), to, isVariableInitializer, null, expression.SourceLocation);
                            break;
                        case PrimaryExpression.Type.Constant:
                            var t = BasicType.ResolveLiteralType(exp.Literal!);
                            if (t == null) throw new BabyPenguinException($"Cant resolve Type '{exp.Literal}'", exp.SourceLocation);
                            else AddInstruction(new AssignLiteralToSymbolInstruction(exp.SourceLocation, to, t, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.StringLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(exp.SourceLocation, to, BasicType.String, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.BoolLiteral:
                            AddInstruction(new AssignLiteralToSymbolInstruction(exp.SourceLocation, to, BasicType.Bool, exp.Literal!));
                            break;
                        case PrimaryExpression.Type.ParenthesizedExpression:
                            AddExpression(exp.ParenthesizedExpression!, isVariableInitializer, to);
                            break;
                        default:
                            break;
                    }
                    break;
                case WaitExpression waitExpression:
                    {
                        if (waitExpression.Expression == null)
                        {
                            AddInstruction(new ReturnInstruction(waitExpression.SourceLocation, null, ReturnStatus.Blocked));
                        }
                        else
                        {
                            var waitExpType = ResolveExpressionType(waitExpression.Expression);
                            if (waitExpType.IsFutureType)
                            {
                                var waitExpressionSymbol = AddExpression(waitExpression.Expression, isVariableInitializer);
                                var futureType = waitExpType.GetImplementedInterfaceType("__builtin.IFuture<?>", waitExpression.Expression.SourceLocation) ?? throw new BabyPenguinException($"Type '{waitExpType.FullName}' does not implement __builtin.IFuture<?> interface", waitExpression.SourceLocation);
                                var futureSymbol = AllocTempSymbol(futureType, waitExpression.SourceLocation);
                                AddCastExpression(new(waitExpressionSymbol), futureSymbol, waitExpression.SourceLocation);
                                var doWaitFuncSymbol = Model.ResolveSymbol($"{futureType.FullName}.do_wait", i => i.IsFunction) ??
                                    throw new BabyPenguinException($"Cant find do_wait function for future type '{waitExpType.FullName}'", waitExpression.SourceLocation);
                                var doWaitSymbol = AllocTempSymbol(doWaitFuncSymbol.TypeInfo, waitExpression.SourceLocation);
                                AddInstruction(new ReadMemberInstruction(waitExpression.SourceLocation, doWaitFuncSymbol, futureSymbol, doWaitSymbol, true));
                                AddInstruction(new FunctionCallInstruction(waitExpression.SourceLocation, doWaitSymbol, [], to));
                            }
                            else
                            {
                                AddExpression(waitExpression.Expression, isVariableInitializer, to);
                            }
                        }
                    }
                    break;
                case SpawnAsyncExpression spawnAsyncExpression:
                    {
                        var exp = spawnAsyncExpression.Expression!;
                        //var spawnExpressionSymbol = AddExpression(exp, false);
                        // TODO: we need to convert expression into a annoymous function
                        var functionCallExpression = exp.GetEffectiveExpression() as FunctionCallExpression;
                        if (functionCallExpression == null)
                            throw new BabyPenguinException($"expecting spawn expression to be a function call", exp.SourceLocation);
                        var funcSymbol = functionCallExpression.IsMemberAccess ? AddExpression(functionCallExpression.MemberAccessExpression!, false) : AddExpression(functionCallExpression.PrimaryExpression!, false);
                        if (funcSymbol.TypeInfo.GenericArguments.Count > 1)
                            throw new NotImplementedException("should be rewrited to lambda expression with no arguments");
                        var type = ResolveSpawnAsyncExpressionType(spawnAsyncExpression);
                        var symbol = targetSymbol ?? AllocTempSymbol(type, spawnAsyncExpression.SourceLocation);
                        return SchedulerAddSimpleJob(funcSymbol, spawnAsyncExpression.SourceLocation, symbol);
                    }
                default:
                    throw new NotImplementedException($"unsupported expression: {expression}");
            }

            if (targetSymbol != null && to.TypeInfo.FullName != targetSymbol.TypeInfo.FullName)
            {
                // deal with implicity cast
                AddCastExpression(new(to), targetSymbol, expression.SourceLocation);
            }
            return to;


        }

        public void AddInstruction(BabyPenguinIR instruction)
        {
            Instructions.Add(instruction);
        }
    }

}