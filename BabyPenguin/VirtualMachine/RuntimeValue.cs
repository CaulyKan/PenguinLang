using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace BabyPenguin.VirtualMachine
{
    public interface IRuntimeValue
    {
        IType TypeInfo { get; }

        T As<T>() where T : class, IRuntimeValue => this as T ?? throw new BabyPenguinRuntimeException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

        IRuntimeValue Clone();
        IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited);
    }

    public class NotInitializedRuntimeValue : IRuntimeValue
    {
        public NotInitializedRuntimeValue(IType typeInfo)
        {
            TypeInfo = typeInfo;
        }

        public IType TypeInfo { get; }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            return new NotInitializedRuntimeValue(TypeInfo);
        }
    }

    public class BasicRuntimeValue : IRuntimeValue
    {
        public BasicRuntimeValue(IType typeInfo)
        {
            TypeInfo = typeInfo;
        }

        public IType TypeInfo { get; }

        public bool BoolValue;
        public byte U8Value;
        public ushort U16Value;
        public uint U32Value;
        public ulong U64Value;
        public sbyte I8Value;
        public short I16Value;
        public int I32Value;
        public long I64Value;
        public float FloatValue;
        public double DoubleValue;
        public string StringValue = "";
        public char CharValue;

        public dynamic? DynamicValue
        {
            get
            {
                return TypeInfo.Type switch
                {
                    TypeEnum.Bool => BoolValue,
                    TypeEnum.U8 => U8Value,
                    TypeEnum.U16 => U16Value,
                    TypeEnum.U32 => U32Value,
                    TypeEnum.U64 => U64Value,
                    TypeEnum.I8 => I8Value,
                    TypeEnum.I16 => I16Value,
                    TypeEnum.I32 => I32Value,
                    TypeEnum.I64 => I64Value,
                    TypeEnum.Float => FloatValue,
                    TypeEnum.Double => DoubleValue,
                    TypeEnum.String => StringValue,
                    TypeEnum.Char => CharValue,
                    TypeEnum.Void => null,
                    _ => null
                };
            }
            set
            {
                switch (TypeInfo.Type)
                {
                    case TypeEnum.Bool:
                        BoolValue = (bool)value;
                        break;
                    case TypeEnum.U8:
                        U8Value = (byte)value;
                        break;
                    case TypeEnum.U16:
                        U16Value = (ushort)value;
                        break;
                    case TypeEnum.U32:
                        U32Value = (uint)value;
                        break;
                    case TypeEnum.U64:
                        U64Value = (ulong)value;
                        break;
                    case TypeEnum.I8:
                        I8Value = (sbyte)value;
                        break;
                    case TypeEnum.I16:
                        I16Value = (short)value;
                        break;
                    case TypeEnum.I32:
                        I32Value = (int)value;
                        break;
                    case TypeEnum.I64:
                        I64Value = (long)value;
                        break;
                    case TypeEnum.Float:
                        FloatValue = (float)value;
                        break;
                    case TypeEnum.Double:
                        DoubleValue = (double)value;
                        break;
                    case TypeEnum.String:
                        StringValue = value as string ?? "";
                        break;
                    case TypeEnum.Char:
                        CharValue = (char)value;
                        break;
                    case TypeEnum.Void:
                        break;
                    default:
                        throw new BabyPenguinRuntimeException($"Cannot assign value of type {value?.GetType()} to type {TypeInfo}");
                }
            }
        }

        public void AssignFrom(BasicRuntimeValue otherVar)
        {
            BoolValue = otherVar.BoolValue;
            U8Value = otherVar.U8Value;
            U16Value = otherVar.U16Value;
            U32Value = otherVar.U32Value;
            U64Value = otherVar.U64Value;
            I8Value = otherVar.I8Value;
            I16Value = otherVar.I16Value;
            I32Value = otherVar.I32Value;
            I64Value = otherVar.I64Value;
            FloatValue = otherVar.FloatValue;
            DoubleValue = otherVar.DoubleValue;
            StringValue = otherVar.StringValue;
            CharValue = otherVar.CharValue;
        }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            var result = new BasicRuntimeValue(TypeInfo);
            result.AssignFrom(this);
            return result;
        }

        public override string ToString()
        {
            var s = TypeInfo.Type switch
            {
                TypeEnum.Bool => BoolValue.ToString(),
                TypeEnum.U8 => U8Value.ToString(),
                TypeEnum.U16 => U16Value.ToString(),
                TypeEnum.U32 => U32Value.ToString(),
                TypeEnum.U64 => U64Value.ToString(),
                TypeEnum.I8 => I8Value.ToString(),
                TypeEnum.I16 => I16Value.ToString(),
                TypeEnum.I32 => I32Value.ToString(),
                TypeEnum.I64 => I64Value.ToString(),
                TypeEnum.Float => FloatValue.ToString(),
                TypeEnum.Double => DoubleValue.ToString(),
                TypeEnum.String => "\"" + StringValue.ToString() + "\"",
                TypeEnum.Char => "'" + CharValue.ToString() + "'",
                TypeEnum.Void => "void",
                _ => "unknown"
            };
            return s;
        }
    }

    public class FunctionRuntimeValue : IRuntimeValue
    {
        public FunctionRuntimeValue(IType typeInfo, ISymbol funcSymbol, IRuntimeValue? owner_ = null)
        {
            Model = typeInfo.Model;
            TypeInfo = typeInfo;
            FunctionSymbol = funcSymbol;
            owner = owner_ ?? new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));
            if (funcSymbol is not Symbol.FunctionSymbol)
                throw new BabyPenguinRuntimeException($"Cannot create FunctionRuntimeValue with symbol of type {funcSymbol.GetType().Name}");
        }

        public IType TypeInfo { get; }

        public ISymbol FunctionSymbol { get; set; }

        public bool IsStatic => FunctionSymbol.IsStatic;

        public SemanticModel Model { get; }

        private IRuntimeValue owner;
        public IRuntimeValue Owner
        {
            get { return owner; }
            set
            {
                if (!FunctionSymbol.IsStatic)
                    owner = value;
                else
                    owner = new NotInitializedRuntimeValue(Model.BasicTypeNodes.Void.ToType(Mutability.Immutable));
            }
        }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            return new FunctionRuntimeValue(TypeInfo, FunctionSymbol, Owner);
        }

        public override string? ToString()
        {
            return FunctionSymbol.Name;
        }
    }

    public class ExternRuntimeValue(SemanticModel model) : IRuntimeValue
    {
        public IType TypeInfo => model.BasicTypeNodes.Void.ToType(Mutability.Immutable);

        public object? Object { get; set; }

        public override string ToString()
        {
            return Object == null ? "null" : RuntimeHelpers.GetHashCode(Object).ToString();
        }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            return new ExternRuntimeValue(model) { Object = Object };
        }
    }

    public class ReferenceRuntimeValue : IRuntimeValue
    {
        private readonly RuntimeGlobal? _global;

        public ReferenceRuntimeValue(IType typeInfo, Dictionary<string, IRuntimeValue> fields, RuntimeGlobal? global = null)
        {
            _global = global;
            RefId = global?.NextRefId() ?? (ulong)Random.Shared.NextInt64();
            _global?.AllObjects.TryAdd(RefId, this);
            TypeInfo = typeInfo;
            Fields = fields;
        }

        public IType TypeInfo { get; }

        public Dictionary<string, IRuntimeValue> Fields { get; } = [];

        public ulong RefId { get; }

        public object? ExternImplenmentationValue
        {
            get
            {
                if (Fields.TryGetValue("__extern_impl", out IRuntimeValue? result)) return (result as ExternRuntimeValue)!.Object;
                else return null;
            }
            set
            {
                Fields["__extern_impl"] = new ExternRuntimeValue(TypeInfo.Model) { Object = value };
            }
        }

        public override string ToString()
        {
            return ToString(0);
        }

        public string ToString(int depth)
        {
            if (depth > 5) return RefId.ToString() + "@{...}";
            var fields = Fields.Where(kvp => kvp.Value is not FunctionRuntimeValue).Select(kvp =>
            {
                var valStr = kvp.Value is ReferenceRuntimeValue rv ? rv.ToString(depth + 1)
                    : kvp.Value is EnumRuntimeValue ev ? ev.ToString(depth + 1)
                    : kvp.Value.ToString();
                return kvp.Key + ": " + valStr;
            }).ToList();
            return RefId.ToString() + "@{" + string.Join(", ", fields) + "}";
        }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            if (visited.TryGetValue(RefId, out var existing))
                return existing;
            var result = new ReferenceRuntimeValue(TypeInfo, [], _global);
            visited[RefId] = result;
            foreach (var kvp in Fields)
                result.Fields[kvp.Key] = kvp.Value.Clone(visited);
            result.ExternImplenmentationValue = ExternImplenmentationValue;
            return result;
        }
    }

    public class EnumRuntimeValue : IRuntimeValue
    {
        public EnumRuntimeValue(IType typeInfo, ReferenceRuntimeValue fields, IRuntimeValue? value)
        {
            TypeInfo = typeInfo;
            FieldsValue = fields;
            ContainingValue = value;
        }

        public IType TypeInfo { get; }

        public ReferenceRuntimeValue FieldsValue { get; set; }

        public IRuntimeValue? ContainingValue { get; set; }

        public override string ToString()
        {
            return ToString(0);
        }

        public string ToString(int depth)
        {
            var enumValue = FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value;
            var enumName = (TypeInfo.TypeNode as IEnumNode)?.EnumDeclarations.Find(e => e.Value == enumValue);
            var name = enumName?.Name ?? "?invalid?";
            if (ContainingValue is null) return name;
            if (depth > 5) return $"{name}(...)";
            var valStr = ContainingValue is ReferenceRuntimeValue rv ? rv.ToString(depth + 1)
                : ContainingValue is EnumRuntimeValue ev ? ev.ToString(depth + 1)
                : ContainingValue.ToString();
            return $"{name}({valStr})";
        }

        public void AssignFrom(EnumRuntimeValue otherVar)
        {
            ContainingValue = otherVar.ContainingValue;
            FieldsValue = (otherVar.FieldsValue.Clone() as ReferenceRuntimeValue)!;
        }

        public IRuntimeValue Clone() => Clone([]);

        public IRuntimeValue Clone(Dictionary<ulong, ReferenceRuntimeValue> visited)
        {
            var result = new EnumRuntimeValue(TypeInfo, (FieldsValue.Clone(visited) as ReferenceRuntimeValue)!, ContainingValue?.Clone(visited));
            return result;
        }
    }
}