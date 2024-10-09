using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;

namespace BabyPenguin
{
    using System.Text.RegularExpressions;
    using BabyPenguin.Syntax;
    using ConsoleTables;
    using Semantic;

    public partial class SemanticModel
    {
        public SemanticModel(ErrorReporter reporter)
        {
            BuiltinNamespace = AddBuiltins();

            Reporter = reporter;
        }

        public void CompileSyntax(IEnumerable<SyntaxCompiler> compilers)
        {
            var compilersList = compilers.ToList();
            SyntaxCopmilers.AddRange(compilersList);

            foreach (var compiler in compilersList)
            {
                foreach (var ns in compiler.Namespaces)
                {
                    Namespaces.Add(new Semantic.Namespace(this, ns));
                }
            }

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Semantic Scopes:\n" + string.Join("\n", Namespaces.OfType<IPrettyPrint>().SelectMany(s => s.PrettyPrint(0))));
            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Type Table Before Specializations:\n" + PrintTypeTable());

            BuiltinNamespace.ElabSyntaxSymbols();
            Namespaces.ForEach(ns => ns.ElabSyntaxSymbols());

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol Table Before Specializations:\n" + PrintSymbolTable());

            while (CompileTasks.Count > 0)
            {
                var tasks = CompileTasks.ToList();
                CompileTasks.Clear();
                foreach (var task in tasks)
                {
                    task.CompileSyntaxStatements();
                }
            }

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Type Table After Specializations:\n" + PrintTypeTable());
            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol Table After Specializations:\n" + PrintSymbolTable());
        }

        public string PrintTypeTable()
        {
            var table = new ConsoleTable("Name");
            Types.ForEach(s => table.AddRow(s.FullName));
            return table.ToMarkDownString();
        }

        public string PrintSymbolTable()
        {
            var table = new ConsoleTable("Name", "Type", "Source");
            Symbols.ForEach(s => table.AddRow(s.FullName, s.TypeInfo.ToString(), s.SourceLocation.ToString()));
            return table.ToMarkDownString();
        }

        public List<ISymbol> ResolveClassSymbols(TypeInfo classType)
        {
            if (classType.IsClassType || classType.IsEnumType)
            {
                return Symbols.Where(s => s.Parent.FullName == classType.FullName).ToList();
            }
            else
            {
                throw new PenguinLangException("Parameter for ResolveClassSymbols must be a class type");
            }
        }

        public EnumSymbol? ResolveEnumSymbol(string name, ISemanticScope? scope = null, SourceLocation? sourceLocation = null)
        {
            var nameComponents = NameComponents.ParseName(name);
            var fullname = "";
            if (nameComponents.Prefix.Count > 0)
            {
                var fullPrefixName = BuildFullTypeName(nameComponents.PrefixString, scope, sourceLocation);
                fullname = fullPrefixName + "." + nameComponents.Name;
            }
            else
            {
                fullname = name;
            }
            var symbol = Symbols.FirstOrDefault(t => t.FullName == fullname && t.IsEnum);
            if (symbol == null && scope != null)
                symbol = Symbols.FirstOrDefault(t => t.FullName == scope.NamespaceName + "." + fullname && t.IsEnum);

            return symbol as EnumSymbol;
        }

        public ISymbol? ResolveSymbol(string name, ISemanticScope? scope = null, SourceLocation? sourceLocation = null, bool? isStatic = null)
        {
            var nameComponents = NameComponents.ParseName(name);
            var fullname = "";
            if (nameComponents.Prefix.Count > 0)
            {
                var fullPrefixName = BuildFullTypeName(nameComponents.PrefixString, scope, sourceLocation);
                fullname = fullPrefixName + "." + nameComponents.Name;
            }
            else
            {
                fullname = name;
            }
            var symbol = Symbols.FirstOrDefault(t => t.FullName == fullname && !t.IsEnum && (isStatic == null || t.IsStatic == isStatic));
            if (symbol == null && scope != null)
                symbol = Symbols.FirstOrDefault(t => t.FullName == scope.NamespaceName + "." + fullname && !t.IsEnum && (isStatic == null || t.IsStatic == isStatic));

            return symbol;
        }

        public string BuildFullTypeName(string name, ISemanticScope? scope = null, SourceLocation? sourceLocation = null)
        {
            bool isValidType(string s)
            {
                if (TypeInfo.BuiltinTypes.ContainsKey(s)) return true;
                var currentScope = scope;
                while (currentScope != null)
                {
                    if (currentScope is IGenericContainer genericContainer && genericContainer.GenericDefinitions.Contains(name) && genericContainer.IsSpecialized)
                        return true;
                    currentScope = currentScope.Parent;
                }
                return Types.Exists(t => t.NameComponents.NameWithPrefix == s);
            }

            if (scope == null) return name;
            var nameComponents = NameComponents.ParseName(name);
            var prefix = nameComponents.Prefix;
            if (!isValidType(nameComponents.NameWithPrefix))
            {
                var currentScope = scope;
                while (currentScope as Semantic.Namespace == null)
                {
                    currentScope = currentScope!.Parent;
                }
                var ns = currentScope as Semantic.Namespace;
                var namespaceName = ns!.FullName;
                if (isValidType(namespaceName + "." + nameComponents.NameWithPrefix))
                    prefix = namespaceName.Split('.').Concat(nameComponents.Prefix).ToList();
                else
                    Reporter.Throw($"Could not resolve type '{name}'", sourceLocation ?? SourceLocation.Empty());
            }

            var newComponents = nameComponents with
            {
                Prefix = prefix,
                Generics = nameComponents.Generics.Select(i => BuildFullTypeName(i, scope, sourceLocation)).ToList()
            };
            return newComponents.ToString();
        }

        public TypeInfo? ResolveType(string name, ISemanticScope? scope = null, SourceLocation? sourceLocation = null)
        {
            if (TypeInfo.BuiltinTypes.TryGetValue(name, out TypeInfo? value))
                return value;

            var nameComponents = NameComponents.ParseName(scope == null ? name : BuildFullTypeName(name, scope, sourceLocation));

            var exactType = Types.FirstOrDefault(t => t.FullName == nameComponents.ToString());
            if (exactType != null) return exactType;

            var baseType = Types.FirstOrDefault(t => t.NameComponents.NameWithPrefix == nameComponents.NameWithPrefix && t.IsGeneric && !t.IsSpecialized);

            if (baseType == null)
            {
                var currentScope = scope;
                while (currentScope != null)
                {
                    if (currentScope is IGenericContainer genericContainer && genericContainer.GenericDefinitions.Contains(name))
                    {
                        if (!genericContainer.IsSpecialized)
                            Reporter.Throw($"Cannot specialize non-specialized generic container '{genericContainer.FullName}'", SourceLocation.Empty());
                        baseType = genericContainer.GenericArguments[genericContainer.GenericDefinitions.IndexOf(name)];
                        break;
                    }
                    currentScope = currentScope.Parent;
                }
            }

            if (baseType == null) return null;

            if (baseType.IsGeneric && !baseType.IsSpecialized)
            {
                var genericArguments = nameComponents.Generics.Select(genericTypeName =>
                {
                    var t = ResolveType(genericTypeName, scope, sourceLocation);
                    if (t == null) Reporter.Throw($"Could not resolve generic argument '{genericTypeName}' for type '{baseType.FullName}'", SourceLocation.Empty());
                    return t!;
                }).ToList();

                var specializedType = ResolveOrCreateSpecializedType(baseType, genericArguments);

                return specializedType;
            }
            else
            {
                return baseType;
            }
        }

        public Class? ResolveClass(string name, ISemanticScope? scope = null, SourceLocation? sourceLocation = null)
        {
            var fullname = scope == null ? name : BuildFullTypeName(name, scope, sourceLocation);
            var exactClass = Classes.FirstOrDefault(c => c.TypeInfo!.FullName == fullname);

            if (exactClass != null) return exactClass;

            var nameComponents = NameComponents.ParseName(fullname);
            var baseClass = Classes.FirstOrDefault(c => c.TypeInfo!.NameComponents.NameWithPrefix == nameComponents.NameWithPrefix && c.IsGeneric && !c.IsSpecialized);

            if (baseClass == null) return null;

            if (baseClass.IsGeneric && !baseClass.IsSpecialized)
            {
                var genericArguments = nameComponents.Generics.Select(genericTypeName =>
                {
                    var t = ResolveType(genericTypeName, scope, sourceLocation);
                    if (t == null) Reporter.Throw($"Could not resolve generic argument '{genericTypeName}' for type '{baseClass.FullName}'", SourceLocation.Empty());
                    return t!;
                }).ToList();

                var specializedClass = ResolveOrCreateSpecializedClass(baseClass, genericArguments, sourceLocation);
                return specializedClass;
            }
            else
            {
                return baseClass;
            }
        }

        public Enum CreateEnum(Or<string, Semantic.Namespace> namespace_, Syntax.EnumDefinition syntaxNode)
        {
            var ns = namespace_.IsLeft ? Namespaces.FirstOrDefault(n => n.Name == namespace_.Left) : namespace_.Right;
            if (ns == null)
                Reporter.Throw($"Namespace '{namespace_}' does not exist", SourceLocation.Empty());

            var enum_ = new Enum(this, ns, syntaxNode);
            if (ns.Enums.Any(c => c.Name == enum_.Name) || ns.Classes.Any(c => c.Name == enum_.Name))
                Reporter.Throw($"Class '{enum_.Name}' already exists in namespace '{namespace_}'", SourceLocation.Empty());
            enum_.TypeInfo = CreateType(enum_.Name, TypeEnum.Enum, namespace_, enum_.GenericDefinitions);
            Enums.Add(enum_);
            return enum_;
        }

        public Class CreateClass(string name, Or<string, Semantic.Namespace> namespace_, List<string>? genericDefinitions = null)
        {
            var ns = namespace_.IsLeft ? Namespaces.FirstOrDefault(n => n.Name == namespace_.Left) : namespace_.Right;
            if (ns == null)
                Reporter.Throw($"Namespace '{namespace_}' does not exist", SourceLocation.Empty());
            if (ns.Classes.Any(c => c.Name == name) || ns.Enums.Any(c => c.Name == name))
                Reporter.Throw($"Class '{name}' already exists in namespace '{namespace_}'", SourceLocation.Empty());

            var class_ = new Class(this, ns, name, genericDefinitions);
            class_.TypeInfo = CreateType(class_.Name, TypeEnum.Class, ns, class_.GenericDefinitions);
            ns.Classes.Add(class_);
            Classes.Add(class_);
            return class_;
        }

        public Class CreateClass(Or<string, Semantic.Namespace> namespace_, Syntax.ClassDefinition syntaxNode)
        {
            var ns = namespace_.IsLeft ? Namespaces.FirstOrDefault(n => n.Name == namespace_.Left) : namespace_.Right;
            if (ns == null)
                Reporter.Throw($"Namespace '{namespace_}' does not exist", SourceLocation.Empty());

            var class_ = new Class(this, ns, syntaxNode);
            if (ns.Classes.Any(c => c.Name == class_.Name || c.TypeInfo!.Name == class_.Name))
                Reporter.Throw($"Class '{class_.Name}' already exists in namespace '{namespace_}'", SourceLocation.Empty());
            class_.TypeInfo = CreateType(class_.Name, TypeEnum.Class, namespace_, class_.GenericDefinitions);
            Classes.Add(class_);
            return class_;
        }

        public Enum ResolveOrCreateSpecializedEnum(Enum enum_, List<TypeInfo> genericArguments, SourceLocation? sourceLocation = null)
        {
            var baseType = enum_.TypeInfo!;
            if (!baseType.IsGeneric && genericArguments.Count > 0)
                Reporter.Throw($"Cannot specialize non-generic type '{baseType.FullName}'", sourceLocation);
            if (baseType.IsSpecialized)
                return enum_;

            var type = new TypeInfo(baseType.Name, baseType.Namespace, TypeEnum.Enum, baseType.GenericDefinitions, genericArguments);
            if (Enums.Find(c => c.TypeInfo == type) is Enum existingEnum)
                return existingEnum;

            var newEnum = enum_.Specialize(type);
            if (!Types.Contains(type))
                Types.Add(type);
            Enums.Add(newEnum);

            newEnum.ElabSyntaxSymbols();

            return newEnum;
        }

        public Class ResolveOrCreateSpecializedClass(Class class_, List<TypeInfo> genericArguments, SourceLocation? sourceLocation = null)
        {
            var baseType = class_.TypeInfo!;
            if (!baseType.IsGeneric && genericArguments.Count > 0)
                Reporter.Throw($"Cannot specialize non-generic type '{baseType.FullName}'", sourceLocation);
            if (baseType.IsSpecialized)
                return class_;

            var type = new TypeInfo(baseType.Name, baseType.Namespace, TypeEnum.Class, baseType.GenericDefinitions, genericArguments);
            if (Classes.Find(c => c.TypeInfo == type) is Class existingClass)
                return existingClass;

            var newClass = class_.Specialize(type);
            if (!Types.Contains(type))
                Types.Add(type);
            Classes.Add(newClass);

            newClass.ElabSyntaxSymbols();

            return newClass;
        }

        public TypeInfo CreateType(string name, TypeEnum typeEnum, Or<string, Semantic.Namespace> namespace_, List<string>? genericDefinition = null, List<TypeInfo>? genericArguments = null)
        {
            var namespaceName = namespace_.IsLeft ? namespace_.Left! : namespace_.Right!.FullName;
            var type = new TypeInfo(name, namespaceName, typeEnum, genericDefinition, genericArguments);
            if (Types.Any(t => t.FullName == type.FullName))
            {
                Reporter.Throw($"Type '{type.FullName}' already exists", SourceLocation.Empty());
            }
            Types.Add(type);
            return type;
        }

        public TypeInfo ResolveOrCreateType(string name, TypeEnum typeEnum, Or<string, Semantic.Namespace> namespace_, List<string>? genericDefinition = null, List<TypeInfo>? genericArguments = null)
        {
            var namespaceName = namespace_.IsLeft ? namespace_.Left! : namespace_.Right!.FullName;
            var components = new NameComponents([.. namespaceName.Split('.')], name, (genericArguments ?? []).Select(i => i.FullName).ToList());
            var existingType = ResolveType(components.ToString());
            if (existingType != null)
            {
                return existingType;
            }
            else
            {
                var type = new TypeInfo(name, namespaceName, typeEnum, genericDefinition, genericArguments);
                Types.Add(type);
                return type;
            }
        }

        public TypeInfo ResolveOrCreateSpecializedType(TypeInfo baseType, List<TypeInfo> genericArguments)
        {
            var type = new TypeInfo(baseType.Name, baseType.Namespace, baseType.Type, baseType.GenericDefinitions, genericArguments);
            if (Types.Contains(type))
            {
                return type;
            }
            else
            {
                if (baseType.IsClassType)
                {
                    var class_ = Classes.FirstOrDefault(c => c.TypeInfo == baseType);
                    if (class_ == null)
                        Reporter.Throw($"Cannot specialize non-class type '{baseType.FullName}'", SourceLocation.Empty());
                    return ResolveOrCreateSpecializedClass(class_, genericArguments).TypeInfo!;
                }
                else if (baseType.IsEnumType)
                {
                    var enum_ = Enums.FirstOrDefault(e => e.TypeInfo == baseType);
                    if (enum_ == null)
                        Reporter.Throw($"Cannot specialize non-enum type '{baseType.FullName}'", SourceLocation.Empty());
                    return ResolveOrCreateSpecializedEnum(enum_, genericArguments).TypeInfo!;
                }
                else
                {
                    Reporter.Throw($"Cannot specialize type '{baseType.FullName}'", SourceLocation.Empty());
                    throw new NotImplementedException();
                }
            }
        }

        public Semantic.Namespace BuiltinNamespace { get; }
        public List<Semantic.Namespace> Namespaces { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<Semantic.Enum> Enums { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<TypeInfo> Types { get; } = [];
        public List<ISymbol> Symbols { get; } = [];
        public ErrorReporter Reporter { get; }
        public List<ICodeContainer> CompileTasks { get; } = [];
        public List<SyntaxCompiler> SyntaxCopmilers { get; } = [];
    }

    public class SemanticCompiler
    {
        public SemanticCompiler(ErrorReporter? reporter = null)
        {
            Reporter = reporter ?? new ErrorReporter();
        }

        public ErrorReporter Reporter { get; }

        public List<PenguinParser> Parsers { get; } = [];

        private static ulong counter = 0;

        public SemanticCompiler AddFile(string filePath)
        {
            var parser = new PenguinParser(filePath, Reporter);
            Parsers.Add(parser);
            return this;
        }

        public SemanticCompiler AddSource(string source, string? fileName = null)
        {
            var parser = new PenguinParser(source, fileName ?? $"<anonymous_{counter++}>", Reporter);
            Parsers.Add(parser);
            return this;
        }

        public SemanticModel Compile()
        {
            var syntaxCompilers = Parsers.Select(parser =>
            {
                if (!parser.Parse() || parser.Result == null)
                {
                    Reporter.Throw("Failed to parse input: " + parser.SourceFile + "\n");
                    throw new NotImplementedException(); // never reached
                }
                else
                    return new SyntaxCompiler(parser.SourceFile, parser.Result, Reporter);
            }).ToList();

            foreach (var compiler in syntaxCompilers)
            {
                compiler.Compile();
            }

            var model = new SemanticModel(Reporter);
            model.CompileSyntax(syntaxCompilers);
            return model;
        }
    }
}