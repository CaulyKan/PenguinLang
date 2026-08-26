namespace BabyPenguin.Type
{
    /// <summary>
    /// Structural type-node equality that is insensitive to mutability flavors
    /// ANYWHERE — the top-level mutability of an IType is not part of its node,
    /// and the generic-argument ITypes each carry their own flavor. The implicit
    /// cast checks use this so that `Foo&lt;!mut i64&gt;` and `Foo&lt;mut i64&gt;`
    /// (and `Foo&lt;i64&gt;`) all compare as the same type structure, matching
    /// EmperorPenguin's semantics where generic-argument mutability never
    /// participates in interface-implementation matching.
    /// </summary>
    public static class TypeStructure
    {
        /// <summary>Base name of a node's FullName with its generic suffix cut
        /// (the namespace-qualified name itself never contains '&lt;').</summary>
        private static string BaseName(ITypeNode node)
        {
            var n = node.FullName();
            var cut = n.IndexOf('<');
            return cut < 0 ? n : n[..cut];
        }

        /// <summary>Strips the "mut " / "!mut " flavor an IType's FullName may
        /// carry, so flavor-only differences do not disturb arg comparisons.</summary>
        private static string WithoutFlavor(IType t)
        {
            var n = t.FullName();
            if (n.StartsWith("!mut ")) return n["!mut ".Length..];
            if (n.StartsWith("mut ")) return n["mut ".Length..];
            return n;
        }

        public static bool SameType(IType? a, IType? b)
        {
            if (a == null || b == null) return false;
            return SameNode(a.TypeNode, b.TypeNode);
        }

        public static bool SameNode(ITypeNode? a, ITypeNode? b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (a.Type != b.Type) return false;
            if (a.IsSpecialized != b.IsSpecialized) return false;
            if (BaseName(a) != BaseName(b)) return false;
            if (!a.IsSpecialized) return true;
            if (a.GenericArguments.Count != b.GenericArguments.Count) return false;
            for (var i = 0; i < a.GenericArguments.Count; i++)
            {
                if (!SameArg(a.GenericArguments[i], b.GenericArguments[i]))
                    return false;
            }
            return true;
        }

        // One generic argument: nested generics compare as nodes (structure);
        // non-generic args compare by their flavor-stripped FullName (covers
        // basic types, whose TypeNodes are shared singletons anyway).
        private static bool SameArg(IType a, IType b)
        {
            if (a.GenericArguments.Count > 0 || b.GenericArguments.Count > 0)
                return SameType(a, b);
            return WithoutFlavor(a) == WithoutFlavor(b);
        }
    }
}
