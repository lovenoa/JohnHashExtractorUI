using JohnHashExtractor.Models;
using JohnHashExtractor.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: JohnHashExtractor.Smoke <john-run-dir> [--custom input-file] | [input-file]");
    return 2;
}

var runDirectory = Path.GetFullPath(args[0]);
const string SevenZipExample = "$7z$0$14$0$$11$33363437353138333138300000000000$2365089182$16$12$d00321533b483f54a523f624a5f63269";
var johnOutput = "ratios.7z:" + SevenZipExample;
var hashcatOutput = OutputFormatter.Format(johnOutput, OutputFileFormat.Hashcat, "John").Trim();
if (hashcatOutput != SevenZipExample)
{
    throw new InvalidOperationException("7z Hashcat output conversion failed");
}

const string PkzipV1Example = "$pkzip$1*1*2*0*0*0*0*0*0*0*0*0*0*0*0*$/pkzip$";
const string PkzipV2Example = "$pkzip2$1*1*2*0*0*0*0*0*0*0*0*0*0*0*0*0*$/pkzip2$";
var pkzipV1Output = OutputFormatter.Format(
    "archive.zip/file.txt:" + PkzipV1Example + ":file.txt:archive.zip::archive.zip",
    OutputFileFormat.Hashcat,
    "John").Trim();
var pkzipV2Output = OutputFormatter.Format(
    "archive.zip:" + PkzipV2Example + "::file.txt:archive.zip:archive.zip",
    OutputFileFormat.Hashcat,
    "John").Trim();
if (pkzipV1Output != PkzipV1Example || pkzipV2Output != PkzipV2Example)
{
    throw new InvalidOperationException("ZIP Hashcat output conversion failed");
}

var hashcatPath = OutputNaming.GetOutputPath(@"C:\input\ratios.7z", @"C:\output", OutputFileFormat.Hashcat);
if (!string.Equals(hashcatPath, @"C:\output\ratios.7z.hashcat.hash", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Hashcat output naming failed: " + hashcatPath);
}

var scanner = new ConverterScanner(new DependencyProbe());
var scan = scanner.Scan(runDirectory);

Console.WriteLine($"Run directory: {scan.RunDirectory}");
Console.WriteLine($"Converters: {scan.Candidates.Count}");
foreach (var environment in scan.Environment)
{
    Console.WriteLine($"Environment {environment.Name}: {(environment.IsAvailable ? "OK" : "MISSING")} ({environment.Detail})");
}

foreach (var candidate in scan.Candidates.Take(12))
{
    Console.WriteLine($"Converter {candidate.Name}: {candidate.Availability}; {candidate.DependencyMessage}");
}

if (args.Length >= 3 && string.Equals(args[1], "--custom", StringComparison.OrdinalIgnoreCase))
{
    var customScript = Path.Combine(Path.GetTempPath(), "jhe-smoke-converter.py");
    File.WriteAllText(customScript, "print('test:$sha256$0000$smoke')");
    var customInput = Path.GetFullPath(args[2]);
    var customCandidate = scanner.ProbeCustom(customScript);
    var customExtraction = new ExtractionService(scanner, new DependencyProbe(), new ProcessRunner());
    var customResult = await customExtraction.ExtractAsync(customInput, customCandidate, new AppConfig
    {
        JohnRunDirectory = runDirectory,
        CustomConverterPaths = { customScript }
    }, scan);

    Console.WriteLine($"Custom result success: {customResult.Success}");
    Console.WriteLine($"Custom raw output only: {customResult.RawOutputOnly}");
    Console.WriteLine($"Custom output: {customResult.StandardOutput}");
    Console.WriteLine($"Custom command: {customResult.Command}");
    var autoResult = await customExtraction.ExtractAsync(customInput, null, new AppConfig
    {
        JohnRunDirectory = runDirectory,
        CustomConverterPaths = { customScript }
    }, scan);
    if (string.Equals(autoResult.ConverterName, customCandidate.Name, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Custom converter was selected automatically");
    }
    return customResult.Success ? 0 : 1;
}

if (args.Length < 2)
{
    return 0;
}

var inputPath = Path.GetFullPath(args[1]);
var extraction = new ExtractionService(scanner, new DependencyProbe(), new ProcessRunner());
var result = await extraction.ExtractAsync(inputPath, null, new AppConfig
{
    JohnRunDirectory = runDirectory
}, scan);

Console.WriteLine($"Result success: {result.Success}");
Console.WriteLine($"Converter: {result.ConverterName}");
Console.WriteLine($"Command: {result.Command}");
Console.WriteLine($"Return code: {result.ReturnCode}");
Console.WriteLine($"Output format: {result.OutputFormat}");
Console.WriteLine($"Failure: {result.FailureReason}");
Console.WriteLine("--- stdout ---");
Console.WriteLine(result.StandardOutput);
Console.WriteLine("--- stderr ---");
Console.WriteLine(result.StandardError);

return result.Success ? 0 : 1;
