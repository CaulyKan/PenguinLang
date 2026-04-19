namespace BabyPenguin.Tests
{
    public class ProjectTest(ITestOutputHelper helper) : TestBase(helper)
    {
        [Fact]
        public void ProjectFile_WithExplicitSources_CompilesAllFiles()
        {
            // Arrange: Create temporary project directory with multiple files
            var tempDir = Path.Combine(Path.GetTempPath(), "penguin_project_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var fileA = Path.Combine(tempDir, "a.penguin");
                var fileB = Path.Combine(tempDir, "b.penguin");
                var projectFile = Path.Combine(tempDir, "test.penguins");

                File.WriteAllText(fileA, @"
                    namespace LibA {
                        fun add(x: i64, y: i64) -> i64 {
                            return x + y;
                        }
                    }
                ");

                File.WriteAllText(fileB, @"
                    initial {
                        let result: i64 = LibA.add(10, 20);
                        println(cast<string>(result));
                    }
                ");

                File.WriteAllText(projectFile, @"[project]
                    name = ""TestProject""
                    sources = [
                        ""a.penguin"",
                        ""b.penguin""
                    ]
                ");

                // Act: Compile using project file
                var compiler = new SemanticCompiler(new ErrorReporter(this));
                compiler.AddProject(projectFile);
                var model = compiler.Compile();
                var vm = new BabyPenguinVM(model);
                vm.Run();
                var output = vm.CollectOutput();

                // Assert
                Assert.Equal("30" + EOL, output);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ProjectFile_WithGlobPattern_CompilesMatchingFiles()
        {
            // Arrange: Create directory structure with subdirectory
            var tempDir = Path.Combine(Path.GetTempPath(), "penguin_glob_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));

            try
            {
                var mainFile = Path.Combine(tempDir, "main.penguin");
                var libFile1 = Path.Combine(tempDir, "src", "lib1.penguin");
                var libFile2 = Path.Combine(tempDir, "src", "lib2.penguin");
                var excludedFile = Path.Combine(tempDir, "excluded.penguin");
                var projectFile = Path.Combine(tempDir, "test.penguins");

                File.WriteAllText(mainFile, @"
                    initial {
                        println(cast<string>(Lib1.value()));
                        println(cast<string>(Lib2.value()));
                    }
                ");

                File.WriteAllText(libFile1, @"
                    namespace Lib1 {
                        fun value() -> i64 { return 100; }
                    }
                ");

                File.WriteAllText(libFile2, @"
                    namespace Lib2 {
                        fun value() -> i64 { return 200; }
                    }
                ");

                File.WriteAllText(excludedFile, @"
                    // This file should NOT be compiled
                    initial {
                        println(""should not execute"");
                    }
                ");

                File.WriteAllText(projectFile, @"[project]
                    name = ""GlobTest""
                    sources = [
                        ""main.penguin"",
                        ""src/*.penguin""
                    ]
                ");

                // Act
                var compiler = new SemanticCompiler(new ErrorReporter(this));
                compiler.AddProject(projectFile);
                var model = compiler.Compile();
                var vm = new BabyPenguinVM(model);
                vm.Run();
                var output = vm.CollectOutput();

                // Assert - should not include "should not execute" because excluded.penguin is not in sources
                Assert.Equal("100" + EOL + "200" + EOL, output);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ProjectFile_WithoutSources_CompileAllPenguinFiles()
        {
            // Arrange: Create directory with .penguin files, no explicit sources
            var tempDir = Path.Combine(Path.GetTempPath(), "penguin_default_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var file1 = Path.Combine(tempDir, "file1.penguin");
                var file2 = Path.Combine(tempDir, "file2.penguin");
                var projectFile = Path.Combine(tempDir, "test.penguins");

                File.WriteAllText(file1, @"
                    namespace Math {
                        fun square(x: i64) -> i64 { return x * x; }
                    }
                ");

                File.WriteAllText(file2, @"
                    initial {
                        println(cast<string>(Math.square(5)));
                    }
                ");

                File.WriteAllText(projectFile, @"[project]
                    name = ""DefaultTest""
                ");

                // Act
                var compiler = new SemanticCompiler(new ErrorReporter(this));
                compiler.AddProject(projectFile);
                var model = compiler.Compile();
                var vm = new BabyPenguinVM(model);
                vm.Run();
                var output = vm.CollectOutput();

                // Assert
                Assert.Equal("25" + EOL, output);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void ProjectFile_InvalidFile_ThrowsException()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "penguin_error_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var projectFile = Path.Combine(tempDir, "invalid.penguins");
                File.WriteAllText(projectFile, @"invalid toml content [[[");

                // Act & Assert
                Assert.ThrowsAny<Exception>(() =>
                {
                    var compiler = new SemanticCompiler(new ErrorReporter(this));
                    compiler.AddProject(projectFile);
                });
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
