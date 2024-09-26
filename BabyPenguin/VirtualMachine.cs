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

            foreach (var symbol in model.Symbols)
            {
                Global.GlobalVariables.Add(symbol.FullName, new RuntimeVar(symbol.TypeInfo, symbol));
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

        public Dictionary<string, Action<RuntimeVar, List<RuntimeVar>>> ExternFunctions { get; } = [];
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
                    LocalVariables.Add(local.FullName, new RuntimeVar(local.TypeInfo, local));
                }
            }
        }

        public Dictionary<string, RuntimeVar> LocalVariables { get; } = [];
        public RuntimeGlobal Global { get; }

        public ICodeContainer CodeContainer { get; }

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

            foreach (var command in CodeContainer.Commands)
            {
                switch (command)
                {
                    case AssignmentCommand cmd:
                        {
                            RuntimeVar right_var = resolveVariable(cmd.RightHandSymbol);
                            RuntimeVar result_var = resolveVariable(cmd.LeftHandSymbol);
                            result_var.AssignFrom(right_var);
                            break;
                        }
                    case AssignLiteralToSymbolCommand cmd:
                        {
                            RuntimeVar result_var = resolveVariable(cmd.Target);
                            switch (result_var.Type)
                            {
                                case TypeEnum.Bool:
                                    result_var.Value = bool.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U8:
                                    result_var.Value = byte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U16:
                                    result_var.Value = ushort.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U32:
                                    result_var.Value = uint.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.U64:
                                    result_var.Value = ulong.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I8:
                                    result_var.Value = sbyte.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I16:
                                    result_var.Value = short.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I32:
                                    result_var.Value = int.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.I64:
                                    result_var.Value = long.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Float:
                                    result_var.Value = float.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Double:
                                    result_var.Value = double.Parse(cmd.LiteralValue);
                                    break;
                                case TypeEnum.Void:
                                    result_var.Value = null;
                                    break;
                                case TypeEnum.String:
                                    result_var.Value = cmd.LiteralValue[1..^1];
                                    break;
                                case TypeEnum.Char:
                                    result_var.Value = cmd.LiteralValue[0];
                                    break;
                                case TypeEnum.Fun:
                                    throw new BabyPenguinRuntimeException("A function cannot be a literal value");
                                case TypeEnum.Other:
                                    throw new BabyPenguinRuntimeException("A complex typecannot be a literal value");
                            }
                            break;
                        }
                    case FunctionCallCommand cmd:
                        {
                            FunctionSymbol fun_var = resolveVariable(cmd.FunctionSymbol).FunctionSymbol ??
                                throw new BabyPenguinRuntimeException("The symbol is not a function: " + cmd.FunctionSymbol.FullName);
                            RuntimeVar ret_var = resolveVariable(cmd.Target);
                            List<RuntimeVar> args = cmd.Arguments.Select(arg => arg.IsReadonly ? resolveVariable(arg).Clone() : resolveVariable(arg)).ToList();
                            if (!fun_var.SemanticFunction.IsExtern)
                            {
                                var new_frame = new RuntimeFrame(fun_var.SemanticFunction, Global, args);
                                ret_var.AssignFrom(new_frame.Run());
                            }
                            else
                            {
                                if (Global.ExternFunctions.TryGetValue(fun_var.FullName, out Action<RuntimeVar, List<RuntimeVar>>? action))
                                {
                                    action(ret_var, args);
                                }
                                else
                                {
                                    throw new BabyPenguinRuntimeException("Cannot find external function " + fun_var.FullName);
                                }
                            }
                            break;
                        }
                    case SlicingCommand cmd:
                        throw new NotImplementedException();
                    case CastCommand cmd:
                        {
                            RuntimeVar result_var = resolveVariable(cmd.Target);
                            RuntimeVar right_var = resolveVariable(cmd.Operand);
                            if (result_var.TypeInfo != cmd.TypeInfo)
                            {
                                throw new BabyPenguinRuntimeException($"Cannot assign type {cmd.TypeInfo} to type {result_var.TypeInfo}");
                            }
                            switch (cmd.TypeInfo.Type)
                            {
                                case TypeEnum.Void:
                                    result_var = RuntimeVar.Void();
                                    break;
                                case TypeEnum.U8:
                                    result_var.Value = (byte)right_var.Value!;
                                    break;
                                case TypeEnum.U16:
                                    result_var.Value = (ushort)right_var.Value!;
                                    break;
                                case TypeEnum.U32:
                                    result_var.Value = (uint)right_var.Value!;
                                    break;
                                case TypeEnum.U64:
                                    result_var.Value = (ulong)right_var.Value!;
                                    break;
                                case TypeEnum.I8:
                                    result_var.Value = (sbyte)right_var.Value!;
                                    break;
                                case TypeEnum.I16:
                                    result_var.Value = (short)right_var.Value!;
                                    break;
                                case TypeEnum.I32:
                                    result_var.Value = (int)right_var.Value!;
                                    break;
                                case TypeEnum.I64:
                                    result_var.Value = (long)right_var.Value!;
                                    break;
                                case TypeEnum.Float:
                                    result_var.Value = (float)right_var.Value!;
                                    break;
                                case TypeEnum.Double:
                                    result_var.Value = (double)right_var.Value!;
                                    break;
                                case TypeEnum.String:
                                    if (right_var.TypeInfo.IsBoolType)
                                    {
                                        result_var.Value = (bool)right_var.Value! ? "true" : "false";
                                    }
                                    else
                                    {
                                        result_var.Value = right_var.Value!.ToString();
                                    }
                                    break;
                                case TypeEnum.Char:
                                    result_var.Value = (char)right_var.Value!;
                                    break;
                                case TypeEnum.Bool:
                                    result_var.Value = (bool)right_var.Value!;
                                    break;
                                case TypeEnum.Fun:
                                case TypeEnum.Other:
                                    throw new NotImplementedException();
                            }
                            break;
                        }
                    case UnaryOperationCommand cmd:
                        {
                            RuntimeVar result_var = resolveVariable(cmd.Target);
                            RuntimeVar right_var = resolveVariable(cmd.Operand);
                            if (!right_var.TypeInfo.CanImplicitlyCastTo(result_var.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case UnaryOperatorEnum.Ref:
                                case UnaryOperatorEnum.Deref:
                                    throw new NotImplementedException();
                                case UnaryOperatorEnum.Plus:
                                    result_var.Value = right_var.Value;
                                    break;
                                case UnaryOperatorEnum.Minus:
                                    result_var.Value = -(dynamic)right_var.Value!;
                                    break;
                                case UnaryOperatorEnum.BitwiseNot:
                                    result_var.Value = ~(dynamic)right_var.Value!;
                                    break;
                                case UnaryOperatorEnum.LogicalNot:
                                    result_var.Value = !(dynamic)right_var.Value!;
                                    break;
                            }
                            break;
                        }
                    case BinaryOperationCommand cmd:
                        {
                            RuntimeVar left_var = resolveVariable(cmd.LeftSymbol);
                            RuntimeVar right_var = resolveVariable(cmd.RightSymbol);
                            RuntimeVar result_var = resolveVariable(cmd.Target);
                            if (!left_var.TypeInfo.CanImplicitlyCastTo(right_var.TypeInfo)
                                && !right_var.TypeInfo.CanImplicitlyCastTo(left_var.TypeInfo))
                                throw new BabyPenguinRuntimeException($"Type {right_var.TypeInfo} is not equal to type {left_var.TypeInfo}");
                            switch (cmd.Operator)
                            {
                                case BinaryOperatorEnum.Add:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! + (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.Subtract:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! - (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.Multiply:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! * (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.Divide:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! / (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.Modulo:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! % (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseAnd:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! & (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseOr:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! | (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.BitwiseXor:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! ^ (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalAnd:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! && (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.LogicalOr:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! || (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.Equal:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! == (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.NotEqual:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! != (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThan:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! > (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.GreaterThanOrEqual:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! >= (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThan:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! < (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.LessThanOrEqual:
                                    if (!result_var.TypeInfo.IsBoolType)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! <= (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.LeftShift:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! << (dynamic)right_var.Value!;
                                    break;
                                case BinaryOperatorEnum.RightShift:
                                    if (TypeInfo.ImplictlyCastResult(left_var.TypeInfo, right_var.TypeInfo)?.CanImplicitlyCastTo(result_var.TypeInfo) != true)
                                        throw new BabyPenguinRuntimeException($"Cannot assign type {right_var.TypeInfo} to type {result_var.TypeInfo}");
                                    result_var.Value = (dynamic)left_var.Value! >> (dynamic)right_var.Value!;
                                    break;
                            }
                            break;
                        }
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
                throw new NotImplementedException();
            }
        }
    }

    public class RuntimeVar
    {
        public RuntimeVar(TypeInfo typeInfo, ISymbol? symbol)
        {
            TypeInfo = typeInfo;
            Symbol = symbol;
            FunctionSymbol = symbol as FunctionSymbol;
            Type = typeInfo.Type;
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
                case TypeEnum.Other:
                    break;
            }
        }

        public TypeInfo TypeInfo { get; }
        public TypeEnum Type { get; }
        public object? Value { get; set; }
        public ISymbol? Symbol { get; }
        public FunctionSymbol? FunctionSymbol { get; private set; }

        public void AssignFrom(RuntimeVar other)
        {
            if (!other.TypeInfo.CanImplicitlyCastTo(this.TypeInfo))
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.Type} to type {Type}");
            Value = other.Value;
            FunctionSymbol = other.FunctionSymbol;
        }

        public static RuntimeVar Void()
        {
            return new RuntimeVar(TypeInfo.BuiltinTypes["void"], null);
        }

        public RuntimeVar Clone()
        {
            RuntimeVar result = new(TypeInfo, Symbol)
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