using BabyPenguin;
using BabyPenguin.SemanticInterface;
using PenguinLangSyntax;
using Xunit;

namespace BabyPenguin.Tests
{
    public class EventTest
    {
        [Fact]
        public void EmitAndWaitEvent()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event;

                initial {
                    wait test_event;
                    print(""2"");
                }

                initial {
                    print(""1"");
                    emit test_event();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("12", vm.CollectOutput());
        }

        [Fact]
        public void EmitWaitResultEvent()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event : i32;

                initial {
                    var a : i32 = wait test_event;
                    print(a as string);
                    a = wait test_event;
                    print(a as string);
                    a = wait test_event;
                    print(a as string);
                }
                
                initial {
                    for (var i : i64 in range(0, 3)) {
                        emit test_event(i as i32);
                        wait;
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void EmitWithImplicitCastWaitResultEvent()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event : i32;

                initial {
                    var a : i32 = wait test_event;
                    print(a as string);
                    a = wait test_event;
                    print(a as string);
                    a = wait test_event;
                    print(a as string);
                }
                
                initial {
                    emit test_event(0);
                    emit test_event(1);
                    emit test_event(2);
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void QueuedEventReceiverTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event : i32;

                var eq : _QueuedEventReceiver<i32> = new _QueuedEventReceiver<i32>(test_event);
                initial {
                    while (true) {
                        val a : Option<i32> = eq.do_wait_any();
                        if (a.is_some()) {
                            val b : i32 = a.some;
                            print(b as string);
                            if (b == 2) exit(0);
                        }
                    }
                }
                
                initial {
                    for (var i : i64 in range(0, 3)) {
                        emit test_event(i as i32);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void AsyncEventReceiverTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event : i32;

                var eq : _AsyncEventReceiver<i32> = new _AsyncEventReceiver<i32>(test_event, on_test_event);
                fun on_test_event(val b : i32) {
                    print(b as string);
                    if (b == 2) exit(0);
                }
                
                initial {
                    for (var i : i64 in range(0, 3)) {
                        emit test_event(i as i32);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void OnEventTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                event test_event : i32;

                on test_event (val b: i32) {
                    print(b as string);
                    if (b == 2) exit(0);
                }
                
                initial {
                    for (var i : i64 in range(0, 3)) {
                        emit test_event(i as i32);
                    }
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("012", vm.CollectOutput());
        }

        [Fact]
        public void OnEventClassTest()
        {
            var compiler = new SemanticCompiler();
            compiler.AddSource(@"
                class Foo {
                    event test_event : i32;

                    on this.test_event (val b: i32) {
                        print(b as string);
                    }
                    
                    fun foo(var this: Foo) {
                        emit this.test_event(1 as i32);
                        emit this.test_event(2 as i32);
                    }
                }

                var f : Foo = new Foo();

                on f.test_event (val b: i32) {
                    print(b as string);
                }

                initial {
                    f.foo();
                }
            ");
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            Assert.Equal("1122", vm.CollectOutput());
        }
    }
}