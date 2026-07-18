using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using BabyPenguin.VirtualMachine;

namespace BabyPenguin.CSharpBackend
{
    /// <summary>
    /// Emits generated C# source to a temp .csproj, compiles it with `dotnet build`,
    /// caches the resulting assembly by a source+BabyPenguin-dll hash, and loads it
    /// into a collectible AssemblyLoadContext for in-process invocation.
    /// </summary>
    public class DiskCompiler
    {
        private readonly string _babyPenguinDll;
        private readonly string _targetFramework;
        private readonly string _cacheRoot;

        public DiskCompiler(string? targetFramework = null)
        {
            _babyPenguinDll = typeof(BabyPenguinVM).Assembly.Location;
            _targetFramework = string.IsNullOrEmpty(targetFramework) ? DetectTargetFramework() : targetFramework;
            _cacheRoot = Path.Combine(Path.GetTempPath(), "bp_cs_cache");
        }

        /// <summary>Compile the given source files and load the resulting assembly.</summary>
        public Assembly CompileAndLoad(IEnumerable<(string FileName, string Content)> sources, bool keepCs)
        {
            var sourceList = sources.ToList();
            var hash = ComputeHash(sourceList);
            var cacheDir = Path.Combine(_cacheRoot, hash);
            var cachedDll = Path.Combine(cacheDir, "BabyPenguinCompiled.dll");

            if (!File.Exists(cachedDll))
            {
                var buildDir = Path.Combine(Path.GetTempPath(), $"bp_cs_build_{Guid.NewGuid():N}");
                Directory.CreateDirectory(buildDir);
                try
                {
                    WriteProject(buildDir, sourceList);
                    RunDotnetBuild(buildDir);
                    var builtDll = Path.Combine(buildDir, "bin", "Release", _targetFramework, "BabyPenguinCompiled.dll");
                    if (!File.Exists(builtDll))
                        throw new CSharpBackendException($"Build produced no assembly at {builtDll}");
                    Directory.CreateDirectory(cacheDir);
                    foreach (var f in Directory.GetFiles(Path.GetDirectoryName(builtDll)!, "BabyPenguinCompiled.*"))
                        File.Copy(f, Path.Combine(cacheDir, Path.GetFileName(f)), overwrite: true);
                    if (keepCs)
                        KeepSources(cacheDir, sourceList);
                }
                finally
                {
                    if (!keepCs)
                    {
                        try { Directory.Delete(buildDir, recursive: true); } catch { }
                    }
                    else
                    {
                        // Move sources next to the build dir for inspection
                        var keepDir = buildDir + "_kept";
                        try { if (Directory.Exists(keepDir)) Directory.Delete(keepDir, true); Directory.Move(buildDir, keepDir); } catch { }
                        Console.Error.WriteLine($"[cs backend] kept sources at {keepDir}");
                    }
                }
            }

            return LoadAssembly(cachedDll);
        }

        private string ComputeHash(List<(string FileName, string Content)> sources)
        {
            using var sha = SHA256.Create();
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);
            foreach (var (name, content) in sources)
            {
                bw.Write(name);
                bw.Write(content);
            }
            bw.Write(_babyPenguinDll ?? "");
            try { bw.Write(new FileInfo(_babyPenguinDll).LastWriteTimeUtc.Ticks); } catch { }
            var bytes = sha.ComputeHash(ms.ToArray());
            return Convert.ToHexString(bytes);
        }

        private void WriteProject(string dir, List<(string FileName, string Content)> sources)
        {
            var csproj = $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>{_targetFramework}</TargetFramework>
    <Nullable>disable</Nullable>
    <AssemblyName>BabyPenguinCompiled</AssemblyName>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <BaseIntermediateOutputPath>obj\</BaseIntermediateOutputPath>
    <BaseOutputPath>bin\</BaseOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BabyPenguin">
      <HintPath>{_babyPenguinDll}</HintPath>
    </Reference>
  </ItemGroup>
  <ItemGroup>
{string.Join(Environment.NewLine, sources.Select(s => $"    <Compile Include=\"{s.FileName}\" />"))}
  </ItemGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(dir, "BabyPenguinCompiled.csproj"), csproj);
            foreach (var (name, content) in sources)
                File.WriteAllText(Path.Combine(dir, name), content);
        }

        private void RunDotnetBuild(string dir)
        {
            var csproj = Path.Combine(dir, "BabyPenguinCompiled.csproj");
            // No PackageReferences, but a normal build still runs the (trivial) restore
            // phase to materialize obj/project.assets.json in a fresh build dir.
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build -c Release \"{csproj}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = dir,
            };
            using var p = Process.Start(psi) ?? throw new CSharpBackendException("Failed to start dotnet build");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderrText = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                throw new CSharpBackendException(
                    $"dotnet build failed (exit {p.ExitCode}).\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderrText}");
            }
        }

        private static void KeepSources(string dir, List<(string FileName, string Content)> sources)
        {
            foreach (var (name, content) in sources)
                File.WriteAllText(Path.Combine(dir, name), content);
            Console.Error.WriteLine($"[BabyPenguin CS Backend] sources wrote at {dir}");
        }

        public static Assembly LoadAssembly(string dllPath)
        {
            var alc = new CollectibleAlc();
            using var fs = File.OpenRead(dllPath);
            return alc.LoadFromStream(fs);
        }

        private static string DetectTargetFramework()
        {
            // Default to the framework the host BabyPenguin is running on.
            var ver = Environment.Version;
            return $"net{ver.Major}.{ver.Minor}";
        }

        private sealed class CollectibleAlc : AssemblyLoadContext
        {
            public CollectibleAlc() : base(isCollectible: true) { }
            protected override Assembly? Load(AssemblyName assemblyName) => null; // fall back to default (host) for deps
            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) => IntPtr.Zero;
        }
    }

    public class CSharpBackendException(string message) : Exception(message);
}
