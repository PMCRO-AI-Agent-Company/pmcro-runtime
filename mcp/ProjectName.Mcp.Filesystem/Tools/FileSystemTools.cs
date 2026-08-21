// src/Mcps/ProjectName.Mcp.Filesystem/Tools/FileSystemTools.cs
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ProjectName.Mcp.Filesystem.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace ProjectName.Mcp.Filesystem.Tools;

[McpServerToolType]
public sealed class FilesystemTools(FilesystemConfig config, ILogger<FilesystemTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string Result(bool success, object? data = null, string? error = null) =>
        JsonSerializer.Serialize(new { success, data, error }, JsonOptions);

    [McpServerTool(Name = "desktop-commander__read_file")]
    public async Task<string> ReadFileAsync(string path)
    {
        try
        {
            string fullPath = config.ResolveAndValidatePath(path);
            if (!File.Exists(fullPath)) return Result(false, error: $"File not found: {path}");
            string content = await File.ReadAllTextAsync(fullPath);
            logger.LogInformation("[FS] Read file: {Path}", path);
            return Result(true, new { path, content });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "desktop-commander__write_file")]
    public async Task<string> WriteFileAsync(string path, string content)
    {
        try
        {
            string fullPath = config.ResolveAndValidatePath(path);
            string? dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(fullPath, content);
            logger.LogInformation("[FS] Wrote {Bytes} bytes to: {Path}", content.Length, path);
            return Result(true, new { path, bytes_written = content.Length });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "desktop-commander__list_directory")]
    public string ListDirectory(string path = "")
    {
        try
        {
            string fullPath = config.ResolveAndValidatePath(path);
            var entries = Directory.GetFileSystemEntries(fullPath).Select(e => new {
                name = Path.GetFileName(e),
                type = Directory.Exists(e) ? "directory" : "file"
            });
            logger.LogInformation("[FS] Listed directory: {Path}", path);
            return Result(true, new { path, entries });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "desktop-commander__get_file_info")]
    public string GetFileInfo(string path)
    {
        try
        {
            string fullPath = config.ResolveAndValidatePath(path);
            var exists = File.Exists(fullPath) || Directory.Exists(fullPath);
            if (!exists) return Result(true, new { exists = false });
            var info = new FileInfo(fullPath);
            return Result(true, new { exists = true, size = info.Length, modified = info.LastWriteTimeUtc });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "desktop-commander__start_search")]
    [Description("Search for files matching a pattern.")]
    public string SearchFiles(string pattern, string path = "")
    {
        try
        {
            string root = config.ResolveAndValidatePath(path);
            var files = Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(config.SandboxRoot, f));
            return Result(true, new { results = files });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "GrepContent")]
    [Description("Search for text inside files.")]
    public string GrepContent(string pattern, string path = "", string filePattern = "*.*")
    {
        try
        {
            string root = config.ResolveAndValidatePath(path);
            var matches = new List<object>();
            var files = Directory.GetFiles(root, filePattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new { file = Path.GetRelativePath(config.SandboxRoot, file), line = i + 1, text = lines[i].Trim() });
                    }
                }
            }
            return Result(true, new { matches });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "ListSkills")]
    [Description("List available skill packs under skills/ — each entry is a directory containing a SKILL.md file.")]
    public string ListSkills(string skillsDir = "skills")
    {
        try
        {
            string root = config.ResolveAndValidatePath(skillsDir);
            if (!Directory.Exists(root))
                return Result(true, new { skills_dir = skillsDir, skills = Array.Empty<object>() });

            var skills = Directory.GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
                .Select(d =>
                {
                    var name = Path.GetFileName(d);
                    var dataFiles = Directory.GetFiles(d, "*.json")
                        .Select(f => Path.GetFileName(f))
                        .ToArray();
                    return new { name, path = Path.GetRelativePath(config.SandboxRoot, d).Replace('\\', '/'), data_files = dataFiles };
                })
                .ToArray();

            logger.LogInformation("[FS] Listed {Count} skill(s) under {Dir}", skills.Length, skillsDir);
            return Result(true, new { skills_dir = skillsDir, skills });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    [McpServerTool(Name = "LoadSkill")]
    [Description("Load a skill pack in one call: returns SKILL.md content plus the contents of any sibling .json data files (e.g. earned-constraints.json, brand-profile.json).")]
    public async Task<string> LoadSkillAsync(string skillName, string skillsDir = "skills")
    {
        try
        {
            string skillPath = config.ResolveAndValidatePath(Path.Combine(skillsDir, skillName));
            if (!Directory.Exists(skillPath))
                return Result(false, error: $"Skill not found: {skillName} (looked in {skillsDir}/{skillName})");

            string skillMdPath = Path.Combine(skillPath, "SKILL.md");
            if (!File.Exists(skillMdPath))
                return Result(false, error: $"SKILL.md not found in {skillsDir}/{skillName}");

            string skillMd = await File.ReadAllTextAsync(skillMdPath);

            var dataFiles = new Dictionary<string, object?>();
            foreach (var jsonFile in Directory.GetFiles(skillPath, "*.json"))
            {
                var key = Path.GetFileName(jsonFile);
                try
                {
                    var content = await File.ReadAllTextAsync(jsonFile);
                    dataFiles[key] = JsonSerializer.Deserialize<object>(content);
                }
                catch (Exception ex)
                {
                    dataFiles[key] = new { error = $"Failed to parse {key}: {ex.Message}" };
                }
            }

            logger.LogInformation("[FS] Loaded skill: {Skill} ({DataFiles} data file(s))", skillName, dataFiles.Count);
            return Result(true, new
            {
                skill_name = skillName,
                skill_md = skillMd,
                data_files = dataFiles
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }
}
