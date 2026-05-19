namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for interface features.
/// Interface tests use RunEndToEnd individually because the interface dispatch
/// infrastructure doesn't yet work correctly in batch mode (namespace-scoped interfaces).
/// </summary>
[Collection("EndToEnd")]
public class EndToEndInterfaceTest : EndToEndTestBase
{
    [Fact]
    public void InterfaceImpl()
    {
        Assert.Equal("(1,2)\n", RunEndToEnd("""
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
            """));
    }

    [Fact]
    public void InterfaceDefaultMethod()
    {
        Assert.Equal("hello\n", RunEndToEnd("""
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
            """));
    }

    [Fact]
    public void InterfaceOverrideDefault()
    {
        Assert.Equal("hi from Bar\n", RunEndToEnd("""
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
            """));
    }

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

    [Fact]
    public void InterfaceIsObject()
    {
        Assert.Equal("is dog\nnot cat\n", RunEndToEnd("""
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
            """));
    }

    [Fact]
    public void InterfaceCastCallVirt()
    {
        Assert.Equal("woof\n", RunEndToEnd("""
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
            """));
    }

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

    [Fact]
    public void EnumInterfaceDirectCall()
    {
        Assert.Equal("color\n", RunEndToEnd("""
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
            """));
    }

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

    [Fact]
    public void EnumInterfaceCallWithLogic()
    {
        Assert.Equal("red\ngreen\nblue\n", RunEndToEnd("""
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
            """));
    }

    [Fact]
    public void ValueTypeBoxing()
    {
        Assert.Equal("(3,4)\n", RunEndToEnd("""
            interface IShow {
                fun show(this) -> string;
            }
            class Point {
                x: i32;
                y: i32;
                fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; }
                impl ICopy {}
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
            """));
    }

    [Fact]
    public void ValueTypeUnboxing()
    {
        Assert.Equal("42\n", RunEndToEnd("""
            interface IShow {
                fun show(this) -> string;
            }
            class Val {
                x: i32;
                fun new(mut this, x: i32) { this.x = x; }
                impl ICopy {}
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
            """));
    }

    // TODO: Enum boxing needs fixing - virtual dispatch passes ptr to method expecting struct value
    // [Fact]
    // public void EnumBoxing() { ... }

    [Fact]
    public void BoxingOptimization()
    {
        Assert.Equal("99\n", RunEndToEnd("""
            interface IShow {
                fun show(this) -> string;
            }
            class Val {
                x: i32;
                fun new(mut this, x: i32) { this.x = x; }
                impl ICopy {}
                impl IShow {
                    fun show(this) -> string { return cast<string>(this.x); }
                }
            }
            initial {
                let v = new Val(99);
                println(cast<IShow>(v).show());
            }
            """));
    }

    [Fact]
    public void MultipleInterfaceBoxing()
    {
        Assert.Equal("foo=7\nbar=7\n", RunEndToEnd("""
            interface IFoo {
                fun foo(this) -> string;
            }
            interface IBar {
                fun bar(this) -> string;
            }
            class Multi {
                val: i32;
                fun new(mut this, val: i32) { this.val = val; }
                impl ICopy {}
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
            """));
    }
}
