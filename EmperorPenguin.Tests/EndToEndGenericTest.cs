namespace EmperorPenguin.Tests;

/// <summary>
/// End-to-end tests for generic type features.
/// Generic tests use RunEndToEnd individually because generic class specialization
/// may interact with the batch compilation namespace wrapping.
/// </summary>
[Collection("EndToEnd")]
public class EndToEndGenericTest : EndToEndTestBase
{
    [Fact]
    public void GenericClassBasic()
    {
        Assert.Equal("42\n", RunEndToEnd("""
            #template(T: type)
            class Box {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
                fun get(this) -> T {
                    return this.value;
                }
            }
            initial {
                let b = new Box<i32>(42);
                println(cast<string>(b.get()));
            }
            """));
    }

    [Fact]
    public void GenericClassString()
    {
        Assert.Equal("hello\n", RunEndToEnd("""
            #template(T: type)
            class Box {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
                fun get(this) -> T {
                    return this.value;
                }
            }
            initial {
                let b = new Box<string>("hello");
                println(b.get());
            }
            """));
    }

    [Fact]
    public void GenericClassMultipleArgs()
    {
        Assert.Equal("1\n2\n", RunEndToEnd("""
            #template(T: type)
            class Box {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
                fun get(this) -> T {
                    return this.value;
                }
            }
            initial {
                let b1 = new Box<i32>(1);
                let b2 = new Box<i32>(2);
                println(cast<string>(b1.get()));
                println(cast<string>(b2.get()));
            }
            """));
    }

    [Fact]
    public void GenericClassTwoParams()
    {
        Assert.Equal("1\nhello\n", RunEndToEnd("""
            #template(T: type, U: type)
            class Pair {
                first: T;
                second: U;
                fun new(mut this, a: T, b: U) {
                    this.first = a;
                    this.second = b;
                }
                fun get_first(this) -> T {
                    return this.first;
                }
                fun get_second(this) -> U {
                    return this.second;
                }
            }
            initial {
                let p = new Pair<i32, string>(1, "hello");
                println(cast<string>(p.get_first()));
                println(p.get_second());
            }
            """));
    }

    [Fact]
    public void GenericClassNestedArg()
    {
        Assert.Equal("42\n", RunEndToEnd("""
            #template(T: type)
            class Box {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
                fun get(this) -> T {
                    return this.value;
                }
            }
            initial {
                let inner = new Box<i32>(42);
                let outer = new Box<Box<i32>>(inner);
                println(cast<string>(outer.get().get()));
            }
            """));
    }

    [Fact]
    public void GenericClassFieldAccess()
    {
        Assert.Equal("99\n", RunEndToEnd("""
            #template(T: type)
            class Foo {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
            }
            #template(T: type)
            class Bar {
                value: Foo<T>;
                fun new(mut this, v: T) {
                    this.value = new Foo<T>(v);
                }
            }
            initial {
                let b = new Bar<i32>(99);
                println(cast<string>(b.value.value));
            }
            """));
    }

    [Fact]
    public void GenericClassTwoInstancesSameType()
    {
        Assert.Equal("10\n20\n", RunEndToEnd("""
            #template(T: type)
            class Box {
                value: T;
                fun new(mut this, v: T) {
                    this.value = v;
                }
                fun get(this) -> T {
                    return this.value;
                }
            }
            initial {
                let b1 = new Box<i32>(10);
                let b2 = new Box<i32>(20);
                println(cast<string>(b1.get()));
                println(cast<string>(b2.get()));
            }
            """));
    }

    [Fact]
    public void GenericClassFunction()
    {
        Assert.Equal("hello\n", RunEndToEnd("""
            class Foo {
                #template(T: type)
                fun identity(this, x: T) -> T {
                    return x;
                }
            }
            initial {
                let a = new Foo();
                let result: string = a.identity<string>("hello");
                println(result);
            }
            """));
    }

    [Fact]
    public void GenericClassWithGenericFunction()
    {
        Assert.Equal("123\n", RunEndToEnd("""
            #template(T: type)
            class Foo {
                v: T;

                #template(U: type)
                fun foo(mut this, x: U) {
                    this.v = cast<T>(x);
                }
            }
            initial {
                let a = new Foo<i32>();
                a.foo<i64>(123);
                println(cast<string>(a.v));
            }
            """));
    }

    [Fact]
    public void GenericFunctionBasic()
    {
        Assert.Equal("42\n", RunEndToEnd("""
            #template(T: type)
            fun identity(x: T) -> T {
                return x;
            }
            initial {
                let result: i32 = identity<i32>(42);
                println(cast<string>(result));
            }
            """));
    }
}
