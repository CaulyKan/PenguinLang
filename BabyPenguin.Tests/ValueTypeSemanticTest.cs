namespace BabyPenguin.Tests;

public class ValueTypeSemanticTest
{
    [Fact]
    public void AutoValueTypeClassAssignment()
    {
        // Class with all value-type fields (no explicit IValueType/IReferenceType)
        // Should auto-implement IValueType → value type
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddSource(@"
            class Point {
                x: i32;
                y: i32;
            }
            initial {
                let a : Point = new Point();
                let b : mut Point;
                b = a;  // value types can be copied from imm to mut
                print(""ok"");
            }
        ");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Equal("ok", vm.CollectOutput());
    }

    [Fact]
    public void ExplicitIValueTypeClass()
    {
        // Class with explicit IValueType (but with non-value field like string)
        // Should be treated as value type
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddSource(@"
            class Val {
                x: i32;
                y: string;
                impl IValueType;
            }
            initial {
                let a : Val = new Val();
                let b : mut Val;
                b = a;
                print(""ok"");
            }
        ");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Equal("ok", vm.CollectOutput());
    }

    [Fact]
    public void ExplicitIReferenceTypeClass()
    {
        // Class with explicit IReferenceType should reject imm→mut assignment
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddSource(@"
            class RefType {
                x: i32;
                impl IReferenceType;
            }
            initial {
                let a : RefType = new RefType();
                let b : mut RefType;
                b = a;  // reference types: imm→mut is error
            }
        ");
        Assert.Throws<BabyPenguinException>(() => compiler.Compile());
    }

    [Fact]
    public void AutoIReferenceTypeWithStringField()
    {
        // Class with string field (reference type) should auto IReferenceType
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddSource(@"
            class HasString {
                name: string;
            }
            initial {
                let a : HasString = new HasString();
                let b : mut HasString;
                b = a;  // auto IReferenceType → imm→mut is error
            }
        ");
        Assert.Throws<BabyPenguinException>(() => compiler.Compile());
    }

    [Fact]
    public void AutoICopyForValueType()
    {
        // IValueType class without explicit ICopy should auto-get ICopy
        var compiler = new SemanticCompiler(new ErrorReporter());
        compiler.AddSource(@"
            class Point {
                x: i32;
                y: i32;
            }
            initial {
                let a : mut Point = new Point();
                a.x = 1;
                a.y = 2;
                let b : mut Point = a.copy();  // auto-generated ICopy
                print(cast<string>(b.x));
                print(cast<string>(b.y));
            }
        ");
        var model = compiler.Compile();
        var vm = new BabyPenguinVM(model);
        vm.Run();
        Assert.Equal("12", vm.CollectOutput());
    }
}
