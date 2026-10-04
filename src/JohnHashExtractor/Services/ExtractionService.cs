using System.ComponentModel;
using System.Text;
using JohnHashExtractor.Models;

namespace JohnHashExtractor.Services;

public sealed class ExtractionService
{
    private readonly ConverterScanner _scanner;
    private readonly ProcessRunner _processRunner;

    public ExtractionService(ConverterScanner scanner, DependencyProbe dependencyProbe, ProcessRunner processRunner)
    {
        _scanner = scanner;
        _ = dependencyProbe;
        _processRunner = processRunner;
    }

    public async Task<ExtractionResult> ExtractAsync(
        string inputPath,
        ConverterCandidate? selectedCandidate,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        var scan = await Task.Run(() => _scanner.Scan(config.JohnRunDirectory ?? string.Empty), cancellationToken);
        return await ExtractAsync(inputPath, selectedCandidate, config, scan, cancellationToken);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string inputPath,
        ConverterCandidate? selectedCandidate,
        AppConfig config,
        ConverterScanResult scan,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("输入文件不存在。", inputPath);
        }

        var type = FileTypeDetector.Detect(inputPath);
        var candidate = ResolveCandidate(type, selectedCandidate, scan.Candidates);
        var targetOutputFormat = candidate.IsCustom ? OutputFileFormat.John : config.OutputFileFormat;
        var outputPath = OutputNaming.GetOutputPath(inputPath, config.OutputDirectory, targetOutputFormat);
        var request = BuildRequest(candidate, inputPath, scan.RunDirectory);
        var command = ProcessRunner.FormatCommand(request.FileName, request.Arguments);
        var outputFormat = candidate.IsCustom ? "Raw" : "John";

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ExtractionResult
            {
                Success = false,
                ConverterName = candidate.Name,
                ConverterPath = candidate.FilePath,
                Command = command,
                ReturnCode = -1,
                StandardOutput = string.Empty,
                StandardError = ex.Message,
                OutputFormat = outputFormat,
                DependencyMessage = candidate.DependencyMessage,
                FailureReason = candidate.IsCustom
                    ? "无法启动自定义转换器。仅支持与 John 相同的调用方式：转换器 输入文件，结果取 stdout。原始错误：" + ex.Message
                    : "无法启动转换器：" + ex.Message,
                RawOutputOnly = candidate.IsCustom
            };
        }

        var output = processResult.StandardOutput;
        if (processResult.TimedOut)
        {
            return Fail(candidate, command, processResult, "转换器执行超时。", outputFormat, output);
        }

        if (processResult.ExitCode != 0)
        {
            var reason = candidate.IsCustom
                ? $"自定义转换器返回码 {processResult.ExitCode}。仅支持“转换器 输入文件”并从 stdout 读取结果，其他调用方式不受支持。"
                : $"转换器返回码 {processResult.ExitCode}。";
            return Fail(candidate, command, processResult, reason, outputFormat, output);
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return Fail(
                candidate,
                command,
                processResult,
                "转换器没有产生非空 stdout，不能视为成功。",
                outputFormat,
                output);
        }

        return new ExtractionResult
        {
            Success = true,
            ConverterName = candidate.Name,
            ConverterPath = candidate.FilePath,
            Command = command,
            ReturnCode = processResult.ExitCode,
            StandardOutput = output,
            StandardError = processResult.StandardError,
            OutputPath = outputPath,
            OutputFormat = outputFormat,
            DependencyMessage = candidate.DependencyMessage,
            RawOutputOnly = candidate.IsCustom
        };
    }

    public async Task SaveAsync(
        ExtractionResult result,
        string destination,
        OutputFileFormat outputFormat,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException("转换失败，不能保存 Hash 输出。");
        }

        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException("输出文件已存在：" + destination);
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = result.RawOutputOnly
            ? result.StandardOutput
            : OutputFormatter.Format(result.StandardOutput, outputFormat, result.OutputFormat);

        await File.WriteAllTextAsync(destination, content, new UTF8Encoding(false), cancellationToken);
    }

    private static ConverterCandidate ResolveCandidate(
        DetectedFileType type,
        ConverterCandidate? selectedCandidate,
        IReadOnlyList<ConverterCandidate> discovered)
    {
        if (selectedCandidate is not null)
        {
            if (selectedCandidate.Availability != ConverterAvailability.Available)
            {
                throw new ConverterUnavailableException(
                    $"转换器 {selectedCandidate.Name} 当前不可用：{selectedCandidate.DependencyMessage}");
            }

            return selectedCandidate;
        }

        var matching = discovered
            .Where(candidate => !candidate.IsCustom)
            .Where(candidate => type.ConverterFamilies.Contains(candidate.Family, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var available = matching
            .Where(candidate => candidate.Availability == ConverterAvailability.Available)
            .OrderByDescending(candidate => candidate.Runtime == ConverterRuntime.Native)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (available.Length > 0)
        {
            return available[0];
        }

        if (matching.Length > 0)
        {
            throw new ConverterUnavailableException(
                "没有满足依赖条件的默认转换器。" + Environment.NewLine +
                string.Join(Environment.NewLine, matching.Select(candidate => $"{candidate.Name}: {candidate.DependencyMessage}")),
                matching);
        }

        throw new ConverterUnavailableException(
            "当前 John 安装目录中没有可识别的默认转换器，请手动选择一个转换器。",
            discovered);
    }

    private static ProcessRequest BuildRequest(
        ConverterCandidate candidate,
        string inputPath,
        string workingDirectory)
    {
        if (candidate.InterpreterPath is not null)
        {
            return new ProcessRequest
            {
                FileName = candidate.InterpreterPath,
                Arguments = new[] { candidate.FilePath, inputPath },
                WorkingDirectory = workingDirectory
            };
        }

        return new ProcessRequest
        {
            FileName = candidate.FilePath,
            Arguments = new[] { inputPath },
            WorkingDirectory = workingDirectory
        };
    }

    private static ExtractionResult Fail(
        ConverterCandidate candidate,
        string command,
        ProcessResult processResult,
        string reason,
        string outputFormat,
        string? output = null)
    {
        return new ExtractionResult
        {
            Success = false,
            ConverterName = candidate.Name,
            ConverterPath = candidate.FilePath,
            Command = command,
            ReturnCode = processResult.ExitCode,
            StandardOutput = output ?? processResult.StandardOutput,
            StandardError = processResult.StandardError,
            OutputFormat = outputFormat,
            DependencyMessage = candidate.DependencyMessage,
            FailureReason = reason,
            RawOutputOnly = candidate.IsCustom
        };
    }
}
