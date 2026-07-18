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

        /// <summary>Build a STANDALONE executable (with a Main entry point) at outExePath, copying
        /// BabyPenguin's runtime deps alongside so it runs without BabyPenguin hosting it.
        /// outExePath is the desired exe path without extension (e.g. /tmp/myprog).</summary>
        public string CompileExe(IEnumerable<(string FileName, string Content)> sources, string outExePath, bool keepCs)
        {
            var sourceList = sources.ToList();
            var outDir = Path.GetDirectoryName(outExePath);
            if (string.IsNullOrEmpty(outDir)) outDir = ".";
            var assemblyName = Path.GetFileNameWithoutExtension(outExePath);
            if (string.IsNullOrEmpty(assemblyName)) assemblyName = "BabyPenguinCompiled";
            Directory.CreateDirectory(outDir);

            var buildDir = Path.Combine(Path.GetTempPath(), $"bp_cs_exe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(buildDir);
            try
            {
                WriteProject(buildDir, sourceList, exe: true, assemblyName, projectName: assemblyName);
                RunDotnetBuild(buildDir, assemblyName);
                var builtDir = Path.Combine(buildDir, "bin", "Release", _targetFramework);
                // Copy the built exe + its files to outDir.
                foreach (var f in Directory.GetFiles(builtDir))
                    File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)), overwrite: true);
                // The exe references BabyPenguin.dll (HintPath) which isn't copied by the build —
                // bring BabyPenguin's runtime deps so the standalone exe can resolve them.
                CopyBabyPenguinDeps(outDir, assemblyName);
            }
            finally
            {
                if (!keepCs)
                {
                    try { Directory.Delete(buildDir, recursive: true); } catch { }
                }
                else
                {
                    var keepDir = buildDir + "_kept";
                    try { if (Directory.Exists(keepDir)) Directory.Delete(keepDir, true); Directory.Move(buildDir, keepDir); } catch { }
                    Console.Error.WriteLine($"[cs backend] kept sources at {keepDir}");
                }
            }
            return Path.Combine(outDir, assemblyName);
        }

        private void CopyBabyPenguinDeps(string outDir, string exeAssemblyName)
        {
            var bpDir = Path.GetDirectoryName(_babyPenguinDll);
            if (string.IsNullOrEmpty(bpDir) || !Directory.Exists(bpDir)) return;
            foreach (var f in Directory.GetFiles(bpDir))
            {
                var name = Path.GetFileName(f);
                // Skip the new exe's own files (already copied) and the BabyPenguin apphost/pdb.
                if (name.StartsWith(exeAssemblyName + ".", StringComparison.OrdinalIgnoreCase)) continue;
                var ext = Path.GetExtension(name);
                if (ext == ".dll" || ext == ".json" || ext == ".so")
                {
                    var dest = Path.Combine(outDir, name);
                    if (!File.Exists(dest)) File.Copy(f, dest, overwrite: false);
                }
            }
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

        private void WriteProject(string dir, List<(string FileName, string Content)> sources) =>
            WriteProject(dir, sources, exe: false, assemblyName: "BabyPenguinCompiled", projectName: "BabyPenguinCompiled");

        private void WriteProject(string dir, List<(string FileName, string Content)> sources, bool exe, string assemblyName, string projectName)
        {
            var outputType = exe ? "Exe" : "Library";
            var csproj = $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>{outputType}</OutputType>
    <TargetFramework>{_targetFramework}</TargetFramework>
    <Nullable>disable</Nullable>
    <AssemblyName>{assemblyName}</AssemblyName>
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
            File.WriteAllText(Path.Combine(dir, projectName + ".csproj"), csproj);
            foreach (var (name, content) in sources)
                File.WriteAllText(Path.Combine(dir, name), content);
        }

        private void RunDotnetBuild(string dir) => RunDotnetBuild(dir, "BabyPenguinCompiled");

        private void RunDotnetBuild(string dir, string projectName)
        {
            var csproj = Path.Combine(dir, projectName + ".csproj");
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
