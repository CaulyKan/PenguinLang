using BabyPenguin.SemanticInterface;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.Tests
{
    public class NewIRTest(ITestOutputHelper helper) : TestBase(helper)
    {
        private (SemanticModel model, BabyPenguinVM oldVm) CompileAndRunOld(string source)
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(source);
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            return (model, vm);
        }

        [Fact]
        public void NewIR_Generation_Print()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    println(""hello"");
                }
            ");
            var model = compiler.Compile();

            // Generate new IR
            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            // Verify IR contains expected instructions
            Assert.Contains("CONST", irText);
            Assert.Contains("CALL", irText);
        }

        [Fact]
        public void NewIR_Generation_Arithmetic()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let x: i64 = 3 + 4;
                    println(cast<string>(x));
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            Assert.Contains("BINOP", irText);
            Assert.Contains("add", irText);
        }

        [Fact]
        public void NewIR_Generation_IfElse()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    if (1 > 0) {
                        println(""yes"");
                    } else {
                        println(""no"");
                    }
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            Assert.Contains("BR_COND", irText);
            Assert.Contains("CONST", irText);
        }

        [Fact]
        public void NewIR_Generation_WhileLoop()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let i: mut i64 = 0;
                    while (i < 5) {
                        i = i + 1;
                    }
                    println(cast<string>(i));
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            Assert.Contains("BINOP", irText);
            Assert.Contains("lt", irText);
        }

        [Fact]
        public void NewIR_Generation_Functions()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                fun add(a: i64, b: i64) -> i64 {
                    return a + b;
                }
                initial {
                    println(cast<string>(add(3, 4)));
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            Assert.Contains("FUN @", irText);
            Assert.Contains("RET", irText);
        }

        [Fact]
        public void NewIR_Generation_Classes()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                class Node {
                    val: i64;
                    fun new(mut this, v: i64) {
                        this.val = v;
                    }
                }
                initial {
                    let n = new Node(42);
                    println(cast<string>(n.val));
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var irText = module.Display();

            testOutputHelper.WriteLine("=== New IR Output ===");
            testOutputHelper.WriteLine(irText);

            Assert.Contains("NEW", irText);
            Assert.Contains("RDMBR", irText);
        }

        [Fact]
        public void NewIR_TypeClassifier_StringIsRef()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let s: string = ""hello"";
                }
            ");
            var model = compiler.Compile();

            var stringSymbol = model.Symbols.FirstOrDefault(s => s.Name == "s");
            Assert.NotNull(stringSymbol);
            var irType = IRTypeClassifier.ToIrType(stringSymbol.TypeInfo);
            testOutputHelper.WriteLine($"String IR type: {irType}");
            Assert.Equal("ref<string>", irType);
            Assert.True(IRTypeClassifier.IsReferenceTypeIrType(irType));
        }

        [Fact]
        public void NewIR_TypeClassifier_Primitives()
        {
            // Test the classifier directly without needing to compile code that
            // might conflict with builtin symbol names
            Assert.True(IRTypeClassifier.IsValueTypeIrType("i64"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("bool"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("f64"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("i32"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("u64"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("f32"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("char"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("void"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("enum<MyEnum>"));
            Assert.True(IRTypeClassifier.IsValueTypeIrType("struct<MyStruct>"));

            Assert.True(IRTypeClassifier.IsReferenceTypeIrType("ref<string>"));
            Assert.True(IRTypeClassifier.IsReferenceTypeIrType("ref<MyClass>"));
            Assert.True(IRTypeClassifier.IsReferenceTypeIrType("funptr"));
        }

        private string CompileAndRunNew(string source)
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(source);
            var model = compiler.Compile();

            var vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;
            vm.Global.DebugFunc = (s) => testOutputHelper.WriteLine(s.TrimEnd('\n'));
            vm.Initialize();
            vm.Run();
            return vm.CollectOutput().Trim();
        }

        [Fact]
        public void Debug_DumpMainOldIR()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    println(""hello"");
                }
            ");
            var model = compiler.Compile();

            using var sw = new StreamWriter("/tmp/_main_ir_dump.txt");

            // Find _main code container
            var mainCC = model.FindAll(n => n is ICodeContainer cc && cc.FullName() == "__builtin._main")
                .Cast<ICodeContainer>()
                .First();

            sw.WriteLine($"=== Old IR for {mainCC.FullName()} ===");
            sw.WriteLine($"Symbols: {string.Join(", ", mainCC.Symbols.Select(s => $"{s.FullName()}:{s.TypeInfo.FullName()}(param={s.IsParameter})"))}");
            sw.WriteLine($"Instructions ({mainCC.Instructions.Count}):");
            for (int i = 0; i < mainCC.Instructions.Count; i++)
            {
                var inst = mainCC.Instructions[i];
                var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
            }

            // Also dump __builtin.new
            var builtinNewCC = model.FindAll(n => n is ICodeContainer cc && cc.FullName() == "__builtin.new")
                .Cast<ICodeContainer>()
                .FirstOrDefault();
            if (builtinNewCC != null)
            {
                sw.WriteLine($"\n=== Old IR for __builtin.new ===");
                sw.WriteLine($"Symbols: {string.Join(", ", builtinNewCC.Symbols.Select(s => $"{s.FullName()}:{s.TypeInfo.FullName()}(param={s.IsParameter})"))}");
                sw.WriteLine($"Instructions ({builtinNewCC.Instructions.Count}):");
                for (int i = 0; i < builtinNewCC.Instructions.Count; i++)
                {
                    var inst = builtinNewCC.Instructions[i];
                    var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                    sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
                }
            }

            // Also dump the new IR for _main
            var generator = new IRGenerator(model);
            var module = generator.Generate();
            var mainFunc = module.FindFunction("__builtin__main");
            if (mainFunc != null)
            {
                sw.WriteLine("=== New IR for __builtin__main ===");
                sw.WriteLine(module.Display());
            }

            sw.WriteLine("=== All functions ===");
            foreach (var f in module.Functions.Values)
            {
                sw.WriteLine($"  {f.Name}");
            }
            sw.Flush();
        }

        private static string Describe(BabyPenguinIR ir)
        {
            return ir switch
            {
                AssignLiteralToSymbolInstruction a => $"ASSIGN_LITERAL {a.Target.FullName()} = {a.LiteralValue} ({a.Type.FullName()})",
                AssignmentInstruction a => $"ASSIGN {a.LeftHandSymbol.FullName()} = {a.RightHandSymbol.FullName()}",
                BinaryOperationInstruction b => $"BINOP {b.Target.FullName()} = {b.LeftSymbol.FullName()} {b.Operator} {b.RightSymbol.FullName()}",
                UnaryOperationInstruction u => $"UNOP {u.Target.FullName()} = {u.Operator} {u.Operand.FullName()}",
                FunctionCallInstruction f => $"CALL {f.FunctionSymbol.FullName()} target={f.Target?.FullName() ?? "void"} args=[{string.Join(", ", f.Arguments.Select(a => a.FullName()))}]",
                NewInstanceInstruction n => $"NEW {n.Target.FullName()} ({n.Target.TypeInfo.FullName()})",
                ReadMemberInstruction r => $"RDMBR {r.Target.FullName()} = {r.MemberOwnerSymbol.FullName()}.{r.Member.Name}",
                WriteMemberInstruction w => $"WRMBR {w.MemberOwnerSymbol.FullName()}.{w.Member.Name} = {w.Value.FullName()}",
                CastInstruction c => $"CAST {c.Target.FullName()} = ({c.TypeInfo.FullName()}) {c.Operand.FullName()}",
                GotoInstruction g => $"GOTO {g.TargetLabel} cond={g.Condition?.FullName() ?? "none"} jump={g.JumpOnCondition}",
                ReturnInstruction r => $"RET {r.RetValue?.FullName() ?? "void"}",
                SignalInstruction => "SIGNAL",
                NopInstuction => "NOP",
                WriteEnumInstruction w => $"WRITE_ENUM {w.TargetEnum.FullName()} = {w.Value.FullName()}",
                ReadEnumInstruction r => $"READ_ENUM {r.TargetValue.FullName()} = {r.Enum.FullName()}",
                _ => $"UNKNOWN: {ir.GetType().Name}"
            };
        }

        [Fact]
        public void NewVM_Execution_PrintHello()
        {
            var output = CompileAndRunNew(@"
                initial {
                    println(""hello world"");
                }
            ");
            Assert.Equal("hello world", output);
        }

        [Fact]
        public void NewVM_Execution_Arithmetic()
        {
            var output = CompileAndRunNew(@"
                initial {
                    let x: i64 = 3 + 4;
                    println(cast<string>(x));
                }
            ");
            Assert.Equal("7", output);
        }

        [Fact]
        public void NewVM_Execution_IfElse_Then()
        {
            var output = CompileAndRunNew(@"
                initial {
                    if (1 > 0) {
                        println(""yes"");
                    } else {
                        println(""no"");
                    }
                }
            ");
            Assert.Equal("yes", output);
        }

        [Fact]
        public void NewVM_Execution_IfElse_Else()
        {
            var output = CompileAndRunNew(@"
                initial {
                    if (0 > 1) {
                        println(""yes"");
                    } else {
                        println(""no"");
                    }
                }
            ");
            Assert.Equal("no", output);
        }

        [Fact]
        public void NewVM_Execution_WhileLoop()
        {
            var output = CompileAndRunNew(@"
                initial {
                    let i: mut i64 = 0;
                    while (i < 5) {
                        i = i + 1;
                    }
                    println(cast<string>(i));
                }
            ");
            Assert.Equal("5", output);
        }

        [Fact]
        public void NewVM_Execution_Functions()
        {
            var output = CompileAndRunNew(@"
                fun add(a: i64, b: i64) -> i64 {
                    return a + b;
                }
                initial {
                    println(cast<string>(add(10, 20)));
                }
            ");
            Assert.Equal("30", output);
        }

        [Fact]
        public void NewVM_Execution_ClassField()
        {
            var output = CompileAndRunNew(@"
                class Node {
                    val: i64;
                    fun new(mut this, v: i64) {
                        this.val = v;
                    }
                }
                initial {
                    let n = new Node(42);
                    println(cast<string>(n.val));
                }
            ");
            Assert.Equal("42", output);
        }

        [Fact]
        public void NewVM_Execution_ComparisonOldAndNew()
        {
            var source = @"
                initial {
                    let sum: mut i64 = 0;
                    let i: mut i64 = 1;
                    while (i <= 10) {
                        sum = sum + i;
                        i = i + 1;
                    }
                    println(cast<string>(sum));
                }
            ";

            var oldOutput = CompileAndRunOld(source).oldVm.Global.Output.ToString().Trim();
            var newOutput = CompileAndRunNew(source);

            testOutputHelper.WriteLine($"Old VM: {oldOutput}");
            testOutputHelper.WriteLine($"New VM: {newOutput}");

            Assert.Equal(oldOutput, newOutput);
        }

        [Fact]
        public void Debug_QueueTest()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                initial {
                    let a : mut _utils.Queue<i64> = new _utils.Queue<i64>();
                    a.enqueue(1);
                    a.enqueue(2);
                    println(cast<string>(a.size()));
                    let res1 : Option<i64> = a.peek();
                    println(cast<string>(res1.some));
                    a.enqueue(3);
                    let res2 : Option<i64> = a.peek();
                    println(cast<string>(res2.some));
                    a.dequeue();
                    println(cast<string>(a.size()));
                    let res3 : Option<i64> = a.peek();
                    println(cast<string>(res3.some));
                    a.dequeue();
                    let res4 : Option<i64> = a.dequeue();
                    println(cast<string>(res4.some));
                    let res5 : Option<i64> = a.peek();
                    println(cast<string>(res5.is_none()));
                }
            ");
            var model = compiler.Compile();

            // Dump old IR for key functions
            using var sw = new StreamWriter("/tmp/_queue_ir_dump.txt");
            var targets = new[] { "__builtin._main", "__builtin.new", "__builtin.Scheduler.new", "__builtin.Scheduler.entry" };
            foreach (var name in targets)
            {
                var cc = model.FindAll(n => n is ICodeContainer c && c.FullName() == name)
                    .Cast<ICodeContainer>()
                    .FirstOrDefault();
                if (cc == null) continue;
                sw.WriteLine($"\n=== Old IR: {cc.FullName()} ===");
                sw.WriteLine($"Symbols: {string.Join(", ", cc.Symbols.Select(s => $"{s.FullName()}:{s.TypeInfo.FullName()}(param={s.IsParameter})"))}");
                sw.WriteLine($"Instructions ({cc.Instructions.Count}):");
                for (int i = 0; i < cc.Instructions.Count; i++)
                {
                    var inst = cc.Instructions[i];
                    var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                    sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
                }
            }

            // Dump new IR
            var generator = new IRGenerator(model);
            var module = generator.Generate();
            sw.WriteLine("\n=== New IR (all) ===");
            sw.WriteLine(module.Display());
            sw.Flush();

            testOutputHelper.WriteLine("Dump written to /tmp/_queue_ir_dump.txt");

            // Run with debug
            var vm = new BabyPenguinVM(model);
            vm.Global.EnableDebugPrint = true;
            vm.Global.DebugFunc = (s) => testOutputHelper.WriteLine(s.TrimEnd('\n'));
            vm.Initialize();
            try { vm.Run(); } catch (Exception ex) { testOutputHelper.WriteLine($"EXCEPTION: {ex.Message}\n{ex.StackTrace}"); }
            testOutputHelper.WriteLine($"Output: {vm.CollectOutput()}");
        }

        [Fact]
        public void Debug_EventListTest()
        {
            // Test if _utils.List works in a simple push+iterate pattern
            var output = CompileAndRunNew(@"
                initial {
                    let lst : mut _utils.List<i64> = new _utils.List<i64>();
                    lst.push(1);
                    lst.push(2);
                    lst.push(3);
                    for (let x : i64 in lst.iter()) {
                        println(cast<string>(x));
                    }
                }
            ");
            testOutputHelper.WriteLine($"Output: {output}");
            Assert.Equal("1\n2\n3", output);
        }

        [Fact]
        public void Debug_EventNewIRDump()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                event test_event;

                initial {
                    wait test_event;
                    println(""2"");
                }

                initial {
                    println(""1"");
                    emit test_event();
                }
            ");
            var model = compiler.Compile();

            var generator = new IRGenerator(model);
            var module = generator.Generate();

            using var sw = new StreamWriter("/tmp/_event_new_ir_dump.txt");

            // Dump ALL functions in the new IR module
            foreach (var func in module.Functions.Values)
            {
                if (func.Instructions.Count > 0)
                {
                    sw.WriteLine($"\n=== {func.Name} ({func.Instructions.Count} instructions) ===");
                    for (int i = 0; i < func.Instructions.Count; i++)
                    {
                        sw.WriteLine($"  {i}: {func.Instructions[i]}");
                    }
                }
            }
            sw.Flush();
            testOutputHelper.WriteLine("New IR dump written to /tmp/_event_new_ir_dump.txt");
            testOutputHelper.WriteLine($"Total functions: {module.Functions.Count}");
        }

        [Fact]
        public void Debug_DumpEventTestIR()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource(@"
                event test_event;
                initial {
                    wait test_event;
                    println(""2"");
                }
                initial {
                    println(""1"");
                    emit test_event();
                }
            ");
            var model = compiler.Compile();

            using var sw = new StreamWriter("/tmp/_event_test_ir_dump.txt");

            // Dump _main IR
            var mainCC = model.FindAll(n => n is ICodeContainer cc && cc.FullName() == "__builtin._main")
                .Cast<ICodeContainer>()
                .First();
            sw.WriteLine($"=== _main ({mainCC.Instructions.Count} instructions) ===");
            for (int i = 0; i < mainCC.Instructions.Count; i++)
            {
                var inst = mainCC.Instructions[i];
                var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
            }

            // Dump Scheduler.entry IR
            var schedulerEntryCC = model.FindAll(n => n is ICodeContainer cc && cc.FullName() == "__builtin.Scheduler.entry")
                .Cast<ICodeContainer>()
                .FirstOrDefault();
            if (schedulerEntryCC != null)
            {
                sw.WriteLine($"\n=== Scheduler.entry ({schedulerEntryCC.Instructions.Count} instructions) ===");
                for (int i = 0; i < schedulerEntryCC.Instructions.Count; i++)
                {
                    var inst = schedulerEntryCC.Instructions[i];
                    var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                    sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
                }
            }

            // Dump Event.notify IR
            var eventNotifyCC = model.FindAll(n => n is ICodeContainer cc && cc.FullName().EndsWith(".notify") && cc.FullName().Contains("Event"))
                .Cast<ICodeContainer>()
                .FirstOrDefault();
            if (eventNotifyCC != null)
            {
                sw.WriteLine($"\n=== {eventNotifyCC.FullName()} ({eventNotifyCC.Instructions.Count} instructions) ===");
                for (int i = 0; i < eventNotifyCC.Instructions.Count; i++)
                {
                    var inst = eventNotifyCC.Instructions[i];
                    var labels = inst.Labels.Count > 0 ? $"[{string.Join(",", inst.Labels)}] " : "";
                    sw.WriteLine($"  {i}: {labels}{Describe(inst)}");
                }
            }

            sw.Flush();
            testOutputHelper.WriteLine("IR dump written to /tmp/_event_test_ir_dump.txt");
        }
    }
}
