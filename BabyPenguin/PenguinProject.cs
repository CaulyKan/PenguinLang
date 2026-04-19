using System.Diagnostics;
using Tomlet;
using Tomlet.Models;

namespace BabyPenguin;

/// <summary>
/// Represents a .penguins project file configuration
/// </summary>
public class PenguinProject
{
    /// <summary>
    /// Gets or sets the project section containing name and sources
    /// </summary>
    public ProjectSection Project { get; set; } = new();

    /// <summary>
    /// Loads a .penguins project file from the specified path
    /// </summary>
    /// <param name="projectFilePath">Path to the .penguins file</param>
    /// <returns>Parsed project configuration</returns>
    /// <exception cref="BabyPenguinException">Thrown when file cannot be parsed</exception>
    public static PenguinProject Load(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new BabyPenguinException($"Project file not found: {projectFilePath}", SourceLocation.Empty());
        }

        var tomlContent = File.ReadAllText(projectFilePath);
        var parser = new TomlParser();
        try
        {
            var tomlRoot = parser.Parse(tomlContent);
            var project = new PenguinProject();

            // Parse [project] section
            if (tomlRoot.TryGetValue("project", out var projectValue) && projectValue is TomlTable projectTable)
            {
                if (projectTable.TryGetValue("name", out var nameValue) && nameValue is TomlString nameString)
                {
                    project.Project.Name = nameString.Value;
                }

                if (projectTable.TryGetValue("sources", out var sourcesValue) && sourcesValue is TomlArray sourcesArray)
                {
                    project.Project.Sources = sourcesArray
                        .Where(item => item is TomlString)
                        .Cast<TomlString>()
                        .Select(s => s.Value)
                        .ToList() ?? [];
                }
            }

            return project;
        }
        catch (Exception ex)
        {
            throw new BabyPenguinException($"Failed to parse project file: {ex.Message}", SourceLocation.Empty());
        }
    }

    /// <summary>
    /// Resolves all source files from the project configuration.
    /// Uses glob patterns and defaults to all .penguin files if no sources specified.
    /// </summary>
    /// <param name="projectDirectory">Directory containing the .penguins file</param>
    /// <returns>List of absolute paths to .penguin source files</returns>
    public List<string> ResolveSourceFiles(string projectDirectory)
    {
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // If sources are specified, resolve glob patterns
        if (Project.Sources != null && Project.Sources.Count > 0)
        {
            foreach (var pattern in Project.Sources)
            {
                var matchedFiles = GlobPatternResolve(projectDirectory, pattern);
                foreach (var file in matchedFiles)
                {
                    if (File.Exists(file) && Path.GetExtension(file).Equals(".penguin", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceFiles.Add(Path.GetFullPath(file));
                    }
                }
            }
        }
        else
        {
            // Default: all .penguin files in project directory and subdirectories
            var allFiles = Directory.GetFiles(projectDirectory, "*.penguin", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                sourceFiles.Add(Path.GetFullPath(file));
            }
        }

        return sourceFiles.OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Resolves glob patterns to file paths
    /// </summary>
    private static List<string> GlobPatternResolve(string baseDirectory, string pattern)
    {
        var result = new List<string>();

        // Split pattern by directory separators
        var parts = pattern.Split(['/', '\\']);

        // Check if pattern contains wildcards
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            return ExpandGlob(baseDirectory, parts);
        }
        else
        {
            // Simple file path - no globbing needed
            var fullPath = Path.IsPathRooted(pattern)
                ? pattern
                : Path.Combine(baseDirectory, pattern);

            if (File.Exists(fullPath))
            {
                result.Add(fullPath);
            }
        }

        return result;
    }

    /// <summary>
    /// Expands glob pattern to matching files
    /// </summary>
    private static List<string> ExpandGlob(string baseDirectory, string[] patternParts)
    {
        var result = new List<string>();
        ExpandGlobRecursive(baseDirectory, patternParts, 0, result);
        return result;
    }

    private static void ExpandGlobRecursive(string currentDir, string[] patternParts, int partIndex, List<string> result)
    {
        if (partIndex >= patternParts.Length)
        {
            return;
        }

        var part = patternParts[partIndex];
        var isLastPart = partIndex == patternParts.Length - 1;

        // Handle ** (recursive directory wildcard)
        if (part == "**")
        {
            // Add all files in current directory
            if (isLastPart)
            {
                var files = Directory.GetFiles(currentDir, "*.penguin");
                result.AddRange(files);
            }
            else
            {
                // Continue to next part in subdirectories
                ExpandGlobRecursive(currentDir, patternParts, partIndex + 1, result);
            }

            // Recurse into subdirectories
            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                ExpandGlobRecursive(subDir, patternParts, partIndex, result);
            }
            return;
        }

        // Handle * and ? wildcards
        if (part.Contains('*') || part.Contains('?'))
        {
            var searchPattern = part;
            var fileOrDirs = isLastPart
                ? Directory.GetFileSystemEntries(currentDir, searchPattern)
                : Directory.GetDirectories(currentDir, searchPattern);

            foreach (var entry in fileOrDirs)
            {
                if (isLastPart)
                {
                    if (File.Exists(entry) && Path.GetExtension(entry).Equals(".penguin", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry);
                    }
                }
                else
                {
                    ExpandGlobRecursive(entry, patternParts, partIndex + 1, result);
                }
            }
        }
        else
        {
            // No wildcard - exact match
            var nextPath = Path.Combine(currentDir, part);

            if (!Directory.Exists(nextPath) && !File.Exists(nextPath))
            {
                return; // Path doesn't exist
            }

            if (isLastPart)
            {
                if (File.Exists(nextPath) && Path.GetExtension(nextPath).Equals(".penguin", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(nextPath);
                }
            }
            else if (Directory.Exists(nextPath))
            {
                ExpandGlobRecursive(nextPath, patternParts, partIndex + 1, result);
            }
        }
    }
}

/// <summary>
/// Represents the [project] section of a .penguins file
/// </summary>
public class ProjectSection
{
    /// <summary>
    /// Gets or sets the project name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of source file patterns
    /// </summary>
    public List<string> Sources { get; set; } = new();
}
