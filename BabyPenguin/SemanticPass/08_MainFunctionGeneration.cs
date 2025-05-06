
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
                throw new BabyPenguinException("symbol '__builtin._main' is reserved.", symbol.SourceLocation);

            var mainFunc = new Function(Model, "_main", [], BasicType.Void, false, true, false, false, true, false);
            if (Model.Namespaces.Find(ns => ns.Name == "__builtin")?.Namespaces.First() is not INamespace builtinNamespace)
                throw new BabyPenguinException("namespace '__builtin' is not found.");

            builtinNamespace.AddFunctionSymbol(mainFunc, true, BasicType.Void, [], SourceLocation.Empty(), 0, null, true, false, true);
            builtinNamespace.AddFunction(mainFunc);

            // init global variables
            foreach (var mergedNamespace in Model.Namespaces)
            {
                var constructor = Model.ResolveSymbol(mergedNamespace.FullName + ".new") ?? throw new BabyPenguinException($"symbol '{mergedNamespace.FullName + ".new"}' is not found.");
                mainFunc.Instructions.Add(new FunctionCallInstruction(constructor.SourceLocation, constructor, [], null));
            }

            // push all initial routines into pending queue
            foreach (var initialRoutine in Model.FindAll(i => i is IInitialRoutine).Cast<IInitialRoutine>())
            {
                (mainFunc as ICodeContainer).SchedulerAddSimpleJob(initialRoutine, SourceLocation.Empty(), null);
            }

            // call __builtin._main_scheduler.entry()
            var schedulerSymbol = Model.ResolveSymbol("__builtin._main_scheduler") ?? throw new BabyPenguinException("symbol '__builtin._main_scheduler' is not found.");
            var schedulerEntrySymbol = Model.ResolveSymbol("__builtin.Scheduler.entry") ?? throw new BabyPenguinException("symbol '__builtin.Scheduler.entry' is not found.");
            mainFunc.Instructions.Add(new FunctionCallInstruction(schedulerEntrySymbol.SourceLocation.StartLocation, schedulerEntrySymbol, [schedulerSymbol], null));
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