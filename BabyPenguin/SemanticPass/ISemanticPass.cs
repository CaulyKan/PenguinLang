namespace BabyPenguin.SemanticPass
{
    public interface ISemanticPass
    {
        SemanticModel Model { get; }

        string Report { get; }

        public void Process();

        public void Process(ISemanticNode obj);
    }
}