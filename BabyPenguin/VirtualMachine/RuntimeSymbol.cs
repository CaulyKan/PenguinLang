using System.Collections;

namespace BabyPenguin.VirtualMachine
{
    public interface IRuntimeSymbol
    {
        SemanticModel Model { get; }

        IType TypeInfo { get; }

        TypeEnum Type => TypeInfo.Type;

        ISymbol Symbol { get; }

        IRuntimeValue Value { get; }

        void AssignFrom(IRuntimeSymbol other);

        void AssignFrom(IRuntimeValue other);

        IRuntimeSymbol Clone();

        T As<T>() where T : class, IRuntimeSymbol => this as T ?? throw new BabyPenguinRuntimeException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

        string? ValueToString => TypeInfo.FullName;

        string ToDebugString() => $"[ {ConsoleColor.YELLOW}{Symbol?.Name}{ConsoleColor.CYAN}({TypeInfo})={ValueToString}{ConsoleColor.NORMAL} ]";

        static IRuntimeSymbol FromSymbol(SemanticModel model, ISymbol symbol)
        {
            if (symbol.TypeInfo.IsEnumType)
            {
                return new EnumRuntimeSymbol(model, symbol);
            }
            else if (symbol is FunctionSymbol)
            {
                return new FunctionRuntimeSymbol(model, symbol, symbol);
            }
            else if (symbol is FunctionVariableSymbol)
            {
                return new FunctionRuntimeSymbol(model, symbol);
            }
            else if (symbol.TypeInfo.IsClassType)
            {
                return new ClassRuntimeSymbol(model, symbol);
            }
            else if (symbol.TypeInfo.IsInterfaceType)
            {
                return new InterfaceRuntimeSymbol(model, symbol);
            }
            else
            {
                return new BasicRuntimeSymbol(model, symbol);
            }
        }
    }

    public class BasicRuntimeSymbol : IRuntimeSymbol
    {
        public BasicRuntimeSymbol(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;
            BasicValue = new BasicRuntimeValue(symbol.TypeInfo);
        }

        public ISymbol Symbol { get; }

        public SemanticModel Model { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public TypeEnum Type => TypeInfo.Type;

        public BasicRuntimeValue BasicValue { get; private set; }

        public IRuntimeValue Value => BasicValue;

        public string? ValueToString => BasicValue.ToString();

        public override string ToString() => (this as IRuntimeSymbol).ToDebugString();

        public void AssignFrom(IRuntimeSymbol other)
        {
            if (other is BasicRuntimeSymbol otherVar)
            {
                if (!other.TypeInfo.CanImplicitlyCastTo(TypeInfo))
                    throw new BabyPenguinRuntimeException($"Cannot implicitly assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
                BasicValue.AssignFrom(otherVar.BasicValue);
            }
            else
            {
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
            }
        }

        public void AssignFrom(IRuntimeValue other)
        {
            if (other is BasicRuntimeValue otherVar)
            {
                if (!other.TypeInfo.CanImplicitlyCastTo(TypeInfo))
                    throw new BabyPenguinRuntimeException($"Cannot implicitly assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
                BasicValue.AssignFrom(otherVar);
            }
            else
            {
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");
            }
        }

        public IRuntimeSymbol Clone()
        {
            var result = new BasicRuntimeSymbol(Model, Symbol);
            result.AssignFrom(this);
            return result;
        }
    }

    public class FunctionRuntimeSymbol : IRuntimeSymbol
    {
        public FunctionRuntimeSymbol(SemanticModel model, ISymbol symbol, ISymbol? functionSymbol = null)
        {
            Model = model;
            Symbol = symbol;
            FunctionValue = functionSymbol == null ? null : new FunctionRuntimeValue(Symbol.TypeInfo, functionSymbol);
        }

        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public FunctionRuntimeValue? FunctionValue { get; private set; }

        public IRuntimeValue Value => (FunctionValue as IRuntimeValue) ?? new NotInitializedRuntimeValue(TypeInfo);

        public IType TypeInfo => Symbol.TypeInfo;

        public string? ValueToString => "fun(" + (FunctionValue?.FunctionSymbol?.FullName ?? "not initialized") + ")";

        public void AssignFrom(IRuntimeSymbol other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not FunctionRuntimeSymbol funVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            FunctionValue = funVar.FunctionValue;
        }

        public void AssignFrom(IRuntimeValue other)
        {
            if (other is not FunctionRuntimeValue funVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            FunctionValue = funVar;
        }

        public override string ToString() => (this as IRuntimeSymbol).ToDebugString();

        public IRuntimeSymbol Clone()
        {
            var result = new FunctionRuntimeSymbol(Model, Symbol);
            result.FunctionValue = FunctionValue;
            return result;
        }
    }


    public class ClassRuntimeSymbol : IRuntimeSymbol
    {
        public ClassRuntimeSymbol(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;
            var symbols = (TypeInfo as ISymbolContainer)!.Symbols;
            var fields = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => IRuntimeSymbol.FromSymbol(Model, s).Value);
            ReferenceValue = new ReferenceRuntimeValue(TypeInfo, fields);
        }

        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public ReferenceRuntimeValue ReferenceValue { get; set; }

        public IRuntimeValue Value => ReferenceValue;

        public string? ValueToString => ReferenceValue.ToString();

        public void AssignFrom(IRuntimeSymbol other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not ClassRuntimeSymbol clsVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            ReferenceValue = clsVar.ReferenceValue;
        }

        public void AssignFrom(IRuntimeValue other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not ReferenceRuntimeValue clsVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            ReferenceValue = clsVar;
        }

        public override string ToString() => (this as IRuntimeSymbol).ToDebugString();

        public IRuntimeSymbol Clone()
        {
            var result = new ClassRuntimeSymbol(Model, Symbol);
            result.ReferenceValue = ReferenceValue;
            return result;
        }
    }

    public class InterfaceRuntimeSymbol(SemanticModel model, ISymbol symbol) : IRuntimeSymbol
    {
        public SemanticModel Model { get; } = model;

        public ISymbol Symbol { get; } = symbol;

        public IType TypeInfo => Symbol.TypeInfo;

        public VTable? VTable { get; set; }

        private IRuntimeValue? value { get; set; }

        public IRuntimeValue Value { get => value ?? new BasicRuntimeValue(BasicType.Void); set => this.value = value; }

        public void AssignFrom(IRuntimeSymbol other)
        {
            if (other is not InterfaceRuntimeSymbol intfVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            if (intfVar.Value is null)
                throw new BabyPenguinRuntimeException($"Cannot assign null to interface {TypeInfo.FullName}");

            if (intfVar.Value.TypeInfo is not IClass cls)
                throw new BabyPenguinRuntimeException($"Cannot assign type {intfVar.Value.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not a class");

            var vtable = cls.VTables.FirstOrDefault(v => v.Interface.FullName == TypeInfo.FullName);
            if (vtable == null)
                throw new BabyPenguinRuntimeException($"Cannot assign type {intfVar.Value.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not implementing the interface");

            Value = intfVar.Value;     // using reference
            VTable = vtable;
        }

        public void AssignFrom(IRuntimeValue other)
        {
            if (other is not ReferenceRuntimeValue refVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            if (refVar.TypeInfo is not IClass cls)
                throw new BabyPenguinRuntimeException($"Cannot assign type {refVar.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not a class");

            var vtable = cls.VTables.FirstOrDefault(v => v.Interface.FullName == TypeInfo.FullName);
            if (vtable == null)
                throw new BabyPenguinRuntimeException($"Cannot assign type {refVar.TypeInfo.FullName} behind {other.TypeInfo.FullName} to interface {TypeInfo.FullName} because it is not implementing the interface");

            Value = refVar;
            VTable = vtable;
        }

        public override string ToString() => (this as IRuntimeSymbol).ToDebugString();

        public string? ValueToString => Value.ToString();

        public IRuntimeSymbol Clone()
        {
            var result = new InterfaceRuntimeSymbol(Model, Symbol);
            result.Value = Value;
            result.VTable = VTable;
            return result;
        }
    }

    public class EnumRuntimeSymbol : IRuntimeSymbol
    {
        public SemanticModel Model { get; }

        public ISymbol Symbol { get; }

        public IType TypeInfo => Symbol.TypeInfo;

        public VTable? VTable { get; set; }

        public EnumRuntimeValue EnumValue { get; private set; }

        public IRuntimeValue Value => EnumValue;

        public EnumRuntimeSymbol(SemanticModel model, ISymbol symbol)
        {
            Model = model;
            Symbol = symbol;

            var symbols = (TypeInfo as ISymbolContainer)!.Symbols;
            var fields = symbols.Where(s => !s.IsEnum).ToDictionary(s => s.Name, s => IRuntimeSymbol.FromSymbol(Model, s).Value);
            EnumValue = new EnumRuntimeValue(TypeInfo, new ReferenceRuntimeValue(TypeInfo, fields), null);
        }

        public void AssignFrom(IRuntimeSymbol other)
        {
            if (other.TypeInfo.FullName != TypeInfo.FullName || other is not EnumRuntimeSymbol enumVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            EnumValue.AssignFrom(enumVar.EnumValue);
        }

        public void AssignFrom(IRuntimeValue other)
        {
            if (other is not EnumRuntimeValue enumVar)
                throw new BabyPenguinRuntimeException($"Cannot assign type {other.TypeInfo.FullName} to type {TypeInfo.FullName}");

            EnumValue.AssignFrom(enumVar);
        }

        public override string ToString() => (this as IRuntimeSymbol).ToDebugString();

        public string? ValueToString => Value.ToString();

        public IRuntimeSymbol Clone()
        {
            var result = new EnumRuntimeSymbol(Model, Symbol);
            result.EnumValue.AssignFrom(EnumValue);
            return result;
        }
    }
}