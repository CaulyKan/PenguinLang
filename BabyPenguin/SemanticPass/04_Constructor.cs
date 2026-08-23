namespace BabyPenguin.SemanticPass
{

    public class ConstructorPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process()
        {
            var classes = Model.FindAll(o => o is IClassNode).Cast<IClassNode>().ToList();
            foreach (var cls in classes)
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Class constructor pass for class '{cls.Name}' is skipped now because it is generic");
                }
                else
                {
                    InitClassConstructor(cls);
                }
            }
            foreach (var cls in classes)
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                {
                }
                else
                {
                    ProcessClass(cls);
                }
            }

            var interfaces = Model.FindAll(o => o is IInterfaceNode).Cast<IInterfaceNode>().ToList();
            foreach (var intf in interfaces)
            {
                if (intf.IsGeneric && !intf.IsSpecialized)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Interface constructor pass for interface '{intf.Name}' is skipped now because it is generic");
                }
                else
                {
                    InitInterfaceConstructor(intf);
                }
            }
            foreach (var intf in interfaces)
            {
                if (intf.IsGeneric && !intf.IsSpecialized)
                {
                }
                else
                {
                    ProcessInterface(intf);
                }
            }

            foreach (var obj in Model.FindAll(o => o is INamespace).ToList())
            {
                Process(obj);
            }
        }

        public void Process(ISemanticNode obj)
        {
            if (obj.PassIndex >= PassIndex)
                return;

            if (obj is IClassNode cls)
            {
                if (cls.IsGeneric && !cls.IsSpecialized)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Class constructor pass for class '{cls.Name}' is skipped now because it is generic");
                }
                else
                {
                    InitClassConstructor(cls);
                    ProcessClass(cls);
                }
            }

            if (obj is IInterfaceNode intf)
            {
                if (intf.IsGeneric && !intf.IsSpecialized)
                {
                    Model.Reporter.Write(DiagnosticLevel.Debug, $"Interface constructor pass for interface '{intf.Name}' is skipped now because it is generic");
                }
                else
                {
                    InitInterfaceConstructor(intf);
                    ProcessInterface(intf);
                }
            }

            if (obj is INamespace ns)
            {
                ProcessNamespace(ns);
            }

            obj.PassIndex = PassIndex;
        }

        private void ResolveUnresolvedSymbols(ICodeContainer constructorBody, ISymbolContainer symbolContainer)
        {
            foreach (var symbol in symbolContainer.Symbols.OfType<VariableSymbol>().Where(s => s.TypeInferStatus != TypeInferStatus.ExplicitTyped))
            {
                throw new BabyPenguinException($"Variable '{symbol.Name}' type was not inferred in TypeInferencePass", symbol.SourceLocation, code: ErrorCode.E_TYPE_INFERENCE);
            }
        }

        public void ProcessNamespace(INamespace ns)
        {
            var sourceLocation = ns.SyntaxNode?.SourceLocation.StartLocation ?? SourceLocation.Empty();

            var constructor = Model.ResolveSymbol(ns.FullName() + ".new", checkImportedNamespaces: false) as FunctionSymbol;

            if (constructor == null)
            {

                ns.Constructor = new Function(Model, "new", [], Model.BasicTypeNodes.Void.ToType(Mutability.Immutable), sourceLocation, false, false);
                ns.AddFunction(ns.Constructor);
                Model.CatchUp(ns.Constructor);
                constructor = ns.Constructor.FunctionSymbol;
            }

            // ResolveUnresolvedSymbols(constructor!.CodeContainer, ns);

            if (ns.SyntaxNode is NamespaceDefinition syntaxNode)
            {
                foreach (var decl in syntaxNode.Declarations)
                {
                    if (decl.InitializeExpression != null)
                    {
                        var symbol = Model.ResolveSymbol(decl.Name, scope: ns, requireSymbolTypeInferred: false) ??
                            throw new BabyPenguinException($"Cant resolve symbol '{decl.Name}' in namespace '{ns.FullName()}'", decl.SourceLocation, code: ErrorCode.E_RESOLVE_SYMBOL);

                        if (symbol.TypeInferStatus != TypeInferStatus.ExplicitTyped)
                            constructor!.CodeContainer.InferVariableType(symbol);

                        constructor!.CodeContainer.AddExpression(decl.InitializeExpression, true, symbol);
                    }
                }
            }
        }


        public void InitClassConstructor(IClassNode cls)
        {
            var sourceLocation = cls.SyntaxNode?.SourceLocation.StartLocation ?? SourceLocation.Empty();

            if (cls.Functions.Find(i => i.Name == "new") is IFunction constructorFunc)
            {
                if (constructorFunc.Parameters.Count > 0 &&
                    constructorFunc.Parameters[0].Type.TypeNode!.FullName() == cls.FullName() && constructorFunc.Parameters[0].Name == "this")
                {
                    if (constructorFunc.Parameters[0].Type.IsMutable == Mutability.Auto)
                    {
                        constructorFunc.Parameters[0] = new FunctionParameter("this", constructorFunc.Parameters[0].Type.WithMutability(Mutability.Immutable), 0);
                    }
                    cls.Constructor = constructorFunc;
                    sourceLocation = constructorFunc.SourceLocation;
                }
                else
                {
                    throw new BabyPenguinException($"Constructor function of class '{cls.Name}' should have first parameter 'this' with type '{cls.FullName()}'", sourceLocation, code: ErrorCode.E_INTERNAL);
                }
            }
            else
            {
                List<FunctionParameter> param = [new FunctionParameter("this", cls.ToType(Mutability.Mutable), 0)];
                cls.Constructor = new Function(Model, "new", param, Model.BasicTypeNodes.Void.ToType(Mutability.Immutable), sourceLocation, false, false);
                cls.AddFunction(cls.Constructor);
                Model.CatchUp(cls.Constructor);
            }

        }

        public void ProcessClass(IClassNode cls)
        {
            if (cls.SyntaxNode is ClassDefinition syntaxNode)
            {
                var constructorBody = (cls.Constructor as ICodeContainer)!;
                // ResolveUnresolvedSymbols(constructorBody, cls);
                foreach (var decl in syntaxNode.Declarations)
                {
                    InitializeVariable(new(cls), constructorBody, decl);
                }

                InitializePorts(cls, syntaxNode.Ports, constructorBody);

                InvokeClassConstructs(cls, constructorBody);

                SpawnClassInitialRoutines(cls, constructorBody);
            }
        }

        /// <summary>
        /// RTL ports become reference fields of type mut __builtin.IChannel&lt;T&gt;
        /// (registered in PortRegistry with their direction). Outputs are wired to
        /// a private LatestChannel (a write-only-driver port with a zero-value
        /// slot); inputs with an explicit default bind a ConstantSource, inputs
        /// without one stay uninitialized (unconnected input — wait fails fast).
        /// </summary>
        private void InitializePorts(IClassNode cls, List<PortDefinition> ports, ICodeContainer constructorBody)
        {
            var thisSymbol = Model.ResolveShortSymbol("this", scope: constructorBody);
            foreach (var port in ports)
            {
                var payloadType = Model.ResolveType(port.Type!.Name, scope: cls)
                    ?? throw new BabyPenguinException($"Cant resolve port type '{port.Type.Name}'", port.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                var payloadAuto = payloadType.WithMutability(Mutability.Auto);
                var channelType = Model.ResolveType($"__builtin.IChannel<{payloadAuto.FullName()}>")
                    ?? throw new BabyPenguinException($"Cant resolve __builtin.IChannel<{payloadAuto.FullName()}>", port.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);

                cls.AddVariableSymbol(port.Name, false, new Or<string, IType>(channelType.WithMutability(Mutability.Mutable)), port.SourceLocation, null, true, null, Mutability.Mutable);
                PortRegistry.Register(new(cls.FullName(), port.Name, port.IsInput, payloadType, port.DefaultExpression != null));

                var fieldSymbol = Model.ResolveSymbol(cls.FullName() + "." + port.Name)
                    ?? throw new BabyPenguinException($"Port field '{port.Name}' not registered", port.SourceLocation, code: ErrorCode.E_INTERNAL);

                string channelClass;
                if (port.IsInput)
                {
                    // Defaulted input: a constant source (current() reads the
                    // default; wait never delivers). No default: a permanently
                    // idle placeholder — waiting an unbound input parks the
                    // routine instead of crashing on a null field.
                    channelClass = port.DefaultExpression == null
                        ? "__builtin._NeverSource"
                        : "__builtin.ConstantSource";
                }
                else
                {
                    // Fan-out hub: keeps the latest slot for direct top-level
                    // reads and grants every connected input its own wire.
                    channelClass = "__builtin._Fanout";
                }

                var concreteType = Model.ResolveType($"{channelClass}<{payloadAuto.FullName()}>")
                    ?? throw new BabyPenguinException($"Cant resolve {channelClass}<{payloadAuto.FullName()}>", port.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);
                var ctorSymbol = Model.ResolveSymbol($"{channelClass}<{payloadAuto.FullName()}>.new")
                    ?? throw new BabyPenguinException($"Cant resolve {channelClass}.new", port.SourceLocation, code: ErrorCode.E_RESOLVE_TYPE);

                var temp = constructorBody.AllocTempSymbol(concreteType.WithMutability(Mutability.Mutable), port.SourceLocation);
                constructorBody.AddInstruction(new NewInstanceInstruction(port.SourceLocation, temp));
                var ctorArgs = new List<ISymbol> { temp };
                if (port.IsInput && port.DefaultExpression != null)
                {
                    var defaultSym = constructorBody.AddExpression(port.DefaultExpression!, false);
                    ctorArgs.Add(defaultSym);
                }
                constructorBody.AddInstruction(new FunctionCallInstruction(port.SourceLocation, ctorSymbol, ctorArgs, null));
                constructorBody.AddInstruction(new WriteMemberInstruction(port.SourceLocation, fieldSymbol, temp, thisSymbol!));

                // An output's initial slot value (Q3): an EXPLICIT declared
                // default is a weak deliverable seed (time-0 first-wait
                // wake-up, superseded by the first real write); a payload
                // type's zero value is current()-only (deterministic bare
                // reads; `wait port` still parks until a real write). Either
                // way subscribe() seeds wires from it.
                if (!port.IsInput)
                {
                    ISymbol? seedSym = null;
                    bool deliver = false;
                    if (port.DefaultExpression != null)
                    {
                        seedSym = constructorBody.AddExpression(port.DefaultExpression!, false);
                        deliver = true;
                    }
                    else if (PortZeroLiteral(payloadAuto) is string zero)
                    {
                        seedSym = constructorBody.AllocTempSymbol(payloadAuto, port.SourceLocation);
                        constructorBody.AddInstruction(new AssignLiteralToSymbolInstruction(port.SourceLocation, seedSym, payloadAuto, zero));
                    }
                    if (seedSym != null)
                    {
                        var deliverSym = constructorBody.AllocTempSymbol(Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable), port.SourceLocation);
                        constructorBody.AddInstruction(new AssignLiteralToSymbolInstruction(port.SourceLocation, deliverSym, Model.BasicTypeNodes.Bool.ToType(Mutability.Immutable), deliver ? "true" : "false"));
                        var setSeedSymbol = Model.ResolveSymbol($"__builtin._Fanout<{payloadAuto.FullName()}>.set_seed")
                            ?? throw new BabyPenguinException($"Cant resolve __builtin._Fanout<{payloadAuto.FullName()}>.set_seed", port.SourceLocation, code: ErrorCode.E_RESOLVE_SYMBOL);
                        var seedRef = constructorBody.AllocTempSymbol(setSeedSymbol.TypeInfo, port.SourceLocation);
                        constructorBody.AddInstruction(new ReadMemberInstruction(port.SourceLocation, setSeedSymbol, temp, seedRef, true));
                        constructorBody.AddInstruction(new FunctionCallInstruction(port.SourceLocation, seedRef, [seedSym, deliverSym], null));
                    }
                }
            }
        }

        /// <summary>Literal text for a payload type's zero value, or null when
        /// the type has no synthesizable zero (reference-typed payloads keep a
        /// seedless slot — bare reads before any write still raise the clear
        /// runtime error).</summary>
        private static string? PortZeroLiteral(Type.IType t) => t.Type switch
        {
            TypeEnum.Bool => "false",
            TypeEnum.I8 or TypeEnum.I16 or TypeEnum.I32 or TypeEnum.I64 => "0",
            TypeEnum.U8 or TypeEnum.U16 or TypeEnum.U32 or TypeEnum.U64 => "0",
            TypeEnum.Float => "0.0",
            TypeEnum.Double => "0.0",
            TypeEnum.Char => "0",
            TypeEnum.String => "\"\"",
            _ => null,
        };

        /// <summary>
        /// Run class-level construct blocks (hidden __class_construct_* member
        /// functions) after port wiring and BEFORE initial spawn: composition
        /// (`new` submodules, `connect` lines) completes before any process of
        /// this instance starts.
        /// </summary>
        private void InvokeClassConstructs(IClassNode cls, ICodeContainer constructorBody)
        {
            var constructFunctions = cls.Functions
                .Where(f => f.Name.StartsWith(SemanticScopingPass.ClassConstructPrefix) && f.FunctionSymbol != null)
                .ToList();
            if (constructFunctions.Count == 0) return;

            var thisSymbol = Model.ResolveShortSymbol("this", scope: constructorBody)
                ?? throw new BabyPenguinException($"Cant resolve 'this' in constructor of '{cls.FullName()}'", cls.SourceLocation, code: ErrorCode.E_RESOLVE_SYMBOL);
            foreach (var func in constructFunctions)
            {
                constructorBody.AddInstruction(new FunctionCallInstruction(func.SourceLocation.StartLocation, func.FunctionSymbol!, [thisSymbol!], null));
            }
        }

        /// <summary>
        /// Spawn every class-level initial routine (hidden __initial_* member
        /// functions) at the tail of the constructor: module processes come to
        /// life when the instance is created. The method reference is read off
        /// `this` (fat pointer carries the owner as the routine's `this`).
        /// </summary>
        private void SpawnClassInitialRoutines(IClassNode cls, ICodeContainer constructorBody)
        {
            var initialFunctions = cls.Functions
                .Where(f => f.Name.StartsWith(SemanticScopingPass.ClassInitialPrefix) && f.FunctionSymbol != null)
                .ToList();
            if (initialFunctions.Count == 0) return;

            var thisSymbol = Model.ResolveShortSymbol("this", scope: constructorBody)
                ?? throw new BabyPenguinException($"Cant resolve 'this' in constructor of '{cls.FullName()}'", cls.SourceLocation, code: ErrorCode.E_RESOLVE_SYMBOL);
            var voidFutureType = Model.ResolveType("__builtin.IFuture<void>")
                ?? throw new BabyPenguinException("type '__builtin.IFuture<void>' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);

            foreach (var func in initialFunctions)
            {
                var methodRef = constructorBody.AllocTempSymbol(func.FunctionSymbol!.TypeInfo, func.SourceLocation.StartLocation);
                constructorBody.AddInstruction(new ReadMemberInstruction(func.SourceLocation.StartLocation, func.FunctionSymbol, thisSymbol, methodRef, true));
                var target = constructorBody.AllocTempSymbol(voidFutureType, func.SourceLocation.StartLocation);
                constructorBody.SchedulerAddSimpleJob(methodRef, func.SourceLocation.StartLocation, target);
            }
        }

        public void InitInterfaceConstructor(IInterfaceNode intf)
        {
            // Idempotent: specialization catch-up replays this pass while it is
            // still running; the constructor was already wired on the first
            // visit and the replayed pass-3 mutates `this` to `mut this`,
            // which would otherwise fail the parameter re-check below.
            if (intf.Constructor != null) return;

            var sourceLocation = intf.SyntaxNode?.SourceLocation.StartLocation ?? SourceLocation.Empty();

            if (intf.Functions.Find(i => i.Name == "new") is IFunction constructorFunc)
            {
                if (constructorFunc.Parameters.Count > 0 &&
                    constructorFunc.Parameters[0].Type.FullName() == intf.FullName())
                {                    if (constructorFunc.Parameters.Count > 1 || constructorFunc.Parameters[0].Name != "this")
                        throw new BabyPenguinException($"Constructor function of interface '{intf.Name}' should have only one parameter 'this' with type '{intf.FullName()}'", sourceLocation, code: ErrorCode.E_INTERNAL);
                    if (constructorFunc.Parameters[0].Type.IsMutable == Mutability.Auto)
                    {
                        constructorFunc.Parameters[0] = new FunctionParameter("this", constructorFunc.Parameters[0].Type.WithMutability(Mutability.Immutable), 0);
                    }
                    intf.Constructor = constructorFunc;
                    sourceLocation = constructorFunc.SourceLocation;
                }
                else
                {
                    throw new BabyPenguinException($"Constructor function of interface '{intf.Name}' should have first parameter of type '{intf.FullName()}'", sourceLocation, code: ErrorCode.E_INTERNAL);
                }
            }
            else
            {
                List<FunctionParameter> param = [new FunctionParameter("this", intf.ToType(Mutability.Mutable), 0)];
                intf.Constructor = new Function(Model, "new", param, Model.BasicTypeNodes.Void.ToType(Mutability.Immutable), sourceLocation, false, false);
                intf.AddFunction(intf.Constructor);
                Model.CatchUp(intf.Constructor);
            }
        }

        public void ProcessInterface(IInterfaceNode intf)
        {
            if (intf.SyntaxNode is InterfaceDefinition syntaxNode)
            {
                var constructorBody = (intf.Constructor as ICodeContainer)!;
                // ResolveUnresolvedSymbols(constructorBody, intf);

                foreach (var varDecl in syntaxNode.Declarations)
                {
                    InitializeVariable(new(intf), constructorBody, varDecl);
                }
            }
        }

        public void InitializeVariable(Or<IInterfaceNode, IClassNode> intfOrCls, ICodeContainer constructorBody, Declaration varDecl)
        {
            if (varDecl.InitializeExpression is ISyntaxExpression initializer)
            {
                var memberSymbol = Model.ResolveShortSymbol(varDecl.Name, scope: intfOrCls.IsLeft ? intfOrCls.Left : intfOrCls.Right, requireSymbolTypeInferred: false)!;

                if (memberSymbol.TypeInferStatus != TypeInferStatus.ExplicitTyped)
                    constructorBody!.InferVariableType(memberSymbol);

                var thisSymbol = Model.ResolveShortSymbol("this", scope: intfOrCls.IsLeft ? intfOrCls.Left!.Constructor : intfOrCls.Right!.Constructor)!;
                var temp = constructorBody.AddExpression(initializer, true);
                if (temp.TypeInfo.FullName() != memberSymbol.TypeInfo.WithMutability(temp.TypeInfo.IsMutable).FullName())
                {
                    if (!temp.TypeInfo.CanImplicitlyCastTo(memberSymbol.TypeInfo))
                    {
                        throw new BabyPenguinException($"Cannot assign type '{temp.TypeInfo.FullName()}' to type '{memberSymbol.TypeInfo.FullName()}'", varDecl.SourceLocation, code: ErrorCode.E_TYPE_MISMATCH);
                    }
                    else
                    {
                        var castedTemp = constructorBody.AllocTempSymbol(memberSymbol.TypeInfo, varDecl.SourceLocation);
                        constructorBody.AddCastExpression(new(temp), castedTemp, varDecl.SourceLocation);
                        temp = castedTemp;
                    }
                }
                constructorBody.AddInstruction(new WriteMemberInstruction(varDecl.SourceLocation, memberSymbol, temp, thisSymbol));
            }
        }

        public string Report => "";
    }
}