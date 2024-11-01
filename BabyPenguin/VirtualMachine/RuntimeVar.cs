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

        string? ValueString => TypeInfo.FullName;

        string ToDebugString() => $"{Symbol?.Name}({ValueString})";

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

            switch (Type)
            {
                case TypeEnum.Bool:
                    Value = false;
                    break;
                case TypeEnum.U8:
                    Value = (byte)0;
                    break;
                case TypeEnum.U16:
                    Value = (ushort)0;
                    break;
                case TypeEnum.U32:
                    Value = (uint)0;
                    break;
                case TypeEnum.U64:
                    Value = (ulong)0;
                    break;
                case TypeEnum.I8:
                    Value = (sbyte)0;
                    break;
                case TypeEnum.I16:
                    Value = (short)0;
                    break;
                case TypeEnum.I32:
                    Value = (int)0;
                    break;
                case TypeEnum.I64:
                    Value = (long)0;
                    break;
                case TypeEnum.Float:
                    Value = (float)0.0;
                    break;
                case TypeEnum.Double:
                    Value = (double)0.0;
                    break;
                case TypeEnum.String:
                    Value = "";
                    break;
                case TypeEnum.Char:
                    Value = '\0';
                    break;
                case TypeEnum.Void:
                case TypeEnum.Fun:
                    Value = 0;
                    break;
                default:
                    throw new BabyPenguinRuntimeException($"Unsupported type {Type} for BasicVar");
            }
        }

        public ISymbol Symbol { get; }

        public SemanticModel Model { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public TypeEnum Type => TypeInfo.Type;

        public object Value { get; set; }

        public string? ValueString => Type switch
        {
            TypeEnum.Bool => Value?.ToString(),
            TypeEnum.U8 => Value?.ToString(),
            TypeEnum.U16 => Value?.ToString(),
            TypeEnum.U32 => Value?.ToString(),
            TypeEnum.U64 => Value?.ToString(),
            TypeEnum.I8 => Value?.ToString(),
            TypeEnum.I16 => Value?.ToString(),
            TypeEnum.I32 => Value?.ToString(),
            TypeEnum.I64 => Value?.ToString(),
            TypeEnum.Float => Value?.ToString(),
            TypeEnum.Double => Value?.ToString(),
            TypeEnum.String => "\"" + Value?.ToString() + "\"",
            TypeEnum.Char => "'" + Value?.ToString() + "'",
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
                Value = otherVar.Value;
            }
            else
            {
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
            }
        }

        public IRuntimeVar Clone()
        {
            var result = new BasicRuntimeVar(Model, Symbol);
            result.Value = Value;
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