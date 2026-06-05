namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for interface features.
/// Most tests use batch mode. Tests with 'is InterfaceType' checks remain individual
/// because the runtime 'is' operator doesn't work with namespace-scoped interfaces.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndInterfaceTest : EndToEndTestBase
{
    private static readonly BatchResults batch = BatchCompiler.InitE2EBatch<EndToEndInterfaceTest>();

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        class Point {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) {
                this.x = x;
                this.y = y;
            }
            impl IShow {
                fun show(this) -> string {
                    return "(" + cast<string>(this.x) + "," + cast<string>(this.y) + ")";
                }
            }
        }
        initial {
            let p = new Point(1, 2);
            println(p.show());
        }
        """,
        "(1,2)")]
    [Fact]
    public void InterfaceImpl() => batch.Assert();

    [BatchE2ETest("""
        interface IGreet {
            fun new(mut this) {}
            fun greet(this) -> string {
                return "hello";
            }
        }
        class Foo {
            fun new(mut this) {}
            impl IGreet {}
        }
        initial {
            let f = new Foo();
            println(f.greet());
        }
        """,
        "hello")]
    [Fact]
    public void InterfaceDefaultMethod() => batch.Assert();

    [BatchE2ETest("""
        interface IGreet {
            fun new(mut this) {}
            fun greet(this) -> string {
                return "hello";
            }
        }
        class Bar {
            fun new(mut this) {}
            impl IGreet {
                fun greet(this) -> string {
                    return "hi from Bar";
                }
            }
        }
        initial {
            let b = new Bar();
            println(b.greet());
        }
        """,
        "hi from Bar")]
    [Fact]
    public void InterfaceOverrideDefault() => batch.Assert();

    // 'is InterfaceType' doesn't work with namespace-scoped interfaces in batch mode
    [Fact]
    public void ObjectIsInterface()
    {
        Assert.Equal("true\nfalse\n", RunEndToEnd("""
            interface IShow {}
            class Point {
                x: i32;
                fun new(mut this, x: i32) { this.x = x; }
                impl IShow {}
            }
            class NoShow {
                fun new(mut this) {}
            }
            initial {
                let p = new Point(1);
                let n = new NoShow();
                println(cast<string>(p is IShow));
                println(cast<string>(n is IShow));
            }
            """));
    }

    [BatchE2ETest("""
        interface IAnimal {
            fun new(mut this) {}
            fun speak(this) -> string;
        }
        class Dog {
            fun new(mut this) {}
            impl IAnimal {
                fun speak(this) -> string {
                    return "woof";
                }
            }
        }
        class Cat {
            fun new(mut this) {}
            impl IAnimal {
                fun speak(this) -> string {
                    return "meow";
                }
            }
        }
        initial {
            let d: IAnimal = cast<IAnimal>(new Dog());
            if (d is Dog) {
                println("is dog");
            } else {
                println("not dog");
            }
            if (d is Cat) {
                println("is cat");
            } else {
                println("not cat");
            }
        }
        """,
        "is dog\nnot cat")]
    [Fact]
    public void InterfaceIsObject() => batch.Assert();

    [BatchE2ETest("""
        interface IAnimal {
            fun new(mut this) {}
            fun speak(this) -> string;
        }
        class Dog {
            fun new(mut this) {}
            impl IAnimal {
                fun speak(this) -> string {
                    return "woof";
                }
            }
        }
        initial {
            let d: IAnimal = cast<IAnimal>(new Dog());
            println(d.speak());
        }
        """,
        "woof")]
    [Fact]
    public void InterfaceCastCallVirt() => batch.Assert();

    // 'is InterfaceType' doesn't work with namespace-scoped interfaces in batch mode
    [Fact]
    public void InterfaceIsInterface()
    {
        Assert.Equal("true\n", RunEndToEnd("""
            interface IBase {
                fun new(mut this) {}
            }
            interface IDerived {
                fun new(mut this) {}
            }
            class Impl {
                fun new(mut this) {}
                impl IBase {}
                impl IDerived {}
            }
            initial {
                let obj: IBase = cast<IBase>(new Impl());
                println(cast<string>(obj is IDerived));
            }
            """));
    }

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        enum Color {
            Red;
            Blue;
            impl IShow {
                fun show(this) -> string {
                    return "color";
                }
            }
        }
        initial {
            let c = new Color.Red();
            println(c.show());
        }
        """,
        "color")]
    [Fact]
    public void EnumInterfaceDirectCall() => batch.Assert();

    // 'is InterfaceType' doesn't work with namespace-scoped interfaces in batch mode
    [Fact]
    public void EnumInterfaceIsInstance()
    {
        Assert.Equal("true\nfalse\n", RunEndToEnd("""
            interface IShow {}
            enum Color {
                Red;
                impl IShow {}
            }
            enum Size {
                Big;
            }
            initial {
                let c = new Color.Red();
                let s = new Size.Big();
                println(cast<string>(c is IShow));
                println(cast<string>(s is IShow));
            }
            """));
    }

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        enum Color {
            Red;
            Green;
            Blue;
            impl IShow {
                fun show(this) -> string {
                    if (this is Color.Red) { return "red"; }
                    if (this is Color.Green) { return "green"; }
                    return "blue";
                }
            }
        }
        initial {
            let c1 = new Color.Red();
            let c2 = new Color.Green();
            let c3 = new Color.Blue();
            println(c1.show());
            println(c2.show());
            println(c3.show());
        }
        """,
        "red\ngreen\nblue")]
    [Fact]
    public void EnumInterfaceCallWithLogic() => batch.Assert();

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        class Point {
            x: i32;
            y: i32;
            fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; }
            impl IValueType {}
            impl IShow {
                fun show(this) -> string {
                    return "(" + cast<string>(this.x) + "," + cast<string>(this.y) + ")";
                }
            }
        }
        initial {
            let p = new Point(3, 4);
            let s: IShow = cast<IShow>(p);
            println(s.show());
        }
        """,
        "(3,4)")]
    [Fact]
    public void ValueTypeBoxing() => batch.Assert();

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        class Val {
            x: i32;
            fun new(mut this, x: i32) { this.x = x; }
            impl IValueType {}
            impl IShow {
                fun show(this) -> string { return cast<string>(this.x); }
            }
        }
        initial {
            let v = new Val(42);
            let s: IShow = cast<IShow>(v);
            let v2: Val = cast<Val>(s);
            println(cast<string>(v2.x));
        }
        """,
        "42")]
    [Fact]
    public void ValueTypeUnboxing() => batch.Assert();

    [BatchE2ETest("""
        interface IShow {
            fun show(this) -> string;
        }
        class Val {
            x: i32;
            fun new(mut this, x: i32) { this.x = x; }
            impl IValueType {}
            impl IShow {
                fun show(this) -> string { return cast<string>(this.x); }
            }
        }
        initial {
            let v = new Val(99);
            println(cast<IShow>(v).show());
        }
        """,
        "99")]
    [Fact]
    public void BoxingOptimization() => batch.Assert();

    [BatchE2ETest("""
        interface IFoo {
            fun foo(this) -> string;
        }
        interface IBar {
            fun bar(this) -> string;
        }
        class Multi {
            val: i32;
            fun new(mut this, val: i32) { this.val = val; }
            impl IValueType {}
            impl IFoo {
                fun foo(this) -> string { return "foo=" + cast<string>(this.val); }
            }
            impl IBar {
                fun bar(this) -> string { return "bar=" + cast<string>(this.val); }
            }
        }
        initial {
            let m = new Multi(7);
            let f: IFoo = cast<IFoo>(m);
            let b: IBar = cast<IBar>(m);
            println(f.foo());
            println(b.bar());
        }
        """,
        "foo=7\nbar=7")]
    [Fact]
    public void MultipleInterfaceBoxing() => batch.Assert();
}
