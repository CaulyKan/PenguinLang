using BabyPenguin;
using BabyPenguin.SemanticInterface;
using PenguinLangParser;
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
                    let a : i32 = wait test_event;
                    print(cast<string>(a));
                    let b : i32 = wait test_event;
                    print(cast<string>(b));
                    let c : i32 = wait test_event;
                    print(cast<string>(c));
                }
                
                initial {
                    for (let i : i64 in range(0, 3)) {
                        emit test_event(cast<i32>(i));
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
                    let a : i32 = wait test_event;
                    print(cast<string>(a));
                    let b : i32 = wait test_event;
                    print(cast<string>(b));
                    let c : i32 = wait test_event;
                    print(cast<string>(c));
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

                let eq : mut _QueuedEventReceiver<i32> = new _QueuedEventReceiver<i32>(test_event);
                initial {
                    while (true) {
                        let a : Option<i32> = eq.do_wait_any();
                        if (a.is_some()) {
                            let b : i32 = a.some;
                            print(cast<string>(b));
                            if (b == 2) exit(0);
                        }
                    }
                }
                
                initial {
                    for (let i : i64 in range(0, 3)) {
                        emit test_event(cast<i32>(i));
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

                let eq : _AsyncEventReceiver<i32> = new _AsyncEventReceiver<i32>(test_event, on_test_event);
                fun on_test_event(b : i32) {
                    print(cast<string>(b));
                    if (b == 2) exit(0);
                }
                
                initial {
                    for (let i : i64 in range(0, 3)) {
                        emit test_event(cast<i32>(i));
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

                on test_event (b: i32) {
                    print(cast<string>(b));
                    if (b == 2) exit(0);
                }
                
                initial {
                    for (let i : i64 in range(0, 3)) {
                        emit test_event(cast<i32>(i));
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

                    on this.test_event (b: i32) {
                        print(cast<string>(b));
                    }
                    
                    fun foo(this: Foo) {
                        emit this.test_event(cast<i32>(1));
                        emit this.test_event(cast<i32>(2));
                    }
                }

                let f : Foo = new Foo();

                on f.test_event (b: i32) {
                    print(cast<string>(b));
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