using BabyPenguin;

namespace EmperorPenguin.Tests;

public class BoundTypeRegistryTest
{
    private static string BoundDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "bound"));

    private static string AstDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                      "EmperorPenguin", "src", "ast"));

    private static string ProjectPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "EmperorPenguin", "EmperorPenguin.penguins"));

    private static readonly Lazy<SemanticModel> CachedModel = new(() =>
    {
        var reporter = new ErrorReporter(new StringWriter(), PenguinLangParser.DiagnosticLevel.Debug);
        var compiler = new SemanticCompiler(reporter);
        compiler.AddProject(ProjectPath);
        return compiler.Compile();
    });

    private string RunBoundCode(string userCode, int timeoutSeconds = 10)
    {
        var vm = new BabyPenguinVM(CachedModel.Value);
        vm.Global.CommandLineArgs = [];
        // Write user code to temp file and run via the EmperorPenguin main
        // Instead, compile and run the bound code directly through BabyPenguin
        var compiler = new SemanticCompiler(new ErrorReporter());
        // Add bound source files
        foreach (var f in Directory.GetFiles(BoundDir, "*.penguin"))
            compiler.AddFile(f);
        // Add ast source files (needed by SemanticModel)
        foreach (var f in Directory.GetFiles(AstDir, "*.penguin"))
            compiler.AddFile(f);
        compiler.AddSource(userCode);
        var model = compiler.Compile();
        vm = new BabyPenguinVM(model);
        var task = Task.Run(() => vm.Run());
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("VM timed out");
        return vm.CollectOutput().Trim();
    }

    [Fact]
    public void PrimitiveTypeResolveTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    let i32t = reg.resolve_type(""i32"");
    if (i32t.is_some()) {
        println(""i32="" + i32t.some.display_name());
    }
    let boolt = reg.resolve_type(""bool"");
    if (boolt.is_some()) {
        println(""bool="" + boolt.some.display_name());
    }
    let strt = reg.resolve_type(""string"");
    if (strt.is_some()) {
        println(""string="" + strt.some.display_name());
    }
    let voidt = reg.resolve_type(""void"");
    if (voidt.is_some()) {
        println(""void="" + voidt.some.display_name());
    }
    let unknown = reg.resolve_type(""unknown"");
    if (unknown.is_none()) {
        println(""unknown=none"");
    }
}
");
        Assert.Contains("i32=i32", output);
        Assert.Contains("bool=bool", output);
        Assert.Contains("string=string", output);
        Assert.Contains("void=void", output);
        Assert.Contains("unknown=none", output);
    }

    [Fact]
    public void TypePredicateTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    let i32t = reg.i32_type;
    let boolt = reg.bool_type;
    let strt = reg.string_type;
    let voidt = reg.void_type;
    let f64t = reg.f64_type;
    println(""i32_numeric="" + cast<string>(reg.is_numeric(i32t)));
    println(""i32_integer="" + cast<string>(reg.is_integer(i32t)));
    println(""f64_numeric="" + cast<string>(reg.is_numeric(f64t)));
    println(""f64_integer="" + cast<string>(reg.is_integer(f64t)));
    println(""bool_numeric="" + cast<string>(reg.is_numeric(boolt)));
    println(""bool_bool="" + cast<string>(reg.is_bool(boolt)));
    println(""string_string="" + cast<string>(reg.is_string(strt)));
    println(""void_void="" + cast<string>(reg.is_void(voidt)));
}
");
        Assert.Contains("i32_numeric=true", output);
        Assert.Contains("i32_integer=true", output);
        Assert.Contains("f64_numeric=true", output);
        Assert.Contains("f64_integer=false", output);
        Assert.Contains("bool_numeric=false", output);
        Assert.Contains("bool_bool=true", output);
        Assert.Contains("string_string=true", output);
        Assert.Contains("void_void=true", output);
    }

    [Fact]
    public void DisplayNamesTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    println(reg.i32_type.display_name());
    println(reg.bool_type.display_name());
    println(reg.string_type.display_name());
    println(reg.void_type.display_name());
    println(reg.f64_type.display_name());
    println(reg.u64_type.display_name());
    println(reg.char_type.display_name());
}
");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.Trim()).ToArray();
        Assert.Equal("i32", lines[0]);
        Assert.Equal("bool", lines[1]);
        Assert.Equal("string", lines[2]);
        Assert.Equal("void", lines[3]);
        Assert.Equal("f64", lines[4]);
        Assert.Equal("u64", lines[5]);
        Assert.Equal("char", lines[6]);
    }

    [Fact]
    public void ImplicitCastTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    println(""i32_to_i64="" + cast<string>(reg.can_implicitly_cast(reg.i32_type, reg.i64_type)));
    println(""i32_to_f64="" + cast<string>(reg.can_implicitly_cast(reg.i32_type, reg.f64_type)));
    println(""i32_to_bool="" + cast<string>(reg.can_implicitly_cast(reg.i32_type, reg.bool_type)));
    println(""i64_to_i32="" + cast<string>(reg.can_implicitly_cast(reg.i64_type, reg.i32_type)));
    println(""i32_to_string="" + cast<string>(reg.can_implicitly_cast(reg.i32_type, reg.string_type)));
    println(""bool_to_string="" + cast<string>(reg.can_implicitly_cast(reg.bool_type, reg.string_type)));
    println(""u8_to_i16="" + cast<string>(reg.can_implicitly_cast(reg.u8_type, reg.i16_type)));
    println(""f32_to_f64="" + cast<string>(reg.can_implicitly_cast(reg.f32_type, reg.f64_type)));
}
");
        Assert.Contains("i32_to_i64=true", output);
        Assert.Contains("i32_to_f64=true", output);
        Assert.Contains("i32_to_bool=false", output);
        Assert.Contains("i64_to_i32=false", output);
        Assert.Contains("i32_to_string=true", output);
        Assert.Contains("bool_to_string=true", output);
        Assert.Contains("u8_to_i16=true", output);
        Assert.Contains("f32_to_f64=true", output);
    }

    [Fact]
    public void FunctionTypeTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    let params = new List<bound.BoundType>();
    params.push(reg.i32_type);
    params.push(reg.i64_type);
    let ft = reg.make_function_type(reg.bool_type, params, false);
    println(ft.display_name());
    let aft = reg.make_function_type(reg.void_type, new List<bound.BoundType>(), true);
    println(aft.display_name());
}
");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.Trim()).ToArray();
        Assert.Equal("fun<bool, i32, i64>", lines[0]);
        Assert.Equal("async_fun<void>", lines[1]);
    }

    [Fact]
    public void ErrorTypeTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    let err = reg.make_error_type();
    println(err.display_name());
}
");
        Assert.Equal("<error>", output);
    }

    [Fact]
    public void ValueTypeReferenceTypeTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    println(""i32_value="" + cast<string>(reg.i32_type.is_value_type()));
    println(""bool_value="" + cast<string>(reg.bool_type.is_value_type()));
    println(""string_value="" + cast<string>(reg.string_type.is_value_type()));
    println(""string_ref="" + cast<string>(reg.string_type.is_reference_type()));
    println(""i32_ref="" + cast<string>(reg.i32_type.is_reference_type()));
}
");
        Assert.Contains("i32_value=true", output);
        Assert.Contains("bool_value=true", output);
        Assert.Contains("string_value=false", output);
        Assert.Contains("string_ref=true", output);
        Assert.Contains("i32_ref=false", output);
    }

    [Fact]
    public void RegisterAndResolveUserTypeTest()
    {
        var output = RunBoundCode(@"
initial {
    let reg = new bound.BoundTypeRegistry();
    let t: mut bound.BoundType = new bound.BoundType();
    t.kind = new bound.TypeKind.ClassKind();
    reg.register_type(""MyClass"", t);
    let resolved = reg.resolve_type(""MyClass"");
    if (resolved.is_some()) {
        let r = resolved.some;
        if (r.kind is bound.TypeKind.ClassKind) {
            println(""found=class"");
        }
    }
    let unknown = reg.resolve_type(""Unknown"");
    if (unknown.is_none()) {
        println(""unknown=none"");
    }
}
");
        Assert.Contains("found=class", output);
        Assert.Contains("unknown=none", output);
    }
}