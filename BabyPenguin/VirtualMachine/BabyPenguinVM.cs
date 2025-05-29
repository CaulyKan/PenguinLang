using CommandLine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BabyPenguin.VirtualMachine
{
    public class BabyPenguinVM
    {
        public BabyPenguinVM(SemanticModel model)
        {
            Model = model;

            foreach (var symbol in model.Symbols.Where(s => !s.IsEnum && !s.IsLocal && !s.IsClassMember))
            {
                Global.GlobalVariables.Add(symbol.FullName, IRuntimeSymbol.FromSymbol(model, symbol));
            }

            ExternFunctions.Build(this);
        }

        public SemanticModel Model { get; }

        public RuntimeGlobal Global { get; } = new RuntimeGlobal();

        public RuntimeFrame? StartFrame { get; private set; }

        public string CollectOutput() => Global.Output.ToString();

        public void Initialize()
        {
            var mainFunc = Model.ResolveSymbol("__builtin._main") as FunctionSymbol;
            if (mainFunc == null)
                throw new BabyPenguinRuntimeException("__builtin._main function not found.");

            var frame = new RuntimeFrame(mainFunc.CodeContainer, Global, [], null);
            StartFrame = frame;
        }

        public int Run()
        {
            if (StartFrame == null)
                Initialize();
            foreach (var result in StartFrame!.Run())
            {
                if (result.IsLeft)
                {
                    if (result.Left!.Reason == RuntimeBreakReason.Exited)
                    {
                        return Global.ExitCode;
                    }
                }
            }
            return 0;
        }

        public bool InsertBreakPoint(SourceLocation location)
        {
            var result = false;
            foreach (var container in Model.FindAll(i => i is ICodeContainer).Cast<ICodeContainer>())
            {
                if (container.SourceLocation.Contains(location))
                {
                    var instructionIndex = container.Instructions.FindIndex(i => i.SourceLocation >= location);
                    var instruction = new SignalInstruction(location, null, 0);
                    container.Instructions.Insert(instructionIndex, instruction);
                    result = true;
                }
            }
            return result;
        }

        public bool RemoveBreakPoint(SourceLocation location)
        {
            var result = false;
            foreach (var container in Model.FindAll(i => i is ICodeContainer && i.SourceLocation.Contains(location)).Cast<ICodeContainer>())
            {
                var instructionIndex = container.Instructions.FindIndex(i => i.SourceLocation >= location && i is SignalInstruction signalInstruction && signalInstruction.Code == 0);
                if (instructionIndex >= 0)
                {
                    container.Instructions.RemoveAt(instructionIndex);
                    result = true;
                }
            }
            return result;
        }
    }

    public class BabyPenguinRuntimeException(string message) : Exception(message) { }

    public class RuntimeGlobal
    {
        public enum StepModeEnum { StepIn, StepOver, StepOut, Run }

        public int ExitCode { get; set; } = 0;

        public StepModeEnum StepMode { get; set; } = StepModeEnum.Run;

        public Dictionary<string, IRuntimeSymbol> GlobalVariables { get; } = [];

        public Dictionary<string, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>>> ExternFunctions { get; } = [];

        public void RegisterExternFunction(string name, Action<IRuntimeSymbol?, List<IRuntimeValue>> func)
        {
            ExternFunctions.Add(name, (frame, result, args) =>
            {
                func(result, args);
                return [];
            });
        }

        public void RegisterExternFunction(string name, Func<RuntimeFrame, IRuntimeSymbol?, List<IRuntimeValue>, IEnumerable<RuntimeBreak>> func)
        {
            ExternFunctions.Add(name, func);
        }

        public bool EnableDebugPrint { get; set; } = false;

        public StringBuilder Output { get; } = new StringBuilder();

        public Action<string> PrintFunc { get; set; } = (s) => Console.Write(s);

        public Action<string> DebugFunc { get; set; } = (s) => Console.Write(s);

        public void Print(string s, bool newline = false)
        {
            if (!newline)
            {
                Output.Append(s);
                PrintFunc(s);
            }
            else
            {
                Output.AppendLine(s);
                PrintFunc(s + Environment.NewLine);
            }
        }

    }
}