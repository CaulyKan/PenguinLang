using Xunit;
using Xunit.Abstractions;

namespace BabyPenguin.Tests;

/// <summary>
/// Test to reproduce the enum variant data preservation bug with List<T> fields.
/// This is similar to the IRCall case where we have args: mut List<IRValue>.
/// </summary>
public class EnumVariantListTest : TestBase
{
    public EnumVariantListTest(ITestOutputHelper helper) : base(helper) { }

    [Fact]
    public void TestEnumVariantWithListField()
    {
        // Test case: enum variant with List<T> field (similar to IRCall.args)
        var compiler = new SemanticCompiler(new ErrorReporter(this));
        compiler.AddSource(@"
class WithList {
    items: mut List<i64> = new List<i64>();
    name: !mut string = """";
    fun new(mut this, name: string) {
        this.name = name;
    }
}

enum ListEnum {
    with_list: WithList;
}

initial {
    let obj = new WithList(""test"");
    obj.items.push(1);
    obj.items.push(2);
    let wrapped = new ListEnum.with_list(obj);

    if (wrapped is ListEnum.with_list) {
        println(""name="" + wrapped.with_list.name);
        println(""count="" + cast<string>(cast<i64>(wrapped.with_list.items.size())));
    }
}
");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        var output = vm.CollectOutput();
        Assert.Contains("name=test", output);
        Assert.Contains("count=2", output);
    }
}
