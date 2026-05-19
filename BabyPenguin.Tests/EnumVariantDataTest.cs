using Xunit;
using Xunit.Abstractions;

namespace BabyPenguin.Tests;

/// <summary>
/// Test to reproduce the enum variant data preservation bug.
/// When a class is wrapped in an enum variant, the fields are not preserved correctly.
/// </summary>
public class EnumVariantDataTest : TestBase
{
    public EnumVariantDataTest(ITestOutputHelper helper) : base(helper) { }

    [Fact]
    public void TestEnumVariantWithStringField()
    {
        // Test basic case: string field in enum variant
        var compiler = new SemanticCompiler(new ErrorReporter(this));
        compiler.AddSource(@"
class TestClass {
    value: !mut string = """";
    fun new(mut this, v: string) {
        this.value = v;
    }
}

enum TestEnum {
    variant_a: TestClass;
}

initial {
    let obj = new TestClass(""hello"");
    let wrapped = new TestEnum.variant_a(obj);

    // Try to access the field
    if (wrapped is TestEnum.variant_a) {
        let result = wrapped.variant_a.value;
        println(""result="" + result);
    }
}
");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Equal("result=hello" + EOL, vm.CollectOutput());
    }

    [Fact]
    public void TestEnumVariantWithMultipleFields()
    {
        // Test case 2: Multiple fields of different types
        var compiler = new SemanticCompiler(new ErrorReporter(this));
        compiler.AddSource(@"
class MultiField {
    name: !mut string = """";
    count: !mut i64 = 0;
    flag: !mut bool = false;
    fun new(mut this, n: string, c: i64, f: bool) {
        this.name = n;
        this.count = c;
        this.flag = f;
    }
}

enum MultiEnum {
    first: MultiField;
}

initial {
    let obj = new MultiField(""test"", 42, true);
    let wrapped = new MultiEnum.first(obj);

    if (wrapped is MultiEnum.first) {
        println(""name="" + wrapped.first.name);
        println(""count="" + cast<string>(wrapped.first.count));
        println(""flag="" + (if (wrapped.first.flag) { ""true"" } else { ""false"" }));
    }
}
");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        var output = vm.CollectOutput();
        Assert.Contains("name=test", output);
        Assert.Contains("count=42", output);
        Assert.Contains("flag=true", output);
    }

    [Fact]
    public void TestEnumVariantWithNestedEnum()
    {
        // Test case 3: Nested enum in class field
        var compiler = new SemanticCompiler(new ErrorReporter(this));
        compiler.AddSource(@"
enum InnerEnum {
    option_a;
    option_b;
}

class OuterClass {
    inner: !mut InnerEnum = new InnerEnum.option_a();
    data: !mut string = """";
    fun new(mut this, e: InnerEnum, d: string) {
        this.inner = e;
        this.data = d;
    }
}

enum OuterEnum {
    wrapper: OuterClass;
}

initial {
    let inner = new InnerEnum.option_b();
    let obj = new OuterClass(inner, ""nested"");
    let wrapped = new OuterEnum.wrapper(obj);

    if (wrapped is OuterEnum.wrapper) {
        if (wrapped.wrapper.inner is InnerEnum.option_b) {
            println(""data="" + wrapped.wrapper.data);
        }
    }
}
");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Contains("data=nested", vm.CollectOutput());
    }
}
