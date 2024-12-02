namespace BabyPenguin.VirtualMachine
{
    public interface IRuntimeVar
    {
        SemanticModel Model { get; }

        IType TypeInfo { get; }

        TypeEnum Type => TypeInfo.Type;

        ISymbol Symbol { get; }

        void AssignFrom(IRuntimeVar other);

        IRuntimeVar Clone();

        T As<T>() where T : class, IRuntimeVar => this as T ?? throw new BabyPenguinRuntimeException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

        string? ValueToString => TypeInfo.FullName;

        string ToDebugString() => $"[ {Symbol?.Name}({TypeInfo.FullName}) = {ValueToString} ]";

        static IRuntimeVar FromSymbol(SemanticModel model, ISymbol symbol)
        {
            if (symbol.TypeInfo.IsEnumType)
            {
                return new EnumRuntimeVar(model, symbol);
            }
            else if (symbol.TypeInfo.IsFunctionType)
            {
                return new FunctionRuntimeVar(model, symbol);
            }
            else if (symbol.TypeInfo.IsClassType)
            {
                return new ClassRuntimeVar(model, symbol);
            }
            else if (symbol.TypeInfo.IsInterfaceType)
            {
                return new InterfaceRuntimeVar(model, symbol);
            }
            else
            {
                return new BasicRuntimeVar(model, symbol);
            }
        }
    }

    public class BasicRuntimeVar : IRuntimeVar
    {
        public BasicRuntimeVar(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;
        }

        public ISymbol Symbol { get; }

        public SemanticModel Model { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public TypeEnum Type => TypeInfo.Type;

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
                return Type switch
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
                switch (Type)
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
                        throw new BabyPenguinRuntimeException($"Cannot assign value of type {value?.GetType()} to type {Type}");
                }
            }
        }

        public object ExternImplenmentationValue { get; set; } = new object();

        public string? ValueToString => Type switch
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

        public override string ToString() => (this as IRuntimeVar).ToDebugString();

        public void AssignFrom(IRuntimeVar other)
        {
            if (other is BasicRuntimeVar otherVar)
            {
                if (!other.TypeInfo.CanImplicitlyCastTo(TypeInfo))
                    throw new BabyPenguinRuntimeException($"Cannot implicitly assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
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
            else
            {
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
            }
        }

        public IRuntimeVar Clone()
        {
            var result = new BasicRuntimeVar(Model, Symbol);
            result.AssignFrom(this);
            return result;
        }
    }

    public class FunctionRuntimeVar : IRuntimeVar
    {
        public FunctionRuntimeVar(SemanticModel model, ISymbol symbol, ISymbol? functionSymbol = null)
        {
            Model = model;
            Symbol = symbol;
            FunctionSymbol = functionSymbol ?? symbol;
        }

        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public ISymbol? FunctionSymbol { get; set; }

        public IType TypeInfo => Symbol.TypeInfo;

        public void AssignFrom(IRuntimeVar other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not FunctionRuntimeVar funVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            FunctionSymbol = funVar.FunctionSymbol;     // using reference
        }

        public override string ToString() => (this as IRuntimeVar).ToDebugString();

        public IRuntimeVar Clone()
        {
            var result = new FunctionRuntimeVar(Model, Symbol);
            result.FunctionSymbol = FunctionSymbol;
            return result;
        }
    }

    public class ClassRuntimeVar : IRuntimeVar
    {
        public ClassRuntimeVar(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;
            var symbols = (TypeInfo as ISymbolContainer)!.Symbols;
            ObjectFields = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => IRuntimeVar.FromSymbol(Model, s));
        }

        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public Dictionary<string, IRuntimeVar> ObjectFields { get; private set; } = [];

        public void AssignFrom(IRuntimeVar other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not ClassRuntimeVar clsVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            ObjectFields = clsVar.ObjectFields;     // using reference
        }

        public override string ToString() => (this as IRuntimeVar).ToDebugString();

        public IRuntimeVar Clone()
        {
            var result = new ClassRuntimeVar(Model, Symbol);
            result.ObjectFields = ObjectFields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
            return result;
        }
    }

    public class InterfaceRuntimeVar(SemanticModel model, ISymbol symbol) : IRuntimeVar
    {
        public SemanticModel Model { get; } = model;

        public ISymbol Symbol { get; } = symbol;

        public IType TypeInfo => Symbol.TypeInfo;

        public VTable? VTable { get; set; }

        public IRuntimeVar? Object { get; set; }

        public void AssignFrom(IRuntimeVar other)
        {
            if (other is not InterfaceRuntimeVar intfVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            if (intfVar.Object is null)
                throw new BabyPenguinRuntimeException($"Cannot assign null to interface {TypeInfo.FullName}");

            if (intfVar.Object is not ClassRuntimeVar clsVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {intfVar.Object.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName}");

            if (clsVar.TypeInfo is not IClass cls)
                throw new BabyPenguinRuntimeException($"Cannot assign type {clsVar.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not a class");

            var vtable = cls.VTables.FirstOrDefault(v => v.Interface.FullName == TypeInfo.FullName);
            if (vtable == null)
                throw new BabyPenguinRuntimeException($"Cannot assign type {clsVar.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not implementing the interface");

            Object = intfVar.Object;     // using reference
            VTable = vtable;
        }

        public override string ToString() => (this as IRuntimeVar).ToDebugString();

        public IRuntimeVar Clone()
        {
            var result = new InterfaceRuntimeVar(Model, Symbol);
            result.Object = Object?.Clone();
            result.VTable = VTable;
            return result;
        }
    }

    public class EnumRuntimeVar : IRuntimeVar
    {
        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public VTable? VTable { get; set; }

        public IRuntimeVar? EnumObject { get; set; }

        public Dictionary<string, IRuntimeVar> ObjectFields { get; private set; } = [];

        public EnumRuntimeVar(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;

            var symbols = (TypeInfo as ISymbolContainer)!.Symbols;
            ObjectFields = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => IRuntimeVar.FromSymbol(Model, s));
        }

        public void AssignFrom(IRuntimeVar other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not EnumRuntimeVar intfVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            EnumObject = intfVar.EnumObject;     // using reference
            ObjectFields = intfVar.ObjectFields;
        }

        public override string ToString() => (this as IRuntimeVar).ToDebugString();

        public IRuntimeVar Clone()
        {
            var result = new EnumRuntimeVar(Model, Symbol);
            result.EnumObject = EnumObject?.Clone();
            result.ObjectFields = ObjectFields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
            return result;
        }
    }
}