namespace JohnHashExtractor.Models;

public enum ConverterRuntime
{
    Unknown,
    Native,
    Python,
    Perl,
    Node,
    Lua
}

public enum ConverterAvailability
{
    Unknown,
    Available,
    MissingRuntime,
    MissingDependency,
    Incompatible
}

public enum OutputFileFormat
{
    John,
    Hashcat
}

public sealed class ConverterCandidate
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string Family { get; init; }
    public required ConverterRuntime Runtime { get; init; }
    public required string RuntimeDisplay { get; init; }
    public required ConverterAvailability Availability { get; init; }
    public string? InterpreterPath { get; init; }
    public string DependencyMessage { get; init; } = string.Empty;
    public bool IsCustom { get; init; }

    public string DisplayName => IsCustom
        ? $"[自定义] {Name} [{Availability}]"
        : $"{Name} [{Availability}]";

    public override string ToString() => DisplayName;
}

public sealed class AppConfig
{
    public string? JohnRunDirectory { get; set; }
    public string? OutputDirectory { get; set; }
    public OutputFileFormat OutputFileFormat { get; set; } = OutputFileFormat.John;
    public Dictionary<string, string> RuntimePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CustomConverterPaths { get; set; } = new();
}

public sealed class DependencyStatus
{
    public required string Name { get; init; }
    public required bool IsAvailable { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class ExtractionResult
{
    public required bool Success { get; init; }
    public required string ConverterName { get; init; }
    public required string ConverterPath { get; init; }
    public required string Command { get; init; }
    public required int ReturnCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public string OutputPath { get; set; } = string.Empty;
    public string OutputFormat { get; init; } = "John";
    public string DependencyMessage { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public bool RawOutputOnly { get; init; }

    public string HashOutput => StandardOutput;
}

public sealed class ProcessRequest
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed class ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool TimedOut { get; init; }
}

public sealed class ConverterUnavailableException : Exception
{
    public ConverterUnavailableException(string message, IReadOnlyList<ConverterCandidate>? alternatives = null)
        : base(message)
    {
        Alternatives = alternatives ?? Array.Empty<ConverterCandidate>();
    }

    public IReadOnlyList<ConverterCandidate> Alternatives { get; }
}
