using System.Collections;

namespace BabyPenguin.VirtualMachine
{
    public interface IRuntimeValue
    {
        IType TypeInfo { get; }

        T As<T>() where T : class, IRuntimeValue => this as T ?? throw new BabyPenguinRuntimeException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

        IRuntimeValue Clone();
    }

    public class NotInitializedRuntimeValue : IRuntimeValue
    {
        public NotInitializedRuntimeValue(IType typeInfo)
        {
            TypeInfo = typeInfo;
        }

        public IType TypeInfo { get; }

        public IRuntimeValue Clone()
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
            ExternImplenmentationValue = otherVar.ExternImplenmentationValue;
        }

        public IRuntimeValue Clone()
        {
            var result = new BasicRuntimeValue(TypeInfo);
            result.AssignFrom(this);
            return result;
        }

        public object? ExternImplenmentationValue { get; set; } = null;

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

            if (this.ExternImplenmentationValue != null)
            {
                // if (this.ExternImplenmentationValue is ICollection enumerable)
                // {
                //     s += " (extern: [";
                //     s += string.Join(", ", enumerable.Cast<object>().Select(o => o.ToString()));
                //     s += "])";
                // }
                // else
                //     s += " (extern: " + ExternImplenmentationValue.ToString() + ")";
            }

            return s;
        }
    }

    public class FunctionRuntimeValue : IRuntimeValue
    {
        public FunctionRuntimeValue(IType typeInfo, ISymbol funcSymbol, IRuntimeValue? owner = null)
        {
            TypeInfo = typeInfo;
            FunctionSymbol = funcSymbol;
            Owner = owner ?? new NotInitializedRuntimeValue(BasicType.Void);
            if (funcSymbol is not Symbol.FunctionSymbol)
                throw new BabyPenguinRuntimeException($"Cannot create FunctionRuntimeValue with symbol of type {funcSymbol.GetType().Name}");
        }

        public IType TypeInfo { get; }

        public ISymbol FunctionSymbol { get; set; }

        public IRuntimeValue Owner { get; set; }

        public IRuntimeValue Clone()
        {
            return new FunctionRuntimeValue(TypeInfo, FunctionSymbol, Owner);
        }

        public override string? ToString()
        {
            return FunctionSymbol.Name;
        }
    }

    public class ReferenceRuntimeValue : IRuntimeValue
    {
        public ReferenceRuntimeValue(IType typeInfo, Dictionary<string, IRuntimeValue> fields)
        {
            RefId = Interlocked.Increment(ref counter);
            AllObjects.Add(RefId, this);
            TypeInfo = typeInfo;
            Fields = fields;
        }

        public IType TypeInfo { get; }

        public Dictionary<string, IRuntimeValue> Fields { get; } = [];

        public ulong RefId { get; }

        public static Dictionary<ulong, ReferenceRuntimeValue> AllObjects { get; } = [];

        private static ulong counter = 0;

        public override string ToString()
        {
            var fields = Fields.Select(kvp => kvp.Key + ": " + kvp.Value.ToString());
            return "{" + string.Join(", ", fields) + "}";
        }

        public IRuntimeValue Clone()
        {
            var result = new ReferenceRuntimeValue(TypeInfo, Fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()));
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
            var enumValue = FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value;
            var enumName = (TypeInfo as IEnum)?.EnumDeclarations.Find(e => e.Value == enumValue);
            var name = enumName?.Name ?? "?invalid?";
            return ContainingValue is null ? name : $"{name}({ContainingValue})";
        }

        public void AssignFrom(EnumRuntimeValue otherVar)
        {
            ContainingValue = otherVar.ContainingValue;
            FieldsValue = (otherVar.FieldsValue.Clone() as ReferenceRuntimeValue)!;
        }

        public IRuntimeValue Clone()
        {
            var result = new EnumRuntimeValue(TypeInfo, (FieldsValue.Clone() as ReferenceRuntimeValue)!, ContainingValue?.Clone());
            return result;
        }
    }
}