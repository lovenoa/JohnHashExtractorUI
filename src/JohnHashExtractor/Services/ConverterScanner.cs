using System.Text.RegularExpressions;
using JohnHashExtractor.Models;

namespace JohnHashExtractor.Services;

public sealed class ConverterScanner
{
    private static readonly Regex ConverterNamePattern = new(
        @"^(?:.*2john(?:-alt)?|convert2john)(?:\.[^.]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DependencyProbe _dependencyProbe;

    public ConverterScanner(DependencyProbe dependencyProbe)
    {
        _dependencyProbe = dependencyProbe;
    }

    public ConverterScanResult Scan(string runDirectory)
    {
        if (!Directory.Exists(runDirectory))
        {
            throw new DirectoryNotFoundException($"John run 目录不存在：{runDirectory}");
        }

        var candidates = new List<ConverterCandidate>();
        foreach (var filePath in Directory.EnumerateFiles(runDirectory))
        {
            var name = Path.GetFileName(filePath);
            if (!ConverterNamePattern.IsMatch(name))
            {
                continue;
            }

            var runtime = GetRuntime(filePath);
            var family = GetFamily(name);
            candidates.Add(_dependencyProbe.Probe(filePath, runtime, family));
        }

        return new ConverterScanResult
        {
            RunDirectory = Path.GetFullPath(runDirectory),
            Candidates = candidates
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Environment = _dependencyProbe.GetEnvironmentStatus()
        };
    }

    public ConverterCandidate ProbeCustom(string filePath)
    {
        var candidate = _dependencyProbe.Probe(filePath, GetRuntime(filePath), "custom");
        var availability = File.Exists(filePath) ? candidate.Availability : ConverterAvailability.MissingDependency;
        var message = File.Exists(filePath)
            ? "自定义转换器：仅支持转换器 输入文件，结果取 stdout。" + candidate.DependencyMessage
            : "自定义转换器文件不存在。";

        return new ConverterCandidate
        {
            Name = candidate.Name,
            FilePath = candidate.FilePath,
            Family = candidate.Family,
            Runtime = candidate.Runtime,
            RuntimeDisplay = candidate.RuntimeDisplay,
            Availability = availability,
            InterpreterPath = candidate.InterpreterPath,
            DependencyMessage = message,
            IsCustom = true
        };
    }

    private static ConverterRuntime GetRuntime(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".exe" => ConverterRuntime.Native,
            ".py" => ConverterRuntime.Python,
            ".pl" => ConverterRuntime.Perl,
            ".js" => ConverterRuntime.Node,
            ".lua" => ConverterRuntime.Lua,
            _ => ConverterRuntime.Unknown
        };
    }

    private static string GetFamily(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        stem = Regex.Replace(stem, @"-alt$", string.Empty, RegexOptions.IgnoreCase);
        return stem
            .Replace("2john", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('_', '-', '.')
            .ToLowerInvariant();
    }
}

public sealed class ConverterScanResult
{
    public required string RunDirectory { get; init; }
    public required IReadOnlyList<ConverterCandidate> Candidates { get; init; }
    public required IReadOnlyList<DependencyStatus> Environment { get; init; }
}
