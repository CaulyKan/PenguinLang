namespace BabyPenguin.SemanticInterface
{

    public interface ISemanticScope : ISemanticNode
    {
        ISemanticScope? Parent { get; set; }

        IEnumerable<ISemanticScope> Children { get; }

        List<NamespaceImport> ImportedNamespaces { get; }

        void Traverse(Action<ISemanticScope> action)
        {
            action(this);
            foreach (var child in Children)
                child.Traverse(action);
        }

        ISemanticScope? FindAncestorIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                return this;

            return FindAncestor(predicate);
        }

        ISemanticScope? FindAncestor(Predicate<ISemanticScope> predicate)
        {
            if (Parent == null)
                return null;

            if (predicate(Parent))
                return Parent;

            return Parent.FindAncestor(predicate);
        }

        ISemanticScope? FindChildIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                return this;

            foreach (var child in Children)
            {
                var result = child.FindChild(predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        ISemanticScope? FindChild(Predicate<ISemanticScope> predicate)
        {
            foreach (var child in Children)
            {
                var result = child.FindChildIncludingSelf(predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        IEnumerable<ISemanticScope> FindChildrenIncludingSelf(Predicate<ISemanticScope> predicate)
        {
            if (predicate(this))
                yield return this;

            if (this is ITypeNode typeObj)
                foreach (var specialization in typeObj.GenericInstances.Cast<ISemanticScope>())
                    foreach (var res in specialization.FindChildrenIncludingSelf(predicate))
                        yield return res;

            foreach (var child in Children)
                foreach (var res in child.FindChildrenIncludingSelf(predicate))
                    yield return res;
        }

        IEnumerable<ISemanticScope> FindChildren(Predicate<ISemanticScope> predicate)
        {
            foreach (var child in Children)
                foreach (var res in child.FindChildrenIncludingSelf(predicate))
                    yield return res;
        }

        IEnumerable<MergedNamespace> GetImportedNamespaces(bool includeBuiltin = true)
        {
            return ImportedNamespaces.Select(i =>
                    Model.Namespaces.Find(n => n.Name == i.Namespace) ??
                        throw new BabyPenguinException($"Namespace '{i}' not found.", i.SourceLocation))
                .Concat(
                    Parent?.GetImportedNamespaces(false) ?? []
                ).Concat(
                    includeBuiltin ? [Model.BuiltinNamespace] : Array.Empty<MergedNamespace>()
                ).Concat(
                    this is MergedNamespace ns ? [ns] : Array.Empty<MergedNamespace>()
                );
        }
    }

}