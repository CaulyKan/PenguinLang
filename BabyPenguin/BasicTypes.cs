
using BabyPenguin.SemanticPass;
using PenguinLangSyntax;

namespace BabyPenguin.SemanticNode
{
    public class BasicType : IType
    {
        private BasicType(string name, TypeEnum type)
        {
            Name = name;
            Type = type;
        }

        public static BasicType Bool { get; } = new("bool", TypeEnum.Bool);
        public static BasicType Double { get; } = new("bool", TypeEnum.Double);
        public static BasicType Float { get; } = new("float", TypeEnum.Float);
        public static BasicType String { get; } = new("string", TypeEnum.String);
        public static BasicType Void { get; } = new("void", TypeEnum.Void);
        public static BasicType U8 { get; } = new("u8", TypeEnum.U8);
        public static BasicType U16 { get; } = new("u16", TypeEnum.U16);
        public static BasicType U32 { get; } = new("u32", TypeEnum.U32);
        public static BasicType U64 { get; } = new("u64", TypeEnum.U64);
        public static BasicType I8 { get; } = new("i8", TypeEnum.I8);
        public static BasicType I16 { get; } = new("i16", TypeEnum.I16);
        public static BasicType I32 { get; } = new("i32", TypeEnum.I32);
        public static BasicType I64 { get; } = new("i64", TypeEnum.I64);
        public static BasicType Char { get; } = new("char", TypeEnum.Char);
        public static BasicType Fun { get; } = new("fun", TypeEnum.Fun);

        public static Dictionary<string, BasicType> BasicTypes { get; } = new() {
            { "bool", Bool },
            { "double", Double },
            { "float", Float },
            { "string", String },
            { "void", Void },
            { "u8", U8 },
            { "u16", U16 },
            { "u32", U32 },
            { "u64", U64 },
            { "i8", I8 },
            { "i16", I16 },
            { "i32", I32 },
            { "i64", I64 },
            { "char", Char },
            { "fun", Fun }
        };

        public bool CanImplicitlyCastTo(IType other)
        {
            if (FullName == other.FullName) return true;
            return implicitlyCastOrders.ContainsKey(Type) && implicitlyCastOrders[Type].Contains(other.Type);
        }

        static readonly Dictionary<TypeEnum, List<TypeEnum>> implicitlyCastOrders = new Dictionary<TypeEnum, List<TypeEnum>>{
            {
                TypeEnum.I8,
                [
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I16,
                [
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I32,
                [
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.I64,
                [
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U8,
                [
                    TypeEnum.U16,
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I8,
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U16,
                [
                    TypeEnum.U32,
                    TypeEnum.U64,
                    TypeEnum.I16,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U32,
                [
                    TypeEnum.U64,
                    TypeEnum.I32,
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.U64,
                [
                    TypeEnum.I64,
                    TypeEnum.Float,
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Float,
                [
                    TypeEnum.Double,
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Double,
                [
                    TypeEnum.String
                ]
            },
            {
                TypeEnum.Bool,
                [
                    TypeEnum.String
                ]
            },
        };

        public string Name { get; }

        public string FullName
        {
            get
            {
                var result = Name;
                if (GenericArguments.Count > 0)
                {
                    result += "<" + string.Join(",", GenericArguments.Select((t, i) => t.FullName)) + ">";
                }
                return result;
            }
        }

        public INamespace? Namespace => null;

        public TypeEnum Type { get; }

        public List<string> GenericDefinitions { get; } = [];

        public List<IType> GenericArguments { get; set; } = [];

        public List<IType> GenericInstances { get; set; } = [];

        public SemanticModel Model => throw new NotImplementedException();

        public SourceLocation SourceLocation => SourceLocation.Empty();

        public SyntaxNode? SyntaxNode => null;

        public static IType? ResolveLiteralType(string literal)
        {
            if (literal.StartsWith('"') && literal.EndsWith('"'))
            {
                return String;
            }
            else if (byte.TryParse(literal, out var _))
            {
                return U8;
            }
            else if (sbyte.TryParse(literal, out var _))
            {
                return I8;
            }
            else if (ushort.TryParse(literal, out var _))
            {
                return U16;
            }
            else if (short.TryParse(literal, out var _))
            {
                return I16;
            }
            else if (uint.TryParse(literal, out var _))
            {
                return U32;
            }
            else if (int.TryParse(literal, out var _))
            {
                return I32;
            }
            else if (ulong.TryParse(literal, out var _))
            {
                return U64;
            }
            else if (long.TryParse(literal, out var _))
            {
                return I64;
            }
            else if (literal == "true" || literal == "false")
            {
                return Bool;
            }
            else if (literal.StartsWith('\'') && literal.EndsWith('\''))
            {
                return Char;
            }
            else if (float.TryParse(literal, out var _))
            {
                return Float;
            }
            else if (double.TryParse(literal, out var _))
            {
                return Double;
            }
            else
            {
                return null;
            }
        }

        public IType Specialize(List<IType> genericArguments)
        {
            var self = this as IType;
            if (self.IsFunctionType)
            {
                if (self.GenericArguments.Count > 0) throw new BabyPenguinException("Cannot specialize a specialized type.");

                var typeInfo = new BasicType("fun", TypeEnum.Fun) { GenericArguments = genericArguments } as IType;
                if (self.GenericInstances.Find(i => i.FullName == typeInfo.FullName) is IType existingType)
                    return existingType;
                self.GenericInstances.Add(typeInfo);
                return typeInfo;

            }
            else
            {
                throw new BabyPenguinException("Cannot specialize a basic type.");
            }
        }

        override public string ToString() => Name;
    }
}