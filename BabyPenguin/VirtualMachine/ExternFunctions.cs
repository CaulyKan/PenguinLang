using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using Mono.Cecil;
using BabyPenguin.Type;
using BabyPenguin.SemanticInterface;
using System.Collections;
using PenguinLangParser;
using PenguinLangParser.SyntaxNodes;

namespace BabyPenguin.VirtualMachine
{
    class ExternFunctions
    {
        public static void Build(BabyPenguinVM vm)
        {
            AddPrint(vm);
            AddCopy(vm);
            AddAtmoic(vm);
            AddList(vm);
            AddRoutineContext(vm);

            AddFile(vm);
            AddArgs(vm);
            AddStringBuilder(vm);
            // AddMap(vm);
            AddEmperorPenguin(vm);
        }

        public static void AddPrint(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.print", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s);
            });

            vm.Global.RegisterExternFunction("__builtin.println", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                vm.Global.Print(s, true);
            });

            vm.Global.RegisterExternFunction("__builtin.eprint", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.Write(s);
            });

            vm.Global.RegisterExternFunction("__builtin.eprintln", (result, args) =>
            {
                var s = args[0].As<BasicRuntimeValue>().StringValue;
                Console.Error.WriteLine(s);
            });

            vm.Global.RegisterExternFunction("__builtin.exit", Exit);
        }

        private static IEnumerable<RuntimeBreak> Exit(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            frame.Global.ExitCode = args[0].As<BasicRuntimeValue>().I32Value;
            yield return new RuntimeBreak(RuntimeBreakReason.Exited, frame);
        }

        public static void AddCopy(BabyPenguinVM vm)
        {
            foreach (var ICopy in vm.Model.ResolveTypeNode("__builtin.ICopy<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(ICopy.FullName() + ".copy", (result, args) =>
                {
                    var v = args[0];
                    if (v is BasicRuntimeValue)
                        result!.AssignFrom(v.Clone());
                });
            }
        }

        public static void AddAtmoic(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.AtomicI64.swap", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var other_value = args[1].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.Exchange(ref atomic.I64Value, other_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.compare_exchange", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var current_value = args[1].As<BasicRuntimeValue>().I64Value;
                var new_value = args[2].As<BasicRuntimeValue>().I64Value;
                var org = Interlocked.CompareExchange(ref atomic.I64Value, new_value, current_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = org;
            });

            vm.Global.RegisterExternFunction("__builtin.AtomicI64.fetch_add", (result, args) =>
            {
                var atomic = args[0].As<ReferenceRuntimeValue>().Fields["value"].As<BasicRuntimeValue>();
                var add_value = (Int64)args[1].As<BasicRuntimeValue>().I64Value;
                var res = Interlocked.Add(ref atomic.I64Value, add_value);
                result!.As<BasicRuntimeSymbol>().BasicValue.I64Value = res;
            });
        }

        public static void AddList(BabyPenguinVM vm)
        {
            foreach (var queue in vm.Model.ResolveTypeNode("__builtin.Queue<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(queue.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new Queue<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".enqueue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    q!.Enqueue(args[1]);
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".dequeue", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Dequeue();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".peek", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    if (q!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = q.Peek();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(queue.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var q = impl.ExternImplenmentationValue as Queue<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)q!.Count;
                });
            }

            foreach (var list in vm.Model.ResolveTypeNode("__builtin.List<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(list.FullName() + ".new", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    impl.ExternImplenmentationValue = new List<IRuntimeValue>();
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".push", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    l!.Add(args[1]);
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".pop", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if (l!.Count == 0)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.Last();
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                        l.RemoveAt(l.Count - 1);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".at", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = null;
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
                    }
                    else
                    {
                        result!.As<EnumRuntimeSymbol>().EnumValue.ContainingValue = l.ElementAt((int)idx);
                        result.As<EnumRuntimeSymbol>().EnumValue.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".remove", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var idx = args[1].As<BasicRuntimeValue>().U64Value;
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    if ((ulong)l!.Count <= idx)
                    {
                        // remove out of range
                    }
                    else
                    {
                        l.RemoveAt((int)idx);
                    }
                });

                vm.Global.RegisterExternFunction(list.FullName() + ".size", (result, args) =>
                {
                    var impl = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
                    var l = impl.ExternImplenmentationValue as List<IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)l!.Count;
                });
            }
        }

        private static IEnumerable<RuntimeBreak> RoutineContextCall(RuntimeFrame frame, IRuntimeSymbol? resultVar, List<IRuntimeValue> args)
        {
            var target = args[0].As<ReferenceRuntimeValue>().Fields["target"].As<FunctionRuntimeValue>() ?? throw new NotImplementedException();
            var frameRuntimeVar = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>();
            RuntimeFrameResult? frameResult = null;
            if (frameRuntimeVar.ExternImplenmentationValue is RuntimeFrame f)
            {
                foreach (var res in f.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }
            else
            {
                // target is a async_fun<T>
                var funcSymbol = target?.FunctionSymbol as FunctionSymbol ?? throw new BabyPenguinRuntimeException("cant find function symbol on " + target!.ToString());
                List<IRuntimeValue> funcArguments = [];
                if (target!.TypeInfo.GenericArguments.Count > 1 && target.Owner is not NotInitializedRuntimeValue)
                {
                    funcArguments.Add(target.Owner);
                }
                else if (target.TypeInfo.GenericArguments.Count == 1 && target.Owner is NotInitializedRuntimeValue)
                {
                    // no arguments
                }

                var newFrame = new RuntimeFrame(funcSymbol.CodeContainer, frame.Global, funcArguments, frame);
                frameRuntimeVar.ExternImplenmentationValue = newFrame;
                foreach (var res in newFrame.Run())
                {
                    if (res.IsLeft)
                        yield return res.Left!;
                    else
                        frameResult = res.Right!;
                }
            }

            if (frameResult!.ReturnStatus == ReturnStatus.YieldFinished || frameResult!.ReturnStatus == ReturnStatus.Finished)
            {
                frame.ChildFrame = null;
                frameRuntimeVar.ExternImplenmentationValue = null;
            }

            var routineContextResult = args[0].As<ReferenceRuntimeValue>().Fields["result"].As<EnumRuntimeValue>();
            if (frameResult.ReturnValue != null)
            {
                routineContextResult.ContainingValue = frameResult.ReturnValue.Value;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0;
            }
            else
            {
                routineContextResult.ContainingValue = null;
                routineContextResult.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1;
            }

            resultVar!.As<BasicRuntimeSymbol>().BasicValue.I64Value = (int)frameResult!.ReturnStatus;
        }

        public static void AddRoutineContext(BabyPenguinVM vm)
        {

            foreach (var routineContext in vm.Model.ResolveTypeNode("__builtin.RoutineContext<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(routineContext.FullName() + ".call", RoutineContextCall);
            }
        }

        private static void AddFile(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.file_read_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = System.IO.File.ReadAllText(path);
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = content;
            });

            vm.Global.RegisterExternFunction("__builtin.file_write_text", (result, args) =>
            {
                var path = args[0].As<BasicRuntimeValue>().StringValue;
                var content = args[1].As<BasicRuntimeValue>().StringValue;
                System.IO.File.WriteAllText(path, content);
            });
        }
        private static void AddArgs(BabyPenguinVM vm)
        {
            vm.Global.RegisterExternFunction("__builtin.__args_init", (result, args) =>
            {
                var commandLineArgs = vm.Global.CommandLineArgs;
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var list = self.ExternImplenmentationValue as List<IRuntimeValue>;
                var stringType = vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable);

                foreach (var arg in commandLineArgs)
                {
                    var basicStringValue = new BasicRuntimeValue(stringType);
                    basicStringValue.StringValue = arg;
                    list!.Add(basicStringValue);
                }
            });
        }

        private static void AddStringBuilder(BabyPenguinVM vm)
        {
            var stringBuilderType = vm.Model.ResolveTypeNode("__builtin.StringBuilder");
            if (stringBuilderType == null) return; // 如果代码中没用到，就跳过

            // 注册 StringBuilder.new()
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".new", (result, args) =>
            {
                // 1. 获取 'this' 实例
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                // 2. 创建一个 C# StringBuilder 并附加
                self.ExternImplenmentationValue = new System.Text.StringBuilder();
            });

            // 注册 StringBuilder.append(text: string)
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".append", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 获取要追加的文本
                var textToAppend = args[1].As<BasicRuntimeValue>().StringValue;

                // 3. 执行 append 操作
                sb!.Append(textToAppend);
            });

            // 注册 StringBuilder.to_string() -> string
            vm.Global.RegisterExternFunction(stringBuilderType.FullName() + ".to_string", (result, args) =>
            {
                // 1. 获取 'this' 实例和底层的 StringBuilder
                var self = args[0].As<ReferenceRuntimeValue>().Fields["__impl"].As<ReferenceRuntimeValue>(); ;
                var sb = self.ExternImplenmentationValue as System.Text.StringBuilder;

                // 2. 将结果赋给返回值
                result!.As<BasicRuntimeSymbol>().BasicValue.StringValue = sb!.ToString();
            });
        }

        private static void AddEmperorPenguin(BabyPenguinVM vm)
        {
            var emperorType = vm.Model.ResolveTypeNode("EmperorPenguin.Compiler");
            if (emperorType == null) return;

            // Ensure AST types are available (optional)
            var cuTypeNode = vm.Model.ResolveTypeNode("ast.CompilationUnit");
            if (cuTypeNode == null)
            {
                var astPath1 = Path.Combine(Environment.CurrentDirectory, "EmperorPenguin", "ast.penguin");
                if (File.Exists(astPath1))
                    vm.Model.AddSource(File.ReadAllText(astPath1), astPath1);
                cuTypeNode = vm.Model.ResolveTypeNode("ast.CompilationUnit");
            }

            // parseAST → returns ast.CompilationUnit
            vm.Global.RegisterExternFunction(emperorType.FullName() + ".parseAST", (result, args) =>
            {
                var name = args[1].As<BasicRuntimeValue>().StringValue;
                var code = args[2].As<BasicRuntimeValue>().StringValue;

                var reporter = vm.Model.Reporter;
                var context = PenguinParser.Parse(code, name, reporter);
                var syntaxCompiler = new SyntaxCompiler(name, context, reporter);
                syntaxCompiler.Compile();

                var cuType = vm.Model.ResolveTypeNode("ast.CompilationUnit")!.ToType(Mutability.Immutable);
                var declListTypeNode = vm.Model.ResolveTypeNode("__builtin.List<ast.IDeclaration>");
                var declListType = declListTypeNode!.ToType(Mutability.Mutable);

                var cuInstance = new ReferenceRuntimeValue(cuType, new Dictionary<string, IRuntimeValue>());

                var declListInstance = new ReferenceRuntimeValue(declListType, new Dictionary<string, IRuntimeValue>());
                declListInstance.Fields["__impl"] = new ReferenceRuntimeValue(declListType, new Dictionary<string, IRuntimeValue>());
                declListInstance.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue = new List<IRuntimeValue>();

                var userNs = syntaxCompiler.Namespaces.FirstOrDefault();
                if (userNs != null)
                {
                    var ns = userNs;
                    var nsType = vm.Model.ResolveTypeNode("ast.NamespaceDefinition")!.ToType(Mutability.Immutable);
                    var nsInstance = new ReferenceRuntimeValue(nsType, new Dictionary<string, IRuntimeValue>());

                    var idType = vm.Model.ResolveTypeNode("ast.Identifier")!.ToType(Mutability.Immutable);
                    var idInstance = new ReferenceRuntimeValue(idType, new Dictionary<string, IRuntimeValue>());
                    idInstance.Fields["name"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable)) { StringValue = ns.Name };
                    nsInstance.Fields["identifier"] = idInstance;

                    var nsDeclListTypeNode = vm.Model.ResolveTypeNode("__builtin.List<ast.IDeclaration>");
                    var nsDeclListType = nsDeclListTypeNode!.ToType(Mutability.Mutable);
                    var nsDeclListInstance = new ReferenceRuntimeValue(nsDeclListType, new Dictionary<string, IRuntimeValue>());
                    nsDeclListInstance.Fields["__impl"] = new ReferenceRuntimeValue(nsDeclListType, new Dictionary<string, IRuntimeValue>());
                    nsDeclListInstance.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue = new List<IRuntimeValue>();

                    foreach (var decl in ns.InitialRoutines)
                    {
                        var initType = vm.Model.ResolveTypeNode("ast.InitialRoutineDefinition")!.ToType(Mutability.Immutable);
                        var initInstance = new ReferenceRuntimeValue(initType, new Dictionary<string, IRuntimeValue>());

                        var optIdTypeNode = vm.Model.ResolveTypeNode("__builtin.Option<ast.Identifier>");
                        var optIdType = optIdTypeNode!.ToType(Mutability.Immutable);
                        var optIdEnum = new EnumRuntimeValue(optIdType, new ReferenceRuntimeValue(optIdType, new Dictionary<string, IRuntimeValue>()), null);
                        if (!string.IsNullOrEmpty(decl.Name))
                        {
                            var idVal = new ReferenceRuntimeValue(idType, new Dictionary<string, IRuntimeValue>());
                            idVal.Fields["name"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable)) { StringValue = decl.Name };
                            optIdEnum.ContainingValue = idVal;
                            optIdEnum.FieldsValue.Fields["_value"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.I32.ToType(Mutability.Immutable)) { I32Value = 0 };
                        }
                        else
                        {
                            optIdEnum.FieldsValue.Fields["_value"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.I32.ToType(Mutability.Immutable)) { I32Value = 1 };
                        }
                        initInstance.Fields["identifier"] = optIdEnum;

                        var blockType = vm.Model.ResolveTypeNode("ast.CodeBlock")!.ToType(Mutability.Immutable);
                        var blockInstance = new ReferenceRuntimeValue(blockType, new Dictionary<string, IRuntimeValue>());

                        var blockItemsListTypeNode = vm.Model.ResolveTypeNode("__builtin.List<ast.ISyntaxNode>");
                        var blockItemsListType = blockItemsListTypeNode!.ToType(Mutability.Mutable);
                        var blockItemsList = new ReferenceRuntimeValue(blockItemsListType, new Dictionary<string, IRuntimeValue>());
                        blockItemsList.Fields["__impl"] = new ReferenceRuntimeValue(blockItemsListType, new Dictionary<string, IRuntimeValue>());
                        var items = new List<IRuntimeValue>();
                        blockItemsList.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue = items;

                        foreach (var item in decl.CodeBlock!.BlockItems)
                        {
                            if (item.Type == PenguinLangParser.SyntaxNodes.CodeBlockItem.CodeBlockItemType.Statement && item.Statement!.StatementType == PenguinLangParser.SyntaxNodes.Statement.Type.ExpressionStatement)
                            {
                                var exprStmtType = vm.Model.ResolveTypeNode("ast.ExpressionStatement")!.ToType(Mutability.Immutable);
                                var exprStmt = new ReferenceRuntimeValue(exprStmtType, new Dictionary<string, IRuntimeValue>());

                                var fce = item.Statement!.ExpressionStatement!.Expression as PenguinLangParser.SyntaxNodes.FunctionCallExpression;
                                if (fce != null)
                                {
                                    var callType = vm.Model.ResolveTypeNode("ast.FunctionCallExpression")!.ToType(Mutability.Immutable);
                                    var callInst = new ReferenceRuntimeValue(callType, new Dictionary<string, IRuntimeValue>());

                                    var calleeId = new ReferenceRuntimeValue(idType, new Dictionary<string, IRuntimeValue>());
                                    var calleeName = fce.PrimaryExpression is PenguinLangParser.SyntaxNodes.PrimaryExpression pe && pe.Identifier != null ? pe.Identifier.BuildText() : "";
                                    calleeId.Fields["name"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable)) { StringValue = calleeName };
                                    callInst.Fields["callee"] = calleeId;

                                    var argsListTypeNode = vm.Model.ResolveTypeNode("__builtin.List<ast.IExpression>");
                                    var argsListType = argsListTypeNode!.ToType(Mutability.Mutable);
                                    var argsList = new ReferenceRuntimeValue(argsListType, new Dictionary<string, IRuntimeValue>());
                                    argsList.Fields["__impl"] = new ReferenceRuntimeValue(argsListType, new Dictionary<string, IRuntimeValue>());
                                    var argsData = new List<IRuntimeValue>();
                                    argsList.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue = argsData;

                                    foreach (var a in fce.ArgumentsExpression)
                                    {
                                        if (a is PenguinLangParser.SyntaxNodes.PrimaryExpression ape && ape.PrimaryExpressionType == PenguinLangParser.SyntaxNodes.PrimaryExpression.Type.StringLiteral)
                                        {
                                            var litType = vm.Model.ResolveTypeNode("ast.LiteralExpression")!.ToType(Mutability.Immutable);
                                            var litInst = new ReferenceRuntimeValue(litType, new Dictionary<string, IRuntimeValue>());
                                            litInst.Fields["value"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable)) { StringValue = ape.Literal! };
                                            argsData.Add(litInst);
                                        }
                                        else
                                        {
                                            var litType = vm.Model.ResolveTypeNode("ast.LiteralExpression")!.ToType(Mutability.Immutable);
                                            var litInst = new ReferenceRuntimeValue(litType, new Dictionary<string, IRuntimeValue>());
                                            litInst.Fields["value"] = new BasicRuntimeValue(vm.Model.BasicTypeNodes.String.ToType(Mutability.Immutable)) { StringValue = a.BuildText() };
                                            argsData.Add(litInst);
                                        }
                                    }

                                    callInst.Fields["arguments"] = argsList;
                                    exprStmt.Fields["expression"] = callInst;
                                    items.Add(exprStmt);
                                }
                            }
                        }

                        blockInstance.Fields["block_items"] = blockItemsList;
                        initInstance.Fields["code_block"] = blockInstance;

                        var nsDecls = nsDeclListInstance.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue as List<IRuntimeValue>;
                        nsDecls!.Add(initInstance);
                    }

                    nsInstance.Fields["declarations"] = nsDeclListInstance;
                    var cuDecls = declListInstance.Fields["__impl"].As<ReferenceRuntimeValue>().ExternImplenmentationValue as List<IRuntimeValue>;
                    cuDecls!.Add(nsInstance);
                }

                cuInstance.Fields["declarations"] = declListInstance;
                result!.AssignFrom(cuInstance);
            });

            // ast.printAst 由 PenguinLang 实现，无需外部注册
        }


        private static void AddMap(BabyPenguinVM vm)
        {
            var mapNode = vm.Model.ResolveTypeNode("__builtin.Map<?,?>");
            if (mapNode == null) return;

            foreach (var mapInstance in mapNode.GenericInstances)
            {
                var mapFullName = mapInstance.FullName();

                // new()
                vm.Global.RegisterExternFunction(mapFullName + ".new", (result, args) =>
                {
                    args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue = new Dictionary<IRuntimeValue, IRuntimeValue>(new RuntimeValueComparer());
                });

                // set(key, value)
                vm.Global.RegisterExternFunction(mapFullName + ".set", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    map![args[1]] = args[2];
                });

                // get(key) -> Option<V>
                vm.Global.RegisterExternFunction(mapFullName + ".get", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    var optionEnum = result!.As<EnumRuntimeSymbol>().EnumValue; // Changed from EnumRuntimeSymbol to EnumRuntimeValue
                    if (map!.TryGetValue(args[1], out var value))
                    {
                        // Some(value)
                        optionEnum.ContainingValue = value;
                        optionEnum.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0; // Assuming 0 is Some
                    }
                    else
                    {
                        // None
                        optionEnum.ContainingValue = null;
                        optionEnum.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1; // Assuming 1 is None
                    }
                });

                // remove(key) -> bool
                vm.Global.RegisterExternFunction(mapFullName + ".remove", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = map!.Remove(args[1]);
                });

                // contains_key(key) -> bool
                vm.Global.RegisterExternFunction(mapFullName + ".contains_key", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.BoolValue = map!.ContainsKey(args[1]);
                });

                // size() -> u64
                vm.Global.RegisterExternFunction(mapFullName + ".size", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    result!.As<BasicRuntimeSymbol>().BasicValue.U64Value = (ulong)map!.Count;
                });

                // keys() -> mut IIterator<K>
                vm.Global.RegisterExternFunction(mapFullName + ".keys", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    var iteratorTypeNode = vm.Model.ResolveTypeNode("__builtin.IIterator<?>");
                    var keyType = mapInstance.GenericArguments[0]; // 获取 K 的实际类型
                    var specializedIteratorType = iteratorTypeNode!.Specialize(new List<IType> { keyType }).ToType(Mutability.Mutable);

                    var iteratorInstance = new ReferenceRuntimeValue(specializedIteratorType, new Dictionary<string, IRuntimeValue>());
                    iteratorInstance.ExternImplenmentationValue = map!.Keys.GetEnumerator();
                    result!.AssignFrom(iteratorInstance);
                });

                // values() -> mut IIterator<V>
                vm.Global.RegisterExternFunction(mapFullName + ".values", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    var iteratorTypeNode = vm.Model.ResolveTypeNode("__builtin.IIterator<?>");
                    var valueType = mapInstance.GenericArguments[1]; // 获取 V 的实际类型
                    var specializedIteratorType = iteratorTypeNode!.Specialize(new List<IType> { valueType }).ToType(Mutability.Mutable);

                    var iteratorInstance = new ReferenceRuntimeValue(specializedIteratorType, new Dictionary<string, IRuntimeValue>());
                    iteratorInstance.ExternImplenmentationValue = map!.Values.GetEnumerator();
                    result!.AssignFrom(iteratorInstance);
                });

                // iterator() -> mut IIterator<Pair<K, V>> (for IIteratable impl)
                vm.Global.RegisterExternFunction(mapFullName + ".iterator", (result, args) =>
                {
                    var map = args[0].As<ReferenceRuntimeValue>().ExternImplenmentationValue as Dictionary<IRuntimeValue, IRuntimeValue>;
                    var iteratorTypeNode = vm.Model.ResolveTypeNode("__builtin.IIterator<?>");
                    var pairTypeNode = vm.Model.ResolveTypeNode("__builtin.Pair<?,?>");
                    var keyType = mapInstance.GenericArguments[0];
                    var valueType = mapInstance.GenericArguments[1];
                    var specializedPairType = pairTypeNode!.Specialize(new List<IType> { keyType, valueType }).ToType(Mutability.Immutable);
                    var specializedIteratorType = iteratorTypeNode!.Specialize(new List<IType> { specializedPairType }).ToType(Mutability.Mutable);

                    var iteratorInstance = new ReferenceRuntimeValue(specializedIteratorType, new Dictionary<string, IRuntimeValue>());
                    iteratorInstance.ExternImplenmentationValue = map!.GetEnumerator();
                    result!.AssignFrom(iteratorInstance);
                });
            }

            // Register IIterator.next() for all specialized iterators
            foreach (var iteratorInstance in vm.Model.ResolveTypeNode("__builtin.IIterator<?>")!.GenericInstances)
            {
                vm.Global.RegisterExternFunction(iteratorInstance.FullName() + ".next", (result, args) =>
                {
                    var self = args[0].As<ReferenceRuntimeValue>();
                    var enumerator = self.ExternImplenmentationValue as IEnumerator; // Changed to non-generic IEnumerator
                    var optionTypeNode = vm.Model.ResolveTypeNode("__builtin.Option<?>经济");
                    var containedType = iteratorInstance.GenericArguments[0]; // 获取迭代器包含的类型 (K, V, 或 Pair<K,V>)
                    var specializedOptionType = optionTypeNode!.Specialize(new List<IType> { containedType }).ToType(Mutability.Immutable);

                    var optionInstance = new EnumRuntimeValue(specializedOptionType, new ReferenceRuntimeValue(specializedOptionType, new Dictionary<string, IRuntimeValue>()), null);

                    if (enumerator!.MoveNext())
                    {
                        // Some(current)
                        IRuntimeValue currentValue;
                        if (enumerator.Current is KeyValuePair<IRuntimeValue, IRuntimeValue> kvp) // For Map.iterator()
                        {
                            var pairTypeNode = vm.Model.ResolveTypeNode("__builtin.Pair<?,?>");
                            var keyType = kvp.Key.TypeInfo;
                            var valueType = kvp.Value.TypeInfo;
                            var specializedPairType = pairTypeNode!.Specialize(new List<IType> { keyType, valueType }).ToType(Mutability.Immutable);

                            var pairInstance = new ReferenceRuntimeValue(specializedPairType, new Dictionary<string, IRuntimeValue>());
                            pairInstance.Fields["first"] = kvp.Key;
                            pairInstance.Fields["second"] = kvp.Value;
                            currentValue = pairInstance;
                        }
                        else // For List.iter(), Map.keys(), Map.values()
                        {
                            currentValue = (IRuntimeValue)enumerator.Current;
                        }

                        optionInstance.ContainingValue = currentValue;
                        optionInstance.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 0; // Assuming 0 is Some
                    }
                    else
                    {
                        // None
                        optionInstance.ContainingValue = null;
                        optionInstance.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value = 1; // Assuming 1 is None
                    }
                    result!.AssignFrom(optionInstance);
                });
            }
        }
    }

    // 需要一个辅助类来比较 IRuntimeValue
    public class RuntimeValueComparer : IEqualityComparer<IRuntimeValue>
    {
        public bool Equals(IRuntimeValue? x, IRuntimeValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // 对于 BasicRuntimeValue，比较其内部值
            if (x is BasicRuntimeValue bx && y is BasicRuntimeValue by)
            {
                if (bx.TypeInfo.Type != by.TypeInfo.Type) return false;
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue == by.BoolValue,
                    TypeEnum.U8 => bx.U8Value == by.U8Value,
                    TypeEnum.U16 => bx.U16Value == by.U16Value,
                    TypeEnum.U32 => bx.U32Value == by.U32Value,
                    TypeEnum.U64 => bx.U64Value == by.U64Value,
                    TypeEnum.I8 => bx.I8Value == by.I8Value,
                    TypeEnum.I16 => bx.I16Value == by.I16Value,
                    TypeEnum.I32 => bx.I32Value == by.I32Value,
                    TypeEnum.I64 => bx.I64Value == by.I64Value,
                    TypeEnum.Float => bx.FloatValue == by.FloatValue,
                    TypeEnum.Double => bx.DoubleValue == by.DoubleValue,
                    TypeEnum.String => bx.StringValue == by.StringValue,
                    TypeEnum.Char => bx.CharValue == by.CharValue,
                    _ => false // 其他类型不应作为基本值进行比较
                };
            }
            // 对于 ReferenceRuntimeValue，比较其引用 ID
            else if (x is ReferenceRuntimeValue rx && y is ReferenceRuntimeValue ry)
            {
                return rx.RefId == ry.RefId;
            }
            // 对于 FunctionRuntimeValue，比较其函数符号
            else if (x is FunctionRuntimeValue fx && y is FunctionRuntimeValue fy)
            {
                return fx.FunctionSymbol.FullName() == fy.FunctionSymbol.FullName();
            }
            // 对于 EnumRuntimeValue，比较其值和类型
            else if (x is EnumRuntimeValue ex && y is EnumRuntimeValue ey)
            {
                return ex.TypeInfo.FullName() == ey.TypeInfo.FullName() &&
                       ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value == ey.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value &&
                       Equals(ex.ContainingValue, ey.ContainingValue);
            }

            return false;
        }

        public int GetHashCode(IRuntimeValue obj)
        {
            if (obj is null) return 0;

            if (obj is BasicRuntimeValue bx)
            {
                return bx.TypeInfo.Type switch
                {
                    TypeEnum.Bool => bx.BoolValue.GetHashCode(),
                    TypeEnum.U8 => bx.U8Value.GetHashCode(),
                    TypeEnum.U16 => bx.U16Value.GetHashCode(),
                    TypeEnum.U32 => bx.U32Value.GetHashCode(),
                    TypeEnum.U64 => bx.U64Value.GetHashCode(),
                    TypeEnum.I8 => bx.I8Value.GetHashCode(),
                    TypeEnum.I16 => bx.I16Value.GetHashCode(),
                    TypeEnum.I32 => bx.I32Value.GetHashCode(),
                    TypeEnum.I64 => bx.I64Value.GetHashCode(),
                    TypeEnum.Float => bx.FloatValue.GetHashCode(),
                    TypeEnum.Double => bx.DoubleValue.GetHashCode(),
                    TypeEnum.String => bx.StringValue.GetHashCode(),
                    TypeEnum.Char => bx.CharValue.GetHashCode(),
                    _ => obj.GetHashCode() // Fallback
                };
            }
            else if (obj is ReferenceRuntimeValue rx)
            {
                return rx.RefId.GetHashCode();
            }
            else if (obj is FunctionRuntimeValue fx)
            {
                return fx.FunctionSymbol.FullName().GetHashCode();
            }
            else if (obj is EnumRuntimeValue ex)
            {
                return HashCode.Combine(ex.TypeInfo.FullName().GetHashCode(),
                                        ex.FieldsValue.Fields["_value"].As<BasicRuntimeValue>().I32Value.GetHashCode(),
                                        ex.ContainingValue?.GetHashCode() ?? 0);
            }

            return obj.GetHashCode();
        }
    }
}
