using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;
using BabyPenguin;
using System.Reflection.Metadata.Ecma335;

namespace BabyPenguin
{
    public class VirtualMachine
    {
        public VirtualMachine(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum))
            {
                Global.GlobalVariables.Add(symbol.FullName, new RuntimeVar(model, symbol.TypeInfo, symbol));
            }

            Global.ExternFunctions.Add("__builtin.print", (result, args) => { Output.Append(args[0].Value); Console.Write(args[0].Value); });
            Global.ExternFunctions.Add("__builtin.println", (result, args) => { Output.AppendLine(args[0].Value as string); Console.WriteLine(args[0].Value); });
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new RuntimeGlobal();

        public StringBuilder Output { get; } = new StringBuilder();

        public string CollectOutput() => Output.ToString();

        public void Run()
        {
            foreach (var ns in Model.Namespaces)
            {
                var frame = new RuntimeFrame(ns, Global, []);
                frame.Run();
            }

            foreach (var inital in Model.Namespaces.SelectMany(ns => ns.InitialRoutines))
            {
                var frame = new RuntimeFrame(inital, Global, []);
                frame.Run();
            }
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public Dictionary<string, RuntimeVar> GlobalVariables { get; } = [];

        public Dictionary<string, Action<RuntimeVar?, List<RuntimeVar>>> ExternFunctions { get; } = [];
        public bool EnableDebugPrint { get; set; } = false;
        public TextWriter DebugWriter { get; set; } = Console.Out;
    }

    public class RuntimeFrame
    {
        public RuntimeFrame(ICodeContainer container, RuntimeGlobal runtimeGlobal, List<RuntimeVar> parameters)
        {
            CodeContainer = container;
            Global = runtimeGlobal;
            foreach (var local in container.Symbols)
            {
                if (local.IsParameter)
                {
                    LocalVariables.Add(local.FullName, parameters[local.ParameterIndex]);
                }
                else
                {
                    LocalVariables.Add(local.FullName, new RuntimeVar(container.Model, local.TypeInfo, local));
                }
            }
        }

        public Dictionary<string, RuntimeVar> LocalVariables { get; } = [];
        public RuntimeGlobal Global { get; }
        public ICodeContainer CodeContainer { get; }

        private void DebugPrint(SemanticInstruction inst, params object[] args)
        {
            if (Global.EnableDebugPrint)
            {
                Global.DebugWriter.WriteLine(inst.ToString() + " " + string.Join(", ", args));
            }
        }

        public RuntimeVar Run()
        {
            RuntimeVar resolveVariable(ISymbol symbol)
            {
                RuntimeVar? result;
                if (symbol.IsLocal)
                {
                    if (!LocalVariables.TryGetValue(symbol.FullName, out result))
                    {
                        throw new BabyPenguinRuntimeException("Cannot find local variable " + symbol.FullName);
                    }
                }
                else
                {
                    if (!Global.GlobalVariables.TryGetValue(symbol.FullName, out result))
                    {
                        throw new BabyPenguinRuntimeException("Cannot find global variable " + symbol.FullName);
                    }
                }
                return result!;
            }

            int findLabel(string label)
            {
                for (int i = 0; i < CodeContainer.Instructions.Count; i++)
                {
                    if (CodeContainer.Instructions[i].Labels.Contains(label))
                    {
                        return i;
                    }
                }
                throw new BabyPenguinRuntimeException("Cannot find label " + label);
            }

            for (int i = 0; i < CodeContainer.Instructions.Count; i++)
            {
                var command = CodeContainer.Instructions[i];
                switch (command)
                {
                    case AssignmentInstruction cmd:
                        {
                            RuntimeVar rightVar = resolveVariable(cmd.RightHandSymbol);
                            RuntimeVar resultVar = resolveVariable(cmd.LeftHandSymbol);
                            DebugPrint(cmd, $"rightVar={rightVar}");
                            resultVar.AssignFrom(rightVar);
                            break;
                        }
                    case AssignLiteralToSymbolInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            DebugPrint(cmd);
                            switch (resultVar.Type)
                            {
                                case TypeEnum.Bool:
                                    resultVar.Value = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    resultVar.Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    resultVar.Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    resultVar.Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    resultVar.Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    resultVar.Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    resultVar.Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    resultVar.Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    resultVar.Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    resultVar.Value = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    resultVar.Value = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    resultVar.Value = null;
                                    break;
                                case TypeEnum.String:
                                    resultVar.Value = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    resultVar.Value = cmd.LiteralValue[0];
                                    break;
                                case TypeEnum.Fun:
                                    throw new BabyPenguinRuntimeException("A function cannot be a literal value");
                                case TypeEnum.Class:
                                    throw new BabyPenguinRuntimeException("A complex typecannot be a literal value");
                            }
                            break;
                        }
                    case FunctionCallInstruction cmd:
                        {
                            FunctionSymbol funVar = resolveVariable(cmd.FunctionSymbol).FunctionSymbol ??
                                throw new BabyPenguinRuntimeException("The symbol is not a function: " + cmd.FunctionSymbol.FullName);
                            RuntimeVar? retVar = cmd.Target == null ? null : resolveVariable(cmd.Target);
                            List<RuntimeVar> args = cmd.Arguments.Select(arg => arg.IsReadonly ? resolveVariable(arg).Clone() : resolveVariable(arg)).ToList();
                            DebugPrint(cmd, $"funVar={funVar}, args={string.Join(", ", args.Select(arg => arg.ToString()))}");
                            if (!funVar.SemanticFunction.IsExtern)
                            {
                                var newFrame = new RuntimeFrame(funVar.SemanticFunction, Global, args);
                                var resTemp = newFrame.Run();
                                retVar?.AssignFrom(resTemp);
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(funVar.FullName, out Action<RuntimeVar?, List<RuntimeVar>>? action))
                                {
                                    action(retVar, args);
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException("Cannot find external function " + funVar.FullName);
                                }
                            }
                            break;
                        }
                    // case SlicingInstruction cmd:
                    //     throw new NotImplementedException();
                    case CastInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            RuntimeVar rightVar = resolveVariable(cmd.Operand);
                            DebugPrint(cmd, $"rightVar={rightVar}");
                            if (resultVar.TypeInfo != cmd.TypeInfo)
                            {
                                throw new BabyPenguinRuntimeException($"Cannot assign type {cmd.TypeInfo} to type {resultVar.TypeInfo}");
                            }
                            switch (cmd.TypeInfo.Type)
                            {
                                case TypeEnum.Void:
                                    resultVar = RuntimeVar.Void();
                                    break;
                                case TypeEnum.U8:
                                    resultVar.Value = (byte)rightVar.Value!;
                                    break;
                                case TypeEnum.U16:
                                    resultVar.Value = (ushort)rightVar.Value!;
                                    break;
                                case TypeEnum.U32:
                                    resultVar.Value = (uint)rightVar.Value!;
                                    break;
                                case TypeEnum.U64:
                                    resultVar.Value = (ulong)rightVar.Value!;
                                    break;
                                case TypeEnum.I8:
                                    resultVar.Value = (sbyte)rightVar.Value!;
                                    break;
                                case TypeEnum.I16:
                                    resultVar.Value = (short)rightVar.Value!;
                                    break;
                                case TypeEnum.I32:
                                    resultVar.Value = (int)rightVar.Value!;
                                    break;
                                case TypeEnum.I64:
                                    resultVar.Value = (long)rightVar.Value!;
                                    break;
                                case TypeEnum.Float:
                                    resultVar.Value = (float)rightVar.Value!;
                                    break;
                                case TypeEnum.Double:
                                    resultVar.Value = (double)rightVar.Value!;
                                    break;
                                case TypeEnum.String:
                                    if (rightVar.TypeInfo.IsBoolType)
                                    {
                                        resultVar.Value = (bool)rightVar.Value! ? "true" : "false";
                                    }
                                    else
                                    {
                                        resultVar.Value = rightVar.Value!.ToString();
                                    }
                                    break;
                                case TypeEnum.Char:
                                    resultVar.Value = (char)rightVar.Value!;
                                    break;
                                case TypeEnum.Bool:
                                    resultVar.Value = (bool)rightVar.Value!;
                                    break;
                                case TypeEnum.Fun:
                                case TypeEnum.Class:
                                    throw new NotImplementedException();
                            }
                            break;
                        }
                    case UnaryOperationInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            RuntimeVar rightVar = resolveVariable(cmd.Operand);
                            DebugPrint(cmd, $"rightVar={rightVar}");
                            if (!rightVar.TypeInfo.CanImplicitlyCastTo(resultVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case UnaryOperatorEnum.Ref:
                                case UnaryOperatorEnum.Deref:
                                    throw new NotImplementedException();
                                case UnaryOperatorEnum.Plus:
                                    resultVar.Value = rightVar.Value;
                                    break;
                                case UnaryOperatorEnum.Minus:
                                    resultVar.Value = -(dynamic)rightVar.Value!;
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    resultVar.Value = ~(dynamic)rightVar.Value!;
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    resultVar.Value = !(dynamic)rightVar.Value!;
                                    break;
                            }
                            break;
                        }
                    case BinaryOperationInstruction cmd:
                        {
                            RuntimeVar leftVar = resolveVariable(cmd.LeftSymbol);
                            RuntimeVar rightVar = resolveVariable(cmd.RightSymbol);
                            DebugPrint(cmd, $"leftVar={leftVar}, rightVar={rightVar}");
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            if (!leftVar.TypeInfo.CanImplicitlyCastTo(rightVar.TypeInfo)
                                && !rightVar.TypeInfo.CanImplicitlyCastTo(leftVar.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Type {rightVar.TypeInfo} is not equal to type {leftVar.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case BinaryOperatorEnum.Add:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! + (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! - (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! * (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! / (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! % (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! & (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! | (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! ^ (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! && (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! || (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! == (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! != (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! > (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! >= (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! < (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!resultVar.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! <= (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! << (dynamic)rightVar.Value!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (TypeInfo.ImplictlyCastResult(leftVar.TypeInfo, rightVar.TypeInfo)?.CanImplicitlyCastTo(resultVar.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {rightVar.TypeInfo} to type {resultVar.TypeInfo}");
                                    resultVar.Value = (dynamic)leftVar.Value! >> (dynamic)rightVar.Value!;
                                    break;
                            }
                            break;
                        }
                    case GotoInstruction cmd:
                        {
                            if (cmd.Condition != null)
                            {
                                RuntimeVar condVar = resolveVariable(cmd.Condition);
                                DebugPrint(cmd, $"condVar={condVar}");
                                if (!condVar.TypeInfo.IsBoolType)
                                    throw new BabyPenguinRuntimeException($"Cannot use type {condVar.TypeInfo} as a condition");
                                if (cmd.JumpOnCondition != (bool)condVar.Value!)
                                {
                                    break;
                                }
                                else
                                {
                                    i = findLabel(cmd.TargetLabel);
                                }
                            }
                            else
                            {
                                i = findLabel(cmd.TargetLabel);
                            }
                            break;
                        }
                    case ReturnInstruction cmd:
                        {
                            if (cmd.RetValue != null)
                            {
                                RuntimeVar retVar = resolveVariable(cmd.RetValue);
                                return retVar;
                            }
                            break;
                        }
                    case NewInstanceInstruction cmd:
                        // do nothing
                        break;
                    case ReadMemberInstruction cmd:
                        {
                            RuntimeVar resultVar = resolveVariable(cmd.Target);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);
                            var members = (owner.Value as Dictionary<string, RuntimeVar>)!;
                            var memberVar = members[cmd.Member.Name]!;
                            DebugPrint(cmd, $"rightVar={memberVar}");
                            resultVar.AssignFrom(memberVar);
                        }
                        break;
                    case WriteMemberInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var owner = resolveVariable(cmd.MemberOwnerSymbol);
                            var members = (owner.Value as Dictionary<string, RuntimeVar>)!;
                            DebugPrint(cmd, $"rightVar={rightVar}");
                            members[cmd.Member.Name]!.AssignFrom(rightVar);
                        }
                        break;
                    case WriteEnumInstruction cmd:
                        {
                            var rightVar = resolveVariable(cmd.Value);
                            var enumVar = resolveVariable(cmd.TargetEnum);
                            DebugPrint(cmd, $"rightVar={rightVar}");
                            enumVar.EnumValue = rightVar;
                            break;
                        }
                    case ReadEnumInstruction cmd:
                        {
                            var resultVar = resolveVariable(cmd.TargetValue);
                            var enumVar = resolveVariable(cmd.Enum);
                            DebugPrint(cmd, $"enumVar={enumVar}");
                            if (enumVar.EnumValue == null)
                                throw new BabyPenguinRuntimeException($"Enum {enumVar.TypeInfo} has no value");
                            resultVar.AssignFrom(enumVar.EnumValue);
                            break;
                        }
                    case NopInstuction cmd:
                        DebugPrint(cmd);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            if (CodeContainer.ReturnTypeInfo.Type == TypeEnum.Void)
            {
                return RuntimeVar.Void();
            }
            else
            {
                throw new BabyPenguinRuntimeException("Function does not return a value");
            }
        }
    }

    public class RuntimeVar
    {
        public RuntimeVar(SemanticModel? model, TypeInfo typeInfo, ISymbol? symbol)
        {
            TypeInfo = typeInfo;
            Symbol = symbol;
            FunctionSymbol = symbol as FunctionSymbol;
            Type = typeInfo.Type;
            Model = model;
            switch (Type)
            {
                case TypeEnum.Bool:
                    Value = false;
                    break;
                case TypeEnum.U8:
                case TypeEnum.U16:
                case TypeEnum.U32:
                case TypeEnum.U64:
                case TypeEnum.I8:
                case TypeEnum.I16:
                case TypeEnum.I32:
                case TypeEnum.I64:
                    Value = 0;
                    break;
                case TypeEnum.Float:
                case TypeEnum.Double:
                    Value = 0.0;
                    break;
                case TypeEnum.String:
                    Value = "";
                    break;
                case TypeEnum.Char:
                    Value = '\0';
                    break;
                case TypeEnum.Void:
                    break;
                case TypeEnum.Fun:
                    break;
                case TypeEnum.Class:
                case TypeEnum.Enum:
                    {
                        var symbols = Model!.ResolveClassSymbols(typeInfo);
                        Value = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => new RuntimeVar(Model, s.TypeInfo, s));
                        break;
                    }
            }
        }

        public TypeInfo TypeInfo { get; }
        public TypeEnum Type { get; }
        public SemanticModel? Model { get; }
        public object? Value { get; set; }
        public RuntimeVar? EnumValue { get; set; }
        public ISymbol? Symbol { get; }
        public FunctionSymbol? FunctionSymbol { get; private set; }

        public void AssignFrom(RuntimeVar other)
        {
            if (!other.TypeInfo.IsEnumType && this.TypeInfo.IsEnumType)
                if (!other.TypeInfo.CanImplicitlyCastTo(this.TypeInfo))
                    throw new BabyPenguinRuntimeException($"Cannot assign type {other.Type} to type {Type}");
            Value = other.Value;
            EnumValue = other.EnumValue;
            FunctionSymbol = other.FunctionSymbol;
        }

        public static RuntimeVar Void()
        {
            return new RuntimeVar(null, TypeInfo.BuiltinTypes["void"], null);
        }

        public RuntimeVar Clone()
        {
            RuntimeVar result = new(Model, TypeInfo, Symbol)
            {
                Value = Value
            };
            return result;
        }

        public override string ToString()
        {
            return (Value?.ToString() ?? "null") + " (" + TypeInfo.ToString() + ")";
        }
    }
}