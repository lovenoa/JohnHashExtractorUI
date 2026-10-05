using JohnHashExtractor.Models;

namespace JohnHashExtractor.Services;

public static class OutputNaming
{
    public static string GetOutputPath(string inputPath, string? outputDirectory, OutputFileFormat format)
    {
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(inputPath)
            : outputDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.CurrentDirectory;
        }

        var suffix = format == OutputFileFormat.Hashcat ? ".hashcat.hash" : ".hash";
        return Path.Combine(directory, Path.GetFileName(inputPath) + suffix);
    }
}

public sealed class OutputConversionException : Exception
{
    public OutputConversionException(string message)
        : base(message)
    {
    }
}

public static class OutputFormatter
{
    private static readonly string[] HashcatPrefixes =
    {
        "$7z$",
        "$pkzip$",
        "$pkzip2$",
        "$rar5$",
        "$RAR3$",
        "$keepass$",
        "$office$",
        "$oldoffice$",
        "$pdf$",
        "$bitlocker$",
        "$axcrypt$",
        "$itunes_backup$",
        "$krb5pa$",
        "$krb5tgs$",
        "$sip$",
        "$mysql$",
        "$postgresql$",
        "$mssql$",
        "$odf$"
    };

    public static string Format(
        string converterOutput,
        OutputFileFormat targetFormat,
        string converterOutputFormat)
    {
        if (targetFormat == OutputFileFormat.John)
        {
            if (string.Equals(converterOutputFormat, "Hashcat", StringComparison.OrdinalIgnoreCase))
            {
                throw new OutputConversionException(
                    "当前转换器输出为 Hashcat 格式，无法可靠转换回 John 原始格式。");
            }

            return converterOutput;
        }

        if (string.Equals(converterOutputFormat, "Hashcat", StringComparison.OrdinalIgnoreCase))
        {
            return converterOutput;
        }

        return ConvertJohnToHashcat(converterOutput);
    }

    private static string ConvertJohnToHashcat(string converterOutput)
    {
        var lines = converterOutput.Replace("\r\n", "\n").Split('\n');
        var converted = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var hash = ExtractHashcatHash(line);
            if (hash is null)
            {
                throw new OutputConversionException(
                    $"无法把第 {index + 1} 行可靠转换为 Hashcat 直接格式。" +
                    Environment.NewLine +
                    "请选择 John 原始格式，或使用原转换器提供的 Hashcat 输出模式。");
            }

            converted.Add(hash);
        }

        if (converted.Count == 0)
        {
            throw new OutputConversionException("转换器输出中没有可保存的 Hash。");
        }

        return string.Join(Environment.NewLine, converted) + Environment.NewLine;
    }

    private static string? ExtractHashcatHash(string line)
    {
        if (StartsWithKnownPrefix(line))
        {
            return TrimKnownTerminator(line);
        }

        var markerIndex = FindKnownPrefix(line);
        if (markerIndex > 0 && line[markerIndex - 1] == ':')
        {
            return TrimKnownTerminator(line[markerIndex..]);
        }

        return null;
    }

    private static string TrimKnownTerminator(string hash)
    {
        // zip2john appends archive/file metadata after the Hashcat terminator.
        var terminators = new[] { "*$/pkzip2$", "*$/pkzip$" };
        foreach (var terminator in terminators)
        {
            var index = hash.IndexOf(terminator, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return hash[..(index + terminator.Length)];
            }
        }

        return hash;
    }

    private static bool StartsWithKnownPrefix(string line)
    {
        return HashcatPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static int FindKnownPrefix(string line)
    {
        var index = -1;
        foreach (var prefix in HashcatPrefixes)
        {
            var candidate = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (candidate >= 0 && (index < 0 || candidate < index))
            {
                index = candidate;
            }
        }

        return index;
    }
}
