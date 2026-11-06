using BabyPenguin.Parser;

namespace BabyPenguin.Tests
{
    /// <summary>
    /// File-scoped conditional-compilation splice (batch 2.2):
    /// #define / #if / #elif / #else / #while at definition and statement
    /// level, resolved by MetaSplicer on the parse contexts before the
    /// syntax tree is built. Aligned with EmperorPenguin's SemanticMetaRewrite
    /// with the redesign's FILE-SCOPED define store (CLI pre-seed layer +
    /// per-file code defines + E_DUPLICATE_SYMBOL on same-file redefinition).
    /// Every behavioral test asserts the FULL program output byte-exactly.
    /// </summary>
    public class MetaSpliceTests(ITestOutputHelper helper) : TestBase(helper)
    {
        private static string RunOutput(SemanticCompiler compiler)
        {
            var model = compiler.Compile();
            var vm = new BabyPenguinVM(model);
            vm.Run();
            return vm.CollectOutput();
        }

        // ───────────────────── definition level ─────────────────────

        [Fact]
        public void DefineAndIfIncludesFunction()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                #define("MODE","1");
                #if (#defined("MODE")) {
                    fun answer() -> i64 { return 42; }
                }
                initial {
                    println("answer=" + cast<string>(answer()));
                }
                """));
            Assert.Equal($"answer=42{EOL}", output);
        }

        [Fact]
        public void IfFalseDropsDefinitions()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #if (false) {
                    fun ghost() -> i64 { return 99; }
                }
                #if (!#defined("NOPE")) {
                    fun keeper() -> i64 { return 7; }
                }
                initial {
                    println("keeper=" + cast<string>(keeper()));
                }
                """);
            Assert.Equal($"keeper=7{EOL}", RunOutput(compiler));
        }

        [Fact]
        public void IfElifElseChain()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                #define("B","1");
                #if (#defined("A")) {
                    fun pick() -> string { return "a"; }
                } #elif (#defined("B")) {
                    fun pick() -> string { return "b"; }
                } #else {
                    fun pick() -> string { return "none"; }
                }
                initial {
                    println(pick());
                }
                """));
            Assert.Equal($"b{EOL}", output);
        }

        [Fact]
        public void IfConditionLogicOperators()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                #define("X","1");
                #if (#defined("X") || #defined("Y")) {
                    fun flag() -> i64 { return 1; }
                }
                #if (!#defined("Z") && #defined("X")) {
                    fun flag2() -> i64 { return 2; }
                }
                #if (#defined("X") == true && #defined("Q") == false) {
                    fun flag3() -> i64 { return 3; }
                }
                initial {
                    println(cast<string>(flag() + flag2() + flag3()));
                }
                """));
            Assert.Equal($"6{EOL}", output);
        }

        [Fact]
        public void RetiredOptionDirectiveIsUnsupported()
        {
            // #option("K") reads were RETIRED with the global option table
            // (batch 2.3, redesign §3) — a condition containing the retired
            // directive must not fold (E_UNSUPPORTED), on both implementations.
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #if (#option("MISSING") == "") {
                    fun missing() -> string { return "empty"; }
                }
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("not a compile-time constant", ex.Message);
        }

        [Fact]
        public void IfBareNameValueComparison()
        {
            // Redesign §3 form: a bare identifier in a condition reads its
            // define VALUE (BP-first; EmperorPenguin gets it with batch 2.1).
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                #define("TIER","gold");
                #if (TIER == "gold") {
                    fun level() -> string { return "gold"; }
                } #elif (TIER == "silver") {
                    fun level() -> string { return "silver"; }
                } #else {
                    fun level() -> string { return "bronze"; }
                }
                #if (#defined("TIER") && TIER == "gold") {
                    fun guarded() -> string { return "guarded-gold"; }
                }
                initial {
                    println(level());
                    println(guarded());
                }
                """));
            Assert.Equal($"gold{EOL}guarded-gold{EOL}", output);
        }

        [Fact]
        public void UndefinedBareNameComparisonIsError()
        {
            // An UNDEFINED bare name does not fold — guard with #defined
            // first (same contract as EmperorPenguin's retired #option reads).
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #if (UNDEFINED_NAME == "x") {
                    fun never() -> string { return "never"; }
                }
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("not a compile-time constant", ex.Message);
        }

        [Fact]
        public void WhileFalseDropsBody()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #while (false) {
                    fun dropped() -> i64 { return 1; }
                }
                initial {
                    println("kept");
                }
                """);
            Assert.Equal($"kept{EOL}", RunOutput(compiler));
        }

        [Fact]
        public void WhileDivergenceCapIsError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #while (true) {
                    fun f() -> i64 { return 1; }
                }
                initial { println("unreachable"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("did not converge", ex.Message);
        }

        // ───────────────────── statement level ─────────────────────

        [Fact]
        public void StatementIfSplicesInOrder()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun compute(mode: i64) -> i64 {
                    #if (true) {
                        let half = mode / 2;
                        return half + 1;
                    }
                    return mode;
                }
                initial {
                    println(cast<string>(compute(10)));
                }
                """));
            // The braced branch flattens into the function body: the `let`
            // must be visible to the spliced `return` (not nested in a block).
            Assert.Equal($"6{EOL}", output);
        }

        [Fact]
        public void StatementIfFalseTakesElse()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun pick() -> i64 {
                    #if (#defined("NOPE")) {
                        return 1;
                    } #else {
                        return 2;
                    }
                    return 3;
                }
                initial {
                    println(cast<string>(pick()));
                }
                """));
            Assert.Equal($"2{EOL}", output);
        }

        [Fact]
        public void StatementWhileFalseDropsBody()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun test() -> i64 {
                    #while (false) {
                        return 1;
                    }
                    return 2;
                }
                initial {
                    println(cast<string>(test()));
                }
                """));
            Assert.Equal($"2{EOL}", output);
        }

        [Fact]
        public void StatementDefineAffectsLaterDefsInSameFile()
        {
            // A statement-level #define is processed by the COMPILE-TIME
            // ordered splice walk (the enclosing function never needs to
            // run): from its source line to the end of the FILE, later
            // definitions' and statements' #if conditions observe it.
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun setup() -> i64 {
                    #define("LATE","1");
                    return 0;
                }
                #if (#defined("LATE")) {
                    fun after() -> i64 { return 11; }
                }
                initial {
                    println("late-visible");
                    println(cast<string>(after()));
                }
                """));
            Assert.Equal($"late-visible{EOL}11{EOL}", output);
        }

        [Fact]
        public void StatementDefineInsideInitialIsCompileTime()
        {
            // The #define itself is processed at COMPILE time during the
            // ordered splice walk — definitions after it (in source order)
            // observe the key even though the containing function never runs.
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                initial {
                    #define("SEED","1");
                    println("first");
                }
                #if (#defined("SEED")) {
                    fun after() -> i64 { return 11; }
                }
                initial {
                    println(cast<string>(after()));
                }
                """));
            Assert.Equal($"first{EOL}11{EOL}", output);
        }

        [Fact]
        public void MetaInsideNestedControlFlow()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun nested(x: i64) -> i64 {
                    if (x > 0) {
                        #if (true) {
                            return 100;
                        }
                    }
                    while (x > 100) {
                        #while (false) {
                            x = x + 1;
                        }
                        return 200;
                    }
                    try {
                        #if (#defined("NOPE")) {
                            x = x + 1;
                        } #else {
                            return 400;
                        }
                    } catch (e : __builtin.RuntimeError) {
                        return 500;
                    }
                    return 600;
                }
                initial {
                    println(cast<string>(nested(1)));
                }
                """));
            Assert.Equal($"100{EOL}", output);
        }

        [Fact]
        public void MetaInSingleStatementPosition()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                fun pick(x: i64) -> i64 {
                    if (x > 0) #if (true) { return 1; } #else { return 2; }
                    return 3;
                }
                fun pickNone(x: i64) -> i64 {
                    if (x > 0) #if (false) { return 9; }
                    return 4;
                }
                initial {
                    println(cast<string>(pick(1)));
                    println(cast<string>(pickNone(1)));
                }
                """));
            Assert.Equal($"1{EOL}4{EOL}", output);
        }

        // ───────────────────── containers ─────────────────────

        [Fact]
        public void MetaInsideNamespaceAndClassAndImpl()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                namespace ns {
                    #if (true) {
                        fun val() -> i64 { return 5; }
                    }
                }
                class C {
                    #if (#defined("GATE")) {
                        fun gated() -> i64 { return 1; }
                    }
                    fun plain() -> i64 { return 2; }
                }
                interface IShow { fun show() -> string; }
                impl IShow for C {
                    #if (true) {
                        fun show() -> string { return "C"; }
                    }
                }
                initial {
                    println(cast<string>(ns.val()));
                    let c = new C();
                    println(cast<string>(c.plain()));
                    println(c.show());
                }
                """));
            Assert.Equal($"5{EOL}2{EOL}C{EOL}", output);
        }

        [Fact]
        public void NestedMetaInsideBranches()
        {
            var output = RunOutput(new SemanticCompiler(new ErrorReporter(this)).AddSource("""
                #define("OUTER","1");
                #if (#defined("OUTER")) {
                    #define("INNER","2");
                    #if (#defined("INNER")) {
                        fun deep() -> i64 { return 21; }
                    }
                }
                initial {
                    println(cast<string>(deep()));
                }
                """));
            Assert.Equal($"21{EOL}", output);
        }

        // ───────────────────── file scoping / CLI layer ─────────────────────

        [Fact]
        public void DefinesDoNotLeakAcrossFiles()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #define("MODE","1");
                initial { println("file-a"); }
                """, "a.penguin");
            compiler.AddSource("""
                #if (#defined("MODE")) {
                    fun leaked() -> i64 { return 1; }
                } #else {
                    fun leaked() -> i64 { return 0; }
                }
                initial { println(cast<string>(leaked())); }
                """, "b.penguin");
            Assert.Equal($"file-a{EOL}0{EOL}", RunOutput(compiler));
        }

        [Fact]
        public void CliDefinesAreVisibleToEveryFile()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.CliDefines["DEBUG"] = "1";
            compiler.CliDefines["BARE"] = "1";
            compiler.AddSource("""
                #if (#defined("DEBUG")) {
                    fun mode() -> string { return "debug"; }
                } #else {
                    fun mode() -> string { return "release"; }
                }
                initial { println(mode()); }
                """, "a.penguin");
            compiler.AddSource("""
                #if (#defined("DEBUG")) {
                    initial { println("also-debug"); }
                }
                """, "b.penguin");
            Assert.Equal($"debug{EOL}also-debug{EOL}", RunOutput(compiler));
        }

        [Fact]
        public void CodeDefineOverwritesCliDefine()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.CliDefines["MODE"] = "cli";
            compiler.AddSource("""
                #define("MODE","code");
                #if (MODE == "code") {
                    fun winner() -> string { return "code"; }
                } #else {
                    fun winner() -> string { return "cli"; }
                }
                initial { println(winner()); }
                """);
            Assert.Equal($"code{EOL}", RunOutput(compiler));
        }

        [Fact]
        public void CliDefineSameFileRedefinitionAfterCodeDefineIsError()
        {
            // CLI seed + one code #define = fine; the SECOND code #define of
            // the same key in the same file = E_DUPLICATE_SYMBOL.
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.CliDefines["K"] = "cli";
            compiler.AddSource("""
                #define("K","one");
                #define("K","two");
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_DUPLICATE_SYMBOL, ex.Code);
            Assert.Contains("'K' is already defined", ex.Message);
        }

        [Fact]
        public void SameFileRedefinitionIsDuplicateError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #define("K","1");
                #define("K","2");
                initial { println("x"); }
                """, "dup.penguin");
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_DUPLICATE_SYMBOL, ex.Code);
            Assert.Contains("'K' is already defined in this file", ex.Message);
            Assert.Contains("dup.penguin", ex.Location?.ToString());
        }

        // ───────────────────── malformed / misplaced ─────────────────────

        [Fact]
        public void UnknownMetaCallInDefinitionPositionIsError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                #frobnicate(1);
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("#frobnicate", ex.Message);
        }

        [Fact]
        public void NonConstantIfConditionIsError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                fun runtime_flag() -> bool { return true; }
                #if (runtime_flag()) {
                    fun f() -> i64 { return 1; }
                }
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("not a compile-time constant", ex.Message);
        }

        [Fact]
        public void MisplacedMetaCallInRuntimeExpressionIsError()
        {
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                fun f() -> bool { return #defined("X"); }
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
            Assert.Contains("#defined", ex.Message);
        }

        [Fact]
        public void UnsupportedMemberKindInClassIfIsError()
        {
            // A `let` declaration inside a class-body #if would be silently
            // dropped by the per-kind visitors — the splicer rejects it
            // loudly instead.
            var compiler = new SemanticCompiler(new ErrorReporter(this));
            compiler.AddSource("""
                class C {
                    #if (true) {
                        let field : i64 = 0;
                    }
                }
                initial { println("x"); }
                """);
            var ex = Assert.Throws<BabyPenguin.BabyPenguinException>(() => compiler.Compile());
            Assert.Equal(ErrorCode.E_UNSUPPORTED, ex.Code);
        }
    }
}
