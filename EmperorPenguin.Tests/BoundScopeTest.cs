using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundScopeTest
{
    private static readonly Lazy<BatchResults> _batch = new(() =>
        BatchCompiler.InitBoundBatch<BoundScopeTest>());

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let mut sym = new bound.BoundVariableSymbol();
    sym.name = ""x"";
    scope.add_symbol(new bound.BoundSymbol.variable(sym));
    let found = scope.lookup_symbol(""x"");
    if (found.is_some()) {
        println(""found="" + found.some.get_name());
    }
    let missing = scope.lookup_symbol(""y"");
    if (missing.is_none()) {
        println(""missing=none"");
    }
}
", "found=x\nmissing=none")]
    public void AddAndLookupSymbolTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut parent = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let mut sym = new bound.BoundVariableSymbol();
    sym.name = ""x"";
    parent.add_symbol(new bound.BoundSymbol.variable(sym));
    let mut child = new bound.BoundScope(new bound.ScopeKind.FunctionScope(), ""func"", ""<global>.func"", new Option<bound.BoundScope>.some(parent));
    parent.add_child(child);
    let found = child.lookup_symbol(""x"");
    if (found.is_some()) {
        println(""found_parent="" + found.some.get_name());
    }
    let local = child.lookup_symbol_local(""x"");
    if (local.is_none()) {
        println(""not_local"");
    }
}
", "found_parent=x\nnot_local")]
    public void ParentScopeLookupTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let mut type_sym = new bound.BoundTypeSymbol(""MyClass"", ""MyClass"");
    scope.add_symbol(new bound.BoundSymbol.type_sym(type_sym));
    let mut var_sym = new bound.BoundVariableSymbol();
    var_sym.name = ""x"";
    scope.add_symbol(new bound.BoundSymbol.variable(var_sym));
    let found_type = scope.lookup_type_in_scope(""MyClass"");
    if (found_type.is_some()) {
        println(""type_found="" + found_type.some.get_name());
    }
    let not_type = scope.lookup_type_in_scope(""x"");
    if (not_type.is_none()) {
        println(""x_not_type"");
    }
}
", "type_found=MyClass\nx_not_type")]
    public void LookupTypeInScopeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut global_scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let ns1: mut bound.BoundScope = global_scope.add_or_merge_namespace(""Foo"");
    let mut sym1 = new bound.BoundVariableSymbol();
    sym1.name = ""a"";
    ns1.add_symbol(new bound.BoundSymbol.variable(sym1));
    let ns2: mut bound.BoundScope = global_scope.add_or_merge_namespace(""Foo"");
    let mut sym2 = new bound.BoundVariableSymbol();
    sym2.name = ""b"";
    ns2.add_symbol(new bound.BoundSymbol.variable(sym2));
    if (cast<string>(ns1.kind) == cast<string>(ns2.kind)) {
        println(""same_scope"");
    }
    let found_a = ns1.lookup_symbol_local(""a"");
    let found_b = ns1.lookup_symbol_local(""b"");
    if (found_a.is_some() && found_b.is_some()) {
        println(""both_found"");
    }
}
", "same_scope\nboth_found")]
    public void NamespaceMergeTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut global_scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let ns: mut bound.BoundScope = global_scope.add_or_merge_namespace(""Foo"");
    let mut type_sym = new bound.BoundTypeSymbol(""Bar"", ""Bar"");
    ns.add_symbol(new bound.BoundSymbol.type_sym(type_sym));
    let parts: mut List<string> = new List<string>();
    parts.push(""Foo"");
    parts.push(""Bar"");
    let found = global_scope.resolve_qualified(parts);
    if (found.is_some()) {
        println(""resolved="" + found.some.get_name());
    }
    let bad_parts: mut List<string> = new List<string>();
    bad_parts.push(""Unknown"");
    let missing = global_scope.resolve_qualified(bad_parts);
    if (missing.is_none()) {
        println(""unresolved"");
    }
}
", "resolved=Bar\nunresolved")]
    public void ResolveQualifiedTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut global_scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let ns: mut bound.BoundScope = global_scope.add_or_merge_namespace(""NS"");
    let mut sym = new bound.BoundVariableSymbol();
    sym.name = ""myvar"";
    ns.add_symbol(new bound.BoundSymbol.variable(sym));
    println(sym.full_name);
}
", "<global>.NS.myvar")]
    public void SymbolFullNameTest() => _batch.Value.Assert();

    [Fact]
    [BatchBoundTest(@"
initial {
    let mut global_scope = new bound.BoundScope(new bound.ScopeKind.GlobalScope(), ""<global>"", ""<global>"", new Option<bound.BoundScope>.none());
    let ns = global_scope.add_or_merge_namespace(""MyNS"");
    let found = global_scope.lookup_namespace(""MyNS"");
    if (found.is_some()) {
        println(""ns_found="" + found.some.name);
    }
    let missing = global_scope.lookup_namespace(""OtherNS"");
    if (missing.is_none()) {
        println(""ns_missing"");
    }
}
", "ns_found=MyNS\nns_missing")]
    public void LookupNamespaceTest() => _batch.Value.Assert();
}
