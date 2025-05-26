namespace BabyPenguin.SemanticInterface
{
    public interface IRoutineContainer : ISemanticScope
    {
        void AddInitialRoutine(IInitialRoutine routine)
        {
            InitialRoutines.Add(routine);
            routine.Parent = this;
        }

        void AddOnRoutine(IOnRoutine routine)
        {
            OnRoutines.Add(routine);
            routine.Parent = this;
        }

        void AddFunction(IFunction function)
        {
            Functions.Add(function);
            function.Parent = this;
        }

        List<IInitialRoutine> InitialRoutines { get; }

        List<IOnRoutine> OnRoutines { get; }

        List<IFunction> Functions { get; }
    }

}