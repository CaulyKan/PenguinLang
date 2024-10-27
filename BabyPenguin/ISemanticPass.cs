namespace BabyPenguin
{
    public interface ISemanticPass
    {
        SemanticModel Model { get; }

        string Report { get; }

        int PassIndex { get; }

        public void Process();

        public void Process(ISemanticNode obj);
    }
}