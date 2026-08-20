using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BabyPenguin.VirtualMachine
{
    public interface IRuntimeValue
    {
        IType TypeInfo { get; }

        T As<T>() where T : class, IRuntimeValue => this as T ?? throw new BabyPenguinRuntimeException($"Cannot cast {GetType().Name} to {typeof(T).Name}", code: ErrorCode.E_RUNTIME_TYPE);

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

    /// <summary>
    /// Union-like storage for primitive value types. All numeric fields share the same 8 bytes,
    /// reducing per-instance memory from ~80 bytes to ~32 bytes (header + TypeInfo ref + 8-byte union + string ref).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct BasicValueUnion
    {
        [FieldOffset(0)] public bool BoolValue;
        [FieldOffset(0)] public byte U8Value;
        [FieldOffset(0)] public sbyte I8Value;
        [FieldOffset(0)] public char CharValue;
        [FieldOffset(0)] public ushort U16Value;
        [FieldOffset(0)] public short I16Value;
        [FieldOffset(0)] public uint U32Value;
        [FieldOffset(0)] public int I32Value;
        [FieldOffset(0)] public float FloatValue;
        [FieldOffset(0)] public ulong U64Value;
        [FieldOffset(0)] public long I64Value;
        [FieldOffset(0)] public double DoubleValue;
    }

    public class BasicRuntimeValue : IRuntimeValue
    {
        public BasicRuntimeValue(IType typeInfo)
        {
            TypeInfo = typeInfo;
        }

        public IType TypeInfo { get; }

        private BasicValueUnion _data;
        private string _stringValue = "";

        // Typed field accessors — backed by the union for numeric types, separate field for string
        public bool BoolValue { get => _data.BoolValue; set => _data.BoolValue = value; }
        public byte U8Value { get => _data.U8Value; set => _data.U8Value = value; }
        public ushort U16Value { get => _data.U16Value; set => _data.U16Value = value; }
        public uint U32Value { get => _data.U32Value; set => _data.U32Value = value; }
        public ulong U64Value { get => _data.U64Value; set => _data.U64Value = value; }
        public sbyte I8Value { get => _data.I8Value; set => _data.I8Value = value; }
        public short I16Value { get => _data.I16Value; set => _data.I16Value = value; }
        public int I32Value { get => _data.I32Value; set => _data.I32Value = value; }
        public long I64Value { get => _data.I64Value; set => _data.I64Value = value; }
        public float FloatValue { get => _data.FloatValue; set => _data.FloatValue = value; }
        public double DoubleValue { get => _data.DoubleValue; set => _data.DoubleValue = value; }
        public string StringValue { get => _stringValue; set => _stringValue = value ?? ""; }
        public char CharValue { get => _data.CharValue; set => _data.CharValue = value; }

        /// <summary>
        /// Provides a ref to the I64Value field for Interlocked atomic operations.
        /// </summary>
        public ref long I64ValueRef => ref _data.I64Value;

        /// <summary>
        /// Typed value getter/setter — returns object? to avoid dynamic/CallSite allocations.
        /// Prefer the typed fields (I32Value, etc.) directly when the type is known at compile time.
        /// </summary>
        public object? DynamicValue
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
                // Use Convert to handle cross-type boxing (e.g., boxed byte → int field),
                // but wrap in unchecked for truncation semantics (e.g., -3 → byte 253)
                switch (TypeInfo.Type)
                {
                    case TypeEnum.Bool: BoolValue = Convert.ToBoolean(value); break;
                    case TypeEnum.U8: U8Value = unchecked((byte)Convert.ToInt64(value)); break;
                    case TypeEnum.U16: U16Value = unchecked((ushort)Convert.ToInt64(value)); break;
                    case TypeEnum.U32: U32Value = unchecked((uint)Convert.ToInt64(value)); break;
                    case TypeEnum.U64: U64Value = unchecked((ulong)Convert.ToInt64(value)); break;
                    case TypeEnum.I8: I8Value = unchecked((sbyte)Convert.ToInt64(value)); break;
                    case TypeEnum.I16: I16Value = unchecked((short)Convert.ToInt64(value)); break;
                    case TypeEnum.I32: I32Value = unchecked((int)Convert.ToInt64(value)); break;
                    case TypeEnum.I64: I64Value = Convert.ToInt64(value); break;
                    case TypeEnum.Float: FloatValue = Convert.ToSingle(value); break;
                    case TypeEnum.Double: DoubleValue = Convert.ToDouble(value); break;
                    case TypeEnum.String: StringValue = value as string ?? ""; break;
                    case TypeEnum.Char: CharValue = unchecked((char)Convert.ToInt64(value)); break;
                    case TypeEnum.Void: break;
                    default: throw new BabyPenguinRuntimeException($"Cannot assign value of type {value?.GetType()} to type {TypeInfo}", code: ErrorCode.E_RUNTIME_TYPE);
                }
            }
        }

        public void AssignFrom(BasicRuntimeValue otherVar)
        {
            _data = otherVar._data;
            _stringValue = otherVar._stringValue;
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
                throw new BabyPenguinRuntimeException($"Cannot create FunctionRuntimeValue with symbol of type {funcSymbol.GetType().Name}", code: ErrorCode.E_RUNTIME_TYPE);
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
        private RuntimeGlobal? _global;

        public ReferenceRuntimeValue(IType typeInfo, Dictionary<string, IRuntimeValue> fields, RuntimeGlobal? global = null)
        {
            _global = global;
            RefId = global?.NextRefId() ?? (ulong)Random.Shared.NextInt64();
            if (_global != null)
                _global.AllObjects[RefId] = this;
            _typeInfo = typeInfo;
            Fields = fields;
        }

        /// <summary>
        /// Reuse this object from the pool with a new type and fields.
        /// Assigns a new RefId and re-registers in AllObjects.
        /// </summary>
        public void Reuse(IType typeInfo, Dictionary<string, IRuntimeValue> fields)
        {
            RefId = _global?.NextRefId() ?? (ulong)Random.Shared.NextInt64();
            if (_global != null)
                _global.AllObjects[RefId] = this;
            _typeInfo = typeInfo;
            Fields.Clear();
            foreach (var kvp in fields)
                Fields[kvp.Key] = kvp.Value;
            ExternImplenmentationValue = null;
        }

        private IType _typeInfo;
        public IType TypeInfo => _typeInfo;

        public Dictionary<string, IRuntimeValue> Fields { get; } = [];

        public ulong RefId { get; private set; }

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

    /// <summary>
    /// Value-semantics copy matching EmperorPenguin's native inline layout:
    /// value-class instances are copied memberwise (recursing into value-typed
    /// fields), while reference-class instances are SHARED (a native struct copy
    /// copies the pointer, not the pointee). Enum values copy their tag and copy
    /// the payload only when it is value-typed. ReferenceRuntimeValue.Clone is
    /// not usable here — it deep-copies reference fields too, which would break
    /// identity where native code shares the pointer.
    /// </summary>
    public static class RuntimeValueCopier
    {
        public static IRuntimeValue CopyIfValueSemantic(IRuntimeValue val, RuntimeGlobal? global)
        {
            return Copy(val, [], global);
        }

        private static IRuntimeValue Copy(IRuntimeValue val, Dictionary<ulong, ReferenceRuntimeValue> visited, RuntimeGlobal? global)
        {
            switch (val)
            {
                case ReferenceRuntimeValue rv:
                    if (rv.TypeInfo.TypeNode is null
                        || !IRTypeClassifier.IsValueClassIncludingAuto(rv.TypeInfo.TypeNode))
                        return rv; // reference class (or unknown) — share, like a copied pointer
                    if (visited.TryGetValue(rv.RefId, out var existing))
                        return existing;
                    var copy = new ReferenceRuntimeValue(rv.TypeInfo, [], global);
                    visited[rv.RefId] = copy;
                    foreach (var kvp in rv.Fields)
                    {
                        // Methods (function values) and extern backings are shared;
                        // data fields recurse so nested value classes copy inline.
                        if (kvp.Value is FunctionRuntimeValue or ExternRuntimeValue)
                            copy.Fields[kvp.Key] = kvp.Value;
                        else
                            copy.Fields[kvp.Key] = Copy(kvp.Value, visited, global);
                    }
                    return copy;

                case EnumRuntimeValue ev:
                    var fields = new ReferenceRuntimeValue(ev.FieldsValue.TypeInfo, [], global);
                    foreach (var kvp in ev.FieldsValue.Fields)
                        fields.Fields[kvp.Key] = kvp.Value.Clone();
                    var containing = ev.ContainingValue != null ? Copy(ev.ContainingValue, visited, global) : null;
                    return new EnumRuntimeValue(ev.TypeInfo, fields, containing);

                default:
                    return val.Clone(); // primitives (incl. string payloads: wrapper copies, .NET string shares)
            }
        }
    }
}