using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BabyPenguin.Symbol;
using BabyPenguin.SemanticInterface;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>The set of generated C# source files for one program.</summary>
    public class CSharpProgram
    {
        public List<(string FileName, string Content)> Sources { get; } = new();
    }

    /// <summary>
    /// Lowers a compiled SemanticModel to C#. Lowers only functions reachable from the entry points
    /// (program namespace constructors + initial routines), plus the classes/enums those functions
    /// touch, the externs they call, and the globals they use. Generates a fast-path __builtin__main
    /// that runs namespace init then the initial routines synchronously (bypassing the coroutine
    /// scheduler — correct for programs with no wait/emit/yield, e.g. the bootstrap).
    /// </summary>
    public class CSharpBackend
    {
        public IRModule IRModule { get; private set; } = null!;

        public CSharpProgram Lower(SemanticModel model) => Lower(model, standalone: false);

        public CSharpProgram Lower(SemanticModel model, bool standalone)
        {
            IRModule = new IRGenerator(model).Generate();
            try
            {
                if (Environment.GetEnvironmentVariable("BP_DUMP_IR") is string dump && dump != null && dump != "0")
                    File.WriteAllText("/tmp/bp_ir.txt", IRModule.Display());
            }
            catch { }

            var mangler = new NameMangler();
            var interfaceNames = model.Interfaces.Select(i => CSharpEmitter.Normalize(i.FullName())).ToHashSet();
            var emitter = new CSharpEmitter(mangler, interfaceNames);

            var mainFunc = IRModule.FindFunction("__builtin__main");
            string Norm(string s) => CSharpEmitter.Normalize(s);
            // Externs get trivial IR stubs and land in IRModule.Functions with IsExtern=false, so detect
            // them from the semantic model; they must NOT be lowered from IR — ExternLowerer emits them.
            var externSet = new HashSet<string>(
                model.Symbols.OfType<FunctionSymbol>().Where(s => s.IsExtern).Select(s => Norm(s.FullName().Replace(".", "_"))));
            // Index IR functions by normalized name (strips !mut) so reachability lookups are consistent
            // regardless of mutability markers in IR names.
            var funcByNorm = new Dictionary<string, IRFunction>();
            foreach (var kv in IRModule.Functions)
                funcByNorm[Norm(kv.Key)] = kv.Value;

            // Include concrete interface implementation methods so vtables can dispatch to them.
            var implSeeds = new List<string>();
            foreach (var cls in model.Classes)
            {
                foreach (var vt in cls.VTables)
                {
                    foreach (var slot in vt.Slots)
                    {
                        var implName = slot.ImplementationSymbol?.FullName()?.Replace(".", "_") ?? "";
                        if (!string.IsNullOrEmpty(implName)) implSeeds.Add(Norm(implName));
                    }
                }
            }
            foreach (var enm in model.Enums)
            {
                foreach (var vt in enm.VTables)
                {
                    foreach (var slot in vt.Slots)
                    {
                        var implName = slot.ImplementationSymbol?.FullName()?.Replace(".", "_") ?? "";
                        if (!string.IsNullOrEmpty(implName)) implSeeds.Add(Norm(implName));
                    }
                }
            }

            var nsNews = (mainFunc != null ? ExtractNamespaceConstructors(mainFunc) : new List<string>())
                .Select(Norm).Where(n => n != "__builtin_new" && n != "_utils_new").ToList();
            var entryPoints = nsNews.Concat(IRModule.EntryFunctions.Select(Norm)).Concat(implSeeds).Distinct().ToList();

            var reachable = ReachableFunctions(entryPoints, externSet, funcByNorm);
            var reachableFuncs = reachable.Where(n => !externSet.Contains(n) && funcByNorm.ContainsKey(n))
                .Select(n => funcByNorm[n]).ToList();

            // Build extern symbol lookup (normalized name -> FunctionSymbol) for collection dispatch.
            var externSyms = model.Symbols.OfType<FunctionSymbol>().Where(s => s.IsExtern)
                .ToDictionary(s => Norm(s.FullName().Replace(".", "_")));

            // Collect externs actually called (with call-site signatures) + IR type roots.
            var externInfos = new Dictionary<string, ExternInfo>();
            var typeRoots = new HashSet<string>();
            foreach (var f in reachableFuncs)
            {
                foreach (var ei in ExternCallsOf(f, externSet))
                    if (!externInfos.ContainsKey(ei.Name)) externInfos[ei.Name] = ei;
                foreach (var callee in CalleesOf(f))
                    if (externSet.Contains(callee) && !externInfos.ContainsKey(callee))
                        externInfos[callee] = new ExternInfo { Name = callee, Symbol = externSyms.GetValueOrDefault(callee) };
                foreach (var t in TypesOf(f))
                    typeRoots.Add(t);
            }
            foreach (var ei in externInfos.Values)
                if (ei.Symbol == null && externSyms.TryGetValue(ei.Name, out var sym)) ei.Symbol = sym;

            var typeLowerer = new TypeLowerer(model, emitter);
            var typesSrc = typeLowerer.Lower(typeRoots);
            var funcLowerer = new FunctionLowerer(emitter, IRModule);

            var functionsSrc = new StringBuilder();
            // Build mapping from normalized IR name to lowered C# method name for vtable registration.
            // The function lowering uses emitter.MangleName(f.Name) which replaces all unsafe chars
            // (dots, hyphens, angle brackets) with underscores. We record this mapping so vtable
            // registration can look up the correct C# method name by matching against funcByNorm keys.
            var loweredNames = new Dictionary<string, string>();
            foreach (var f in reachableFuncs)
            {
                var csName = funcLowerer.Lower(f);
                functionsSrc.AppendLine(csName);
                loweredNames[Norm(f.Name)] = emitter.MangleName(f.Name);
            }

            // Globals referenced by reachable functions.
            var globals = new Dictionary<string, string>();
            foreach (var f in reachableFuncs)
                foreach (var inst in f.Instructions)
                {
                    if (inst is IRGlobalLoadInst gl) globals[gl.GlobalName] = gl.IrType;
                    else if (inst is IRGlobalStoreInst gs) globals[gs.GlobalName] = gs.Value.GetIrType();
                }

            var src = new StringBuilder();
            src.AppendLine("using BabyPenguin.CSharpBackend.Runtime;");
            src.AppendLine();
            src.AppendLine("namespace BabyPenguinCompiled");
            src.AppendLine("{");
            src.AppendLine(typesSrc.TrimEnd());
            src.AppendLine();
            src.AppendLine("    public static class G");
            src.AppendLine("    {");
            foreach (var kv in globals)
                src.AppendLine($"        public static {emitter.CsType(kv.Value)} {mangler.Mangle(kv.Key)};");
            src.AppendLine("    }");
            src.AppendLine();
            src.AppendLine("    public static class Generated");
            src.AppendLine("    {");
            src.AppendLine(functionsSrc.ToString().TrimEnd());
            var externLowerer = new ExternLowerer(emitter);
            foreach (var ext in externLowerer.LowerExterns(externInfos.Values))
                src.AppendLine("        " + ext);

            // Generate __InitVtables: register all class/enum interface method implementations for virtual dispatch.
            var vtableRegs = new List<string>();
            foreach (var cls in model.Classes)
            {
                var csTypeName = mangler.Mangle(CSharpEmitter.Normalize(cls.FullName()));
                foreach (var vt in cls.VTables)
                {
                    foreach (var slot in vt.Slots)
                    {
                        var ifaceFullName = slot.InterfaceSymbol.Parent?.FullName() ?? "";
                        var ifaceIrType = "ref<" + ifaceFullName + ">";
                        var ifaceMethodKey = emitter.MethodCsName(ifaceIrType, slot.InterfaceSymbol.Name);
                        var implName = CSharpEmitter.Normalize(
                            slot.ImplementationSymbol.FullName().Replace(".", "_"));
                        if (!loweredNames.TryGetValue(implName, out var implMethodName))
                        {
                            var sb = new StringBuilder();
                            foreach (var ch in implName)
                                sb.Append((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                                          (ch >= '0' && ch <= '9') || ch == '_' ? ch : '_');
                            implMethodName = sb.ToString();
                            if (implMethodName.Length > 0 && !((implMethodName[0] >= 'a' && implMethodName[0] <= 'z') ||
                                                               (implMethodName[0] >= 'A' && implMethodName[0] <= 'Z') ||
                                                               implMethodName[0] == '_'))
                                implMethodName = "_" + implMethodName;
                        }
                        vtableRegs.Add($"            BabyPenguin.CSharpBackend.Runtime.GlobalState.RegisterVtable(typeof({csTypeName}), \"{ifaceMethodKey}\", typeof(Generated).GetMethod(\"{implMethodName}\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!);");
                    }
                }
            }
            foreach (var enm in model.Enums)
            {
                var csTypeName = mangler.Mangle(CSharpEmitter.Normalize(enm.FullName()));
                foreach (var vt in enm.VTables)
                {
                    foreach (var slot in vt.Slots)
                    {
                        var ifaceFullName = slot.InterfaceSymbol.Parent?.FullName() ?? "";
                        var ifaceIrType = "ref<" + ifaceFullName + ">";
                        var ifaceMethodKey = emitter.MethodCsName(ifaceIrType, slot.InterfaceSymbol.Name);
                        var implName = CSharpEmitter.Normalize(
                            slot.ImplementationSymbol.FullName().Replace(".", "_"));
                        if (!loweredNames.TryGetValue(implName, out var implMethodName))
                        {
                            var sb = new StringBuilder();
                            foreach (var ch in implName)
                                sb.Append((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                                          (ch >= '0' && ch <= '9') || ch == '_' ? ch : '_');
                            implMethodName = sb.ToString();
                            if (implMethodName.Length > 0 && !((implMethodName[0] >= 'a' && implMethodName[0] <= 'z') ||
                                                               (implMethodName[0] >= 'A' && implMethodName[0] <= 'Z') ||
                                                               implMethodName[0] == '_'))
                                implMethodName = "_" + implMethodName;
                        }
                        vtableRegs.Add($"            BabyPenguin.CSharpBackend.Runtime.GlobalState.RegisterVtable(typeof({csTypeName}), \"{ifaceMethodKey}\", typeof(Generated).GetMethod(\"{implMethodName}\", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!);");
                    }
                }
            }
            if (vtableRegs.Count > 0)
            {
                src.AppendLine();
                src.AppendLine("        public static void __InitVtables()");
                src.AppendLine("        {");
                foreach (var reg in vtableRegs)
                    src.AppendLine(reg);
                src.AppendLine("        }");
            }

            src.AppendLine();
            src.AppendLine("        // Fast-path entry: namespace init then initial routines (synchronous, no scheduler).");
            src.AppendLine("        public static void __builtin__main()");
            src.AppendLine("        {");
            if (vtableRegs.Count > 0)
                src.AppendLine("            __InitVtables();");
            foreach (var n in nsNews)
                src.AppendLine($"            {emitter.MangleName(n)}();");
            foreach (var ir in IRModule.EntryFunctions)
                src.AppendLine($"            {emitter.MangleName(ir)}();");
            src.AppendLine("        }");
            src.AppendLine("    }");

            if (standalone)
            {
                // Standalone exe entry: wire a RuntimeGlobal (shared I/O/exit channel), run __builtin__main,
                // write captured stdout, return the exit code. Live output via PrintFunc.
                src.AppendLine("    public static class Program");
                src.AppendLine("    {");
                src.AppendLine("        public static int Main(string[] args)");
                src.AppendLine("        {");
                src.AppendLine("            var g = new BabyPenguin.VirtualMachine.RuntimeGlobal();");
                src.AppendLine("            g.CommandLineArgs = args;");
                src.AppendLine("            g.PrintFunc = (s) => System.Console.Write(s);");
                src.AppendLine("            BabyPenguin.CSharpBackend.Runtime.GlobalState.Global = g;");
                src.AppendLine("            BabyPenguin.CSharpBackend.Runtime.GlobalState.Args = args;");
                src.AppendLine("            try { Generated.__builtin__main(); }");
                src.AppendLine("            catch (BabyPenguin.VirtualMachine.ProgramExitException) { }");
                src.AppendLine("            return g.ExitCode;");
                src.AppendLine("        }");
                src.AppendLine("    }");
            }

            src.AppendLine("}");

            var prog = new CSharpProgram();
            var cs = src.ToString();
            prog.Sources.Add(("Generated.cs", cs));
            try { System.IO.File.WriteAllText("/tmp/bp_cs_dump.cs", cs); } catch { }
            try { System.IO.File.WriteAllText("/home/cauly/Workspace/penguinlang/tmp/bp_cs_dump.cs", cs); } catch { }
            return prog;
        }

        private static List<string> ExtractNamespaceConstructors(IRFunction mainFunc)
        {
            var result = new List<string>();
            foreach (var inst in mainFunc.Instructions)
            {
                if (inst is IRNewInst) break;
                if (inst is IRCallInst c) result.Add(c.FuncName);
                else if (inst is IRCallVoidInst cv) result.Add(cv.FuncName);
            }
            return result;
        }

        /// <summary>Walks a function tracking funptr provenance, yielding call-site signatures for each
        /// extern called (direct CALL, method-ref CALL_FUNC_PTR, func-ref CALL_FUNC_PTR).</summary>
        private static IEnumerable<ExternInfo> ExternCallsOf(IRFunction f, HashSet<string> externSet)
        {
            var fp = new Dictionary<int, (IRValue? Obj, string? MethodTarget, string? FuncName)>();
            foreach (var inst in f.Instructions)
            {
                switch (inst)
                {
                    case IRRdmbrInst r when r.Result.GetIrType() == "funptr":
                    {
                        var inner = CSharpEmitter.InnerTypeName(r.Obj.GetIrType());
                        if (inner != null)
                            fp[Reg(r.Result)] = (r.Obj, CSharpEmitter.Normalize((inner + "." + r.FieldName).Replace(".", "_")), null);
                        break;
                    }
                    case IRAssignInst a when a.Src is IRConstant kc1 && kc1.IrType == "funptr":
                        fp[Reg(a.Dest)] = (null, null, CSharpEmitter.Normalize(kc1.Value)); break;
                    case IRConstInst c when c.Result.GetIrType() == "funptr":
                        fp[Reg(c.Result)] = (null, null, CSharpEmitter.Normalize(c.Value)); break;
                    case IRCallInst c:
                        if (externSet.Contains(CSharpEmitter.Normalize(c.FuncName)))
                            yield return Info(c.FuncName, c.Args.Select(a => a.GetIrType()).ToArray(), c.RetType);
                        break;
                    case IRCallVoidInst c:
                        if (externSet.Contains(CSharpEmitter.Normalize(c.FuncName)))
                            yield return Info(c.FuncName, c.Args.Select(a => a.GetIrType()).ToArray(), "void");
                        break;
                    case IRCallFuncPtrInst c when fp.TryGetValue(Reg(c.FuncPtr), out var p):
                        if (p.MethodTarget != null && externSet.Contains(p.MethodTarget))
                            yield return Info(p.MethodTarget, new[] { p.Obj!.GetIrType() }.Concat(c.Args.Select(a => a.GetIrType())).ToArray(), c.RetType);
                        else if (p.FuncName != null && externSet.Contains(p.FuncName))
                            yield return Info(p.FuncName, c.Args.Select(a => a.GetIrType()).ToArray(), c.RetType);
                        break;
                    case IRCallFuncPtrVoidInst c when fp.TryGetValue(Reg(c.FuncPtr), out var p2):
                        if (p2.MethodTarget != null && externSet.Contains(p2.MethodTarget))
                            yield return Info(p2.MethodTarget, new[] { p2.Obj!.GetIrType() }.Concat(c.Args.Select(a => a.GetIrType())).ToArray(), "void");
                        else if (p2.FuncName != null && externSet.Contains(p2.FuncName))
                            yield return Info(p2.FuncName, c.Args.Select(a => a.GetIrType()).ToArray(), "void");
                        break;
                }
            }
        }

        private static ExternInfo Info(string name, string[] args, string ret) => new()
        {
            Name = CSharpEmitter.Normalize(name),
            ArgIrTypes = args,
            RetIrType = ret
        };

        private static int Reg(IRValue v) => v is IRNamedRegister nr ? nr.Index : ((IRTempRegister)v).Index;

        /// <summary>All callee names from a function: direct calls + instance-method refs (RDMBR method)
        /// + function references (funptr constants). Method refs resolve to the lowered static name.</summary>
        private static IEnumerable<string> CalleesOf(IRFunction f)
        {
            foreach (var inst in f.Instructions)
            {
                switch (inst)
                {
                    case IRCallInst c: yield return CSharpEmitter.Normalize(c.FuncName); break;
                    case IRCallVoidInst c: yield return CSharpEmitter.Normalize(c.FuncName); break;
                    case IRRdmbrInst r when r.Result.GetIrType() == "funptr":
                        var inner = CSharpEmitter.InnerTypeName(r.Obj.GetIrType());
                        if (inner != null) yield return CSharpEmitter.Normalize((inner + "." + r.FieldName).Replace(".", "_"));
                        break;
                    case IRAssignInst a when a.Src is IRConstant kc && kc.IrType == "funptr":
                        yield return CSharpEmitter.Normalize(kc.Value); break;
                    case IRConstInst c when c.Result.GetIrType() == "funptr":
                        yield return CSharpEmitter.Normalize(c.Value); break;
                }
            }
        }

        /// <summary>IR type strings referenced by a function (operands, NEW type names, globals) — roots for type lowering.</summary>
        private static IEnumerable<string> TypesOf(IRFunction f)
        {
            foreach (var inst in f.Instructions)
            {
                switch (inst)
                {
                    case IRNewInst n: yield return n.TypeName; break;
                    case IRNewEnumInst n: yield return $"enum<{n.TypeName}>"; break;
                    case IRGlobalLoadInst g: yield return g.IrType; break;
                    case IRGlobalStoreInst g: yield return g.Value.GetIrType(); break;
                }
                foreach (var v in OperandsOf(inst))
                    yield return v.GetIrType();
            }
        }

        private static IEnumerable<IRValue> OperandsOf(IRInstruction inst)
        {
            switch (inst)
            {
                case IRConstInst c: yield return c.Result; break;
                case IRArgInst a: yield return a.Result; break;
                case IRAssignInst a: yield return a.Dest; if (!(a.Src is IRConstant)) yield return a.Src; break;
                case IRCastInst c: yield return c.Result; yield return c.Operand; break;
                case IRBinOpInst b: yield return b.Result; yield return b.Left; yield return b.Right; break;
                case IRUnaryOpInst u: yield return u.Result; yield return u.Operand; break;
                case IRRdmbrInst r: if (r.Result.GetIrType() != "funptr") yield return r.Result; yield return r.Obj; break;
                case IRWrmbrInst w: yield return w.Obj; yield return w.Value; break;
                case IRBrCondInst b: yield return b.Cond; break;
                case IRRetInst r: if (r.Value != null) yield return r.Value; break;
                case IRCallInst c: foreach (var a in c.Args) yield return a; if (c.RetType != "void") yield return c.ResultValue; break;
                case IRCallVoidInst c: foreach (var a in c.Args) yield return a; break;
                case IRCallFuncPtrInst c: foreach (var a in c.Args) yield return a; if (c.RetType != "void") yield return c.ResultValue; break;
                case IRCallFuncPtrVoidInst c: foreach (var a in c.Args) yield return a; break;
                case IRNewInst n: foreach (var a in n.Args) yield return a; yield return n.Result; break;
                case IRNewEnumInst n: if (n.Payload != null) yield return n.Payload!; yield return n.Result; break;
                case IRIsEnumInst i: yield return i.EnumValue; yield return i.VariantIdx; yield return i.Result; break;
                case IRRdenumInst r: yield return r.Result; yield return r.EnumValue; break;
                case IRGlobalLoadInst g: yield return g.Result; break;
                case IRGlobalStoreInst g: break;
            }
        }

        private HashSet<string> ReachableFunctions(List<string> seeds, HashSet<string> externSet, Dictionary<string, IRFunction> funcByNorm)
        {
            var seen = new HashSet<string>();
            var queue = new Queue<string>();
            foreach (var s in seeds.Where(s => !externSet.Contains(s) && funcByNorm.ContainsKey(s) && seen.Add(s)))
                queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!funcByNorm.TryGetValue(cur, out var f) || f == null) continue;
                foreach (var target in CalleesOf(f))
                    if (!externSet.Contains(target) && funcByNorm.ContainsKey(target) && seen.Add(target))
                        queue.Enqueue(target);
            }
            return seen;
        }
    }
}
