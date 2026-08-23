
namespace BabyPenguin.SemanticPass
{

    public class MainFunctionGenerationPass(SemanticModel model, int passIndex) : ISemanticPass
    {
        public SemanticModel Model { get; } = model;

        public int PassIndex { get; } = passIndex;

        public void Process(ISemanticNode node)
        {
            // do nothing
        }

        public void Process()
        {
            var symbol = Model.ResolveSymbol("__builtin._main");
            if (symbol != null)
                throw new BabyPenguinException("symbol '__builtin._main' is reserved.", symbol.SourceLocation, code: ErrorCode.E_BUILTIN_MISSING);

            var schedulerSymbol = Model.ResolveSymbol("__builtin._main_scheduler") ?? throw new BabyPenguinException("symbol '__builtin._main_scheduler' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);
            var runSymbol = Model.ResolveSymbol("__builtin._run") ?? throw new BabyPenguinException("symbol '__builtin._run' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);

            var mainFunc = new Function(Model, "_main", [], Model.BasicTypeNodes.Void.ToType(Mutability.Immutable), runSymbol.SourceLocation.StartLocation, false, true, false, false, true, false);
            if (Model.Namespaces.Find(ns => ns.Name == "__builtin")?.Namespaces.First() is not INamespace builtinNamespace)
                throw new BabyPenguinException("namespace '__builtin' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);

            builtinNamespace.AddFunctionSymbol(mainFunc, true, Model.BasicTypeNodes.Void.ToType(Mutability.Immutable), [], runSymbol.SourceLocation.StartLocation, null, true, false, true, Mutability.Immutable);
            builtinNamespace.AddFunction(mainFunc);

            // init global variables
            foreach (var mergedNamespace in Model.Namespaces)
            {
                var constructor = Model.ResolveSymbol(mergedNamespace.FullName() + ".new") ?? throw new BabyPenguinException($"symbol '{mergedNamespace.FullName() + ".new"}' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);
                mainFunc.Instructions.Add(new FunctionCallInstruction(constructor.SourceLocation, constructor, [], null));
            }

            // elaboration: run every top-level construct block synchronously
            // (in declaration order) before any initial routine is spawned
            foreach (var constructFunc in Model.FindAll(n => n is IFunction f
                    && f.Name.StartsWith(SemanticScopingPass.ConstructPrefix)
                    && n.Parent is INamespace
                    && f.FunctionSymbol != null).Cast<IFunction>())
            {
                mainFunc.Instructions.Add(new FunctionCallInstruction(constructFunc.SourceLocation.StartLocation, constructFunc.FunctionSymbol!, [], null));
            }

            // push all initial routines into pending queue
            foreach (var initialRoutine in Model.FindAll(i => i is IInitialRoutine).Cast<IInitialRoutine>())
            {
                var ifutureVoidType = Model.ResolveType("__builtin.IFuture<void>") ?? throw new BabyPenguinException("type '__builtin.IFutureBase' is not found.", null, code: ErrorCode.E_BUILTIN_MISSING);
                var targetSymbol = (mainFunc as ICodeContainer).AllocTempSymbol(ifutureVoidType, runSymbol.SourceLocation.StartLocation);
                (mainFunc as ICodeContainer).SchedulerAddSimpleJob(initialRoutine.FunctionSymbol!, runSymbol.SourceLocation.StartLocation, targetSymbol);
            }

            // call __builtin._run()
            mainFunc.Instructions.Add(new FunctionCallInstruction(runSymbol.SourceLocation.StartLocation, runSymbol, [], null));
        }

        public string Report
        {
            get
            {
                var sb = new StringBuilder();
                var symbol = Model.ResolveSymbol("__builtin._main") as FunctionSymbol;
                sb.AppendLine($"Compile Result For '__builtin._main'");
                sb.AppendLine(symbol!.CodeContainer.PrintInstructionsTable());
                return sb.ToString();
            }
        }
    }
}