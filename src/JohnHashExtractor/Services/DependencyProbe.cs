using System.Diagnostics;
using System.Text.RegularExpressions;
using JohnHashExtractor.Models;

namespace JohnHashExtractor.Services;

public sealed class DependencyProbe
{
    private static readonly HashSet<string> PythonStandardLibrary = new(StringComparer.OrdinalIgnoreCase)
    {
        "argparse", "base64", "binascii", "collections", "contextlib", "csv", "datetime",
        "email", "enum", "fnmatch", "getpass", "glob", "hashlib", "hmac", "html", "http",
        "io", "json", "logging", "math", "os", "pathlib", "plistlib", "random", "re",
        "secrets", "shutil", "socket", "sqlite3", "ssl", "string", "struct", "subprocess",
        "sys", "tempfile", "time", "traceback", "typing", "urllib", "uuid", "warnings",
        "xml", "zipfile"
    };

    private readonly ProcessRunner _processRunner = new();
    private readonly Dictionary<string, string> _runtimePaths;
    private readonly Dictionary<string, bool> _pythonModuleCache = new(StringComparer.OrdinalIgnoreCase);

    public DependencyProbe(IReadOnlyDictionary<string, string>? runtimePaths = null)
    {
        _runtimePaths = runtimePaths is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(runtimePaths, StringComparer.OrdinalIgnoreCase);
    }

    public ConverterCandidate Probe(string filePath, ConverterRuntime runtime, string family)
    {
        var name = Path.GetFileName(filePath);
        var shebang = ReadShebang(filePath);
        var actualRuntime = runtime == ConverterRuntime.Unknown ? DetectRuntimeFromShebang(shebang) : runtime;
        var runtimeDisplay = actualRuntime.ToString();
        string? interpreter = null;
        string message;

        switch (actualRuntime)
        {
            case ConverterRuntime.Native:
                message = "Native executable";
                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null, ConverterAvailability.Available, message);

            case ConverterRuntime.Python:
                var pythonVersion = GetPythonVersion(shebang);
                runtimeDisplay = pythonVersion;
                interpreter = FindPythonInterpreter(pythonVersion);
                if (interpreter is null)
                {
                    return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null,
                        ConverterAvailability.MissingRuntime, $"缺少 {pythonVersion} 运行环境");
                }

                var pythonCheck = CheckPythonScript(interpreter, filePath);
                if (!pythonCheck.IsAvailable)
                {
                    return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, interpreter,
                        pythonCheck.Status, pythonCheck.Detail);
                }

                var moduleCheck = CheckPythonModules(interpreter, filePath);
                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, interpreter,
                    moduleCheck.IsAvailable ? ConverterAvailability.Available : ConverterAvailability.MissingDependency,
                    moduleCheck.Detail);

            case ConverterRuntime.Perl:
                interpreter = FindExecutable("perl", "perl.exe");
                runtimeDisplay = "Perl";
                if (interpreter is null)
                {
                    return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null,
                        ConverterAvailability.MissingRuntime, "缺少 Perl 运行环境");
                }

                var perlCheck = CheckCommand(interpreter, new[] { "-c", filePath }, "Perl");
                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, interpreter,
                    perlCheck.IsAvailable ? ConverterAvailability.Available : ConverterAvailability.MissingDependency,
                    perlCheck.Detail);

            case ConverterRuntime.Node:
                interpreter = FindExecutable("node", "node.exe");
                runtimeDisplay = "Node.js";
                if (interpreter is null)
                {
                    return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null,
                        ConverterAvailability.MissingRuntime, "缺少 Node.js 运行环境");
                }

                var nodeCheck = CheckCommand(interpreter, new[] { "--check", filePath }, "Node.js");
                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, interpreter,
                    nodeCheck.IsAvailable ? ConverterAvailability.Available : ConverterAvailability.MissingDependency,
                    nodeCheck.Detail);

            case ConverterRuntime.Lua:
                interpreter = FindExecutable("lua", "lua.exe");
                runtimeDisplay = "Lua";
                if (interpreter is null)
                {
                    return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null,
                        ConverterAvailability.MissingRuntime, "缺少 Lua 运行环境");
                }

                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, interpreter,
                    ConverterAvailability.Available, "Lua runtime found");

            default:
                return MakeCandidate(name, filePath, family, actualRuntime, runtimeDisplay, null,
                    ConverterAvailability.Unknown, "无法确定运行环境，请手动确认");
        }
    }

    public IReadOnlyList<DependencyStatus> GetEnvironmentStatus()
    {
        return new[]
        {
            CheckRuntime("Python 3", "python", "python.exe", "python3", "python3.exe"),
            CheckPython2Runtime(),
            CheckRuntime("Perl", "perl", "perl.exe"),
            CheckRuntime("Node.js", "node", "node.exe"),
            CheckRuntime("Lua", "lua", "lua.exe")
        };
    }

    private DependencyStatus CheckPython2Runtime()
    {
        var python2 = FindExecutable("python2", "python2.exe");
        if (python2 is not null)
        {
            return new DependencyStatus { Name = "Python 2", IsAvailable = true, Detail = python2 };
        }

        var pyLauncher = FindExecutable("py", "py.exe");
        if (pyLauncher is not null)
        {
            var probe = _processRunner.RunAsync(new ProcessRequest
            {
                FileName = pyLauncher,
                Arguments = new[] { "-2", "--version" },
                Timeout = TimeSpan.FromSeconds(8)
            }).GetAwaiter().GetResult();
            return new DependencyStatus
            {
                Name = "Python 2",
                IsAvailable = probe.ExitCode == 0,
                Detail = probe.ExitCode == 0 ? pyLauncher : "py.exe 未安装 Python 2"
            };
        }

        return new DependencyStatus { Name = "Python 2", IsAvailable = false, Detail = "未找到 Python 2" };
    }

    private DependencyStatus CheckRuntime(string displayName, params string[] names)
    {
        var path = FindExecutable(names);
        return new DependencyStatus
        {
            Name = displayName,
            IsAvailable = path is not null,
            Detail = path ?? "未找到可执行文件"
        };
    }

    private static ConverterCandidate MakeCandidate(
        string name,
        string filePath,
        string family,
        ConverterRuntime runtime,
        string runtimeDisplay,
        string? interpreter,
        ConverterAvailability availability,
        string message)
    {
        return new ConverterCandidate
        {
            Name = name,
            FilePath = filePath,
            Family = family,
            Runtime = runtime,
            RuntimeDisplay = runtimeDisplay,
            InterpreterPath = interpreter,
            Availability = availability,
            DependencyMessage = message
        };
    }

    private (bool IsAvailable, ConverterAvailability Status, string Detail) CheckPythonScript(string interpreter, string filePath)
    {
        var result = _processRunner.RunAsync(new ProcessRequest
        {
            FileName = interpreter,
            Arguments = new[]
            {
                "-c",
                "import pathlib,sys; compile(pathlib.Path(sys.argv[1]).read_bytes(),sys.argv[1],'exec')",
                filePath
            },
            Timeout = TimeSpan.FromSeconds(8)
        }).GetAwaiter().GetResult();

        if (result.ExitCode == 0)
        {
            return (true, ConverterAvailability.Available, "Python syntax check passed");
        }

        return (false, ConverterAvailability.Incompatible, TrimDiagnostic(result.StandardError));
    }

    private (bool IsAvailable, string Detail) CheckPythonModules(string interpreter, string filePath)
    {
        var modules = GetTopLevelPythonModules(filePath)
            .Where(module => !PythonStandardLibrary.Contains(module))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missing = new List<string>();
        foreach (var module in modules)
        {
            var key = interpreter + "|" + module;
            if (!_pythonModuleCache.TryGetValue(key, out var available))
            {
                var probe = _processRunner.RunAsync(new ProcessRequest
                {
                    FileName = interpreter,
                    Arguments = new[]
                    {
                        "-c",
                        "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec(sys.argv[1]) else 1)",
                        module
                    },
                    Timeout = TimeSpan.FromSeconds(8)
                }).GetAwaiter().GetResult();
                available = probe.ExitCode == 0;
                _pythonModuleCache[key] = available;
            }

            if (!available)
            {
                missing.Add(module);
            }
        }

        return missing.Count == 0
            ? (true, modules.Length == 0 ? "Python modules not required" : "Python modules available")
            : (false, "缺少 Python 模块：" + string.Join(", ", missing));
    }

    private (bool IsAvailable, string Detail) CheckCommand(string executable, IReadOnlyList<string> arguments, string displayName)
    {
        var result = _processRunner.RunAsync(new ProcessRequest
        {
            FileName = executable,
            Arguments = arguments,
            Timeout = TimeSpan.FromSeconds(8)
        }).GetAwaiter().GetResult();

        return result.ExitCode == 0
            ? (true, $"{displayName} dependency check passed")
            : (false, TrimDiagnostic(result.StandardError));
    }

    private string? FindPythonInterpreter(string version)
    {
        var names = version switch
        {
            "Python 2" => new[] { "python2", "python2.exe" },
            "Python 3" => new[] { "python3", "python3.exe", "python", "python.exe", "py", "py.exe" },
            _ => new[] { "python", "python.exe", "py", "py.exe" }
        };

        return FindExecutable(names);
    }

    private string? FindExecutable(params string[] names)
    {
        foreach (var name in names)
        {
            if (_runtimePaths.TryGetValue(name, out var configured) && File.Exists(configured))
            {
                return configured;
            }

            if (Path.IsPathRooted(name) && File.Exists(name))
            {
                return name;
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var extensions = name.Contains('.')
                ? new[] { string.Empty }
                : new[] { ".exe", ".cmd", ".bat", string.Empty };

            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim(), name + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static string GetPythonVersion(string shebang)
    {
        if (shebang.Contains("python2", StringComparison.OrdinalIgnoreCase))
        {
            return "Python 2";
        }

        return "Python 3";
    }

    private static ConverterRuntime DetectRuntimeFromShebang(string shebang)
    {
        if (shebang.Contains("python", StringComparison.OrdinalIgnoreCase))
        {
            return ConverterRuntime.Python;
        }

        if (shebang.Contains("perl", StringComparison.OrdinalIgnoreCase))
        {
            return ConverterRuntime.Perl;
        }

        if (shebang.Contains("node", StringComparison.OrdinalIgnoreCase))
        {
            return ConverterRuntime.Node;
        }

        if (shebang.Contains("lua", StringComparison.OrdinalIgnoreCase))
        {
            return ConverterRuntime.Lua;
        }

        return ConverterRuntime.Unknown;
    }

    private static string ReadShebang(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            return reader.ReadLine() ?? string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> GetTopLevelPythonModules(string filePath)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(filePath))
        {
            if (line.StartsWith(" ") || line.StartsWith("\t") || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = Regex.Match(line, @"^\s*(?:from|import)\s+([A-Za-z_][A-Za-z0-9_.]*)");
            if (match.Success)
            {
                modules.Add(match.Groups[1].Value.Split('.')[0]);
            }
        }

        return modules;
    }

    private static string TrimDiagnostic(string value)
    {
        var lines = value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .ToArray();

        return lines.Length == 0 ? "依赖检查失败" : string.Join(Environment.NewLine, lines.Take(6));
    }
}
