namespace BabyPenguin.VirtualMachine
{

    public class RuntimeVar
    {
        public RuntimeVar(SemanticModel? model, IType typeInfo, ISymbol? symbol)
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
                        var symbols = (typeInfo as ISymbolContainer)!.Symbols;
                        Value = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => new RuntimeVar(Model, s.TypeInfo, s));
                        break;
                    }
                case TypeEnum.Interface:
                    {
                        var symbols = (typeInfo as ISymbolContainer)!.Symbols;
                        Value = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => new RuntimeVar(Model, s.TypeInfo, s));
                        break;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public IType TypeInfo { get; }
        public TypeEnum Type { get; }
        public SemanticModel? Model { get; }
        public object? Value { get; set; }
        public RuntimeVar? EnumValue { get; set; }
        public ISymbol? Symbol { get; }
        public FunctionSymbol? FunctionSymbol { get; private set; }
        public VTable? VTable { get; set; }

        public void AssignFrom(RuntimeVar other)
        {
            if (!other.TypeInfo.IsEnumType && !this.TypeInfo.IsEnumType)
                if (!other.TypeInfo.CanImplicitlyCastTo(this.TypeInfo))
                    throw new BabyPenguinRuntimeException($"Cannot assign type {other.Type} to type {Type}");
            Value = other.Value;
            EnumValue = other.EnumValue;
            FunctionSymbol = other.FunctionSymbol;
            VTable = other.VTable;
        }

        public static RuntimeVar Void()
        {
            return new RuntimeVar(null, BasicType.Void, null);
        }

        public RuntimeVar Clone()
        {
            RuntimeVar result = new(Model, TypeInfo, Symbol)
            {
                Value = Value,
                VTable = VTable
            };
            return result;
        }

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
            TypeEnum.Fun => "fun",
            TypeEnum.Class => "class",
            TypeEnum.Enum => "enum",
            TypeEnum.Interface => "interface",
            _ => "unknown"
        };

        public override string ToString()
        {
            return $"{ValueString}({TypeInfo})";
        }

        public string ToDebugString()
        {
            return $"{Symbol?.Name}({ValueString})";
        }
    }
}