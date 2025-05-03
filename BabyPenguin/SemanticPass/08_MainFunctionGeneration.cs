
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
                mainFunc.Instructions.Add(new FunctionCallInstruction(constructor, [], null));
            }

            // push all initial routines into pending queue
            var schedulerSymbol = Model.ResolveSymbol("__builtin._main_scheduler") ?? throw new BabyPenguinException("symbol '__builtin._main_scheduler' is not found.");
            var schedulerEntrySymbol = Model.ResolveSymbol("__builtin.Scheduler.entry") ?? throw new BabyPenguinException("symbol '__builtin.Scheduler.entry' is not found.");
            var pendingJobsSymbol = Model.ResolveSymbol("__builtin.Scheduler.pending_jobs") ?? throw new BabyPenguinException("symbol '__builtin.Scheduler.pending_jobs' is not found.");
            var pendingJobsEnqueueSymbol = Model.ResolveSymbol("__builtin.Queue<__builtin.IFutureBase>.enqueue") ?? throw new BabyPenguinException("symbol '__builtin.Queue<__builtin.IFutureBase>.enqueue' is not found.");
            var simpleRoutineType = Model.ResolveType("__builtin.SimpleRoutine") ?? throw new BabyPenguinException("type '__builtin.SimpleRoutine' is not found.");
            var simpleRoutineConstructor = Model.ResolveSymbol("__builtin.SimpleRoutine.new") ?? throw new BabyPenguinException("symbol '__builtin.SimpleRoutine.new' is not found.");
            var ifutureBaseType = Model.ResolveType("__builtin.IFutureBase") ?? throw new BabyPenguinException("type '__builtin.IFutureBase' is not found.");
            var pendingJobsInstanceSymbol = (mainFunc as ICodeContainer).AllocTempSymbol(Model.ResolveType("__builtin.Queue<__builtin.IFutureBase>") ?? throw new BabyPenguinException("type '__builtin.Queue<__builtin.IFutureBase>' is not found."), SourceLocation.Empty());

            mainFunc.Instructions.Add(new ReadMemberInstruction(pendingJobsSymbol, schedulerSymbol, pendingJobsInstanceSymbol));
            foreach (var initialRoutine in Model.FindAll(i => i is IInitialRoutine))
            {
                var routineNameSymbol = (mainFunc as ICodeContainer).AllocTempSymbol(BasicType.String, SourceLocation.Empty());
                var routineSymbol = (mainFunc as ICodeContainer).AllocTempSymbol(simpleRoutineType, SourceLocation.Empty());
                var futureSymbol = (mainFunc as ICodeContainer).AllocTempSymbol(ifutureBaseType, SourceLocation.Empty());
                mainFunc.Instructions.Add(new AssignLiteralToSymbolInstruction(routineNameSymbol, BasicType.String, "\"" + initialRoutine.FullName + "\""));
                mainFunc.Instructions.Add(new NewInstanceInstruction(routineSymbol));
                mainFunc.Instructions.Add(new FunctionCallInstruction(simpleRoutineConstructor, [routineSymbol, routineNameSymbol], null));
                mainFunc.Instructions.Add(new CastInstruction(routineSymbol, ifutureBaseType, futureSymbol));
                mainFunc.Instructions.Add(new FunctionCallInstruction(pendingJobsEnqueueSymbol, [pendingJobsInstanceSymbol, futureSymbol], null));
            }
            mainFunc.Instructions.Add(new FunctionCallInstruction(schedulerEntrySymbol, [schedulerSymbol], null));
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