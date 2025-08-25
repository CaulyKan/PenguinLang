namespace BabyPenguin.Tests
{
    public class MutabilityTest
    {
        [Fact]
        public void MutDefinition()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    let x: i32 = 10;
                    let y: mut i32 = 20;
                }
            ");
            var model = compiler.Compile();
            var x = model.ResolveSymbol("ns.x");
            Assert.Equal(Mutability.Immutable, x!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Immutable, x!.IsMutable);
            Assert.Equal("!mut i32", x.TypeInfo.FullName());
            var y = model.ResolveSymbol("ns.y");
            Assert.Equal(Mutability.Mutable, y!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Mutable, y!.IsMutable);
            Assert.Equal("mut i32", y.TypeInfo.FullName());
        }

        [Fact]
        public void MutClassDefinition()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                namespace ns {
                    class A{
                        x: i32 = 10;
                        y: mut i32 = 20;
                        z: !mut i32 = 30;
                    }

                    let a1 : A;
                    let a2 : mut A;
                }
            ");
            var model = compiler.Compile();
            var a1x = model.ResolveSymbol("ns.a1.x");
            Assert.Equal(Mutability.Auto, a1x!.TypeInfo.IsMutable);
            Assert.Equal(Mutability.Immutable, a1x!.IsMutable);
            Assert.Equal("i32", a1x.TypeInfo.FullName());
            var a1y = model.ResolveSymbol("ns.a1.y");
            Assert.Equal(Mutability.Mutable, a1y!.IsMutable);
            Assert.Equal("mut i32", a1y.TypeInfo.FullName());
            var a1z = model.ResolveSymbol("ns.a1.z");
            Assert.Equal(Mutability.Immutable, a1z!.IsMutable);
            Assert.Equal("!mut i32", a1z.TypeInfo.FullName());
            var a2x = model.ResolveSymbol("ns.a2.x");
            Assert.Equal(Mutability.Mutable, a2x!.IsMutable);
            Assert.Equal("i32", a2x.TypeInfo.FullName());
        }

        [Fact]
        public void MutableAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let x: mut i32 = 1;
                    x += 1;
                    print(x as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ImmutabilityAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let x: i32 = 1;
                    x += 1;
                    print(x as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassMutableAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: i32 = 1;
                }
                initial {
                    let a : mut A = new A();
                    a.a = 2;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("2", vm.CollectOutput());
        }

        [Fact]
        public void ClassImmutableAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: i32 = 0;
                }
                initial {
                    let a : A = new A();
                    a.a = 1;
                    print(x as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassForceImmutableAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: !mut i32 = 0;
                }
                initial {
                    let a : mut A = new A();
                    a.a = 1;
                    print(x as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassMutableMemberAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: mut i32 = 0;
                }
                initial {
                    let a : A = new A();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void CascadeClassMutableMemberAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class B {
                    a : A = new A();
                }
                class A {
                    a: mut i32 = 0;
                }
                initial {
                    let b : mut B = new B();
                    b.a.a = 1;
                    print(b.a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void CascadeEnumMutableMemberAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                enum B {
                    a : A;
                    b;
                }
                class A {
                    a: mut i32 = 0;
                }
                initial {
                    let b : mut B = new B.a(new A());
                    b.a.a = 1;
                    print(b.a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void CascadeClassMutableMemberAssignmentTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class B {
                    a : A = new A();
                }
                class A {
                    a: mut i32 = 0;
                }
                initial {
                    let b : B = new B();
                    b.a.a = 1;
                    print(b.a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void CascadeClassMutableMemberAssignmentTest3()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class B {
                    a : A = new A();
                }
                class A {
                    a: i32 = 0;
                }
                initial {
                    let b : mut B = new B();
                    b.a.a = 1;
                    print(b.a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void CascadeClassImmutableMemberAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class B {
                    a : A = new A();
                }
                class A {
                    a: i32 = 0;
                }
                initial {
                    let b : B = new B();
                    b.a.a = 1;
                    print(b.a.a as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassGenericMemberAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: T;
                }
                initial {
                    let a : A<i32> = new A<i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassGenericMemberAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: T;
                }
                initial {
                    let a : A<mut i32> = new A<mut i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void ClassGenericMemberImplicitImmutabilityAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: T;
                }
                initial {
                    let a : mut A<i32> = new A<i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ClassGenericMemberAutoMutabilityAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: auto T;
                }
                initial {
                    let a : mut A<i32> = new A<i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void ClassGenericMemberForceMutableAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: mut T = 0;
                }
                initial {
                    let a : A<i32> = new A<i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void ClassGenericMemberForceMutableImcompatibleTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: mut T = 0;
                }
                initial {
                    let a : mut A<!mut i32> = new A<!mut i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void ClassGenericMemberForceMutableImcompatibleErrorTest2()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A<T> {
                    a: !mut T = 0;
                }
                initial {
                    let a : mut A<mut i32> = new A<mut i32>();
                    a.a = 1;
                    print(a.a as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void MutableToImmutableInitialAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : mut Box<i32> = new Box<i32>(1);
                    let b : Box<i32> = a;
                    print(b.value as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void MutableToImmutableAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : mut Box<i32> = new Box<i32>(1);
                    let b : Box<i32>;
                    b = a;
                    print(b.value as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ImmutableToMutableAssignmentErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : Box<i32> = new Box<i32>(1);
                    let b : mut Box<i32>;
                    b = a;
                    print(b.value as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void ImmutableToMutableValueTypeAssignmentTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : i32 = 1;
                    let b : mut i32;
                    b = a;
                    print(b as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void FunctionCallMutabilityTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : Box<i32> = new Box<i32>(1);
                    let b : mut Box<i32> = new Box<i32>(2);
                    let c : mut Box<i32> = new Box<i32>(2);
                    foo(a, b, c);
                }

                fun foo(a: Box<i32>, b: mut Box<i32>, c: Box<i32>){
                    print(a.value as string);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1", vm.CollectOutput());
        }

        [Fact]
        public void FunctionCallMutabilityErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                initial {
                    let a : Box<i32> = new Box<i32>(1);
                    foo(a, b, c);
                }

                fun foo(a: mut Box<i32>){
                    print(a.value as string);
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

        [Fact]
        public void FunctionCallThisMutabilityTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: i32 = 1;
                    fun immutable(this) {
                        print(this.a as string);
                    }
                    fun mutable(mut this) {
                        print(this.a as string);
                    }
                }
                initial {
                    let a : A = new A();
                    a.immutable();
                    let b: mut A = new A();
                    b.mutable();
                    b.immutable();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("111", vm.CollectOutput());
        }

        [Fact]
        public void FunctionCallThisMutabilityErrorTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class A {
                    a: i32 = 1;
                    fun immutable(this) {
                        print(this.a as string);
                    }
                    fun mutable(mut this) {
                        print(this.a as string);
                    }
                }
                initial {
                    let a : A = new A();
                    a.mutable();
                }
            ");
            Assert.Throws<BabyPenguinException>(() => compiler.Compile());
        }

    }
}