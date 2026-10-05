##This project was completed by AI

# John Hash Extractor

Windows GUI front end for John the Ripper Jumbo's existing `*2john` conversion tools. The application launches converters only; it does not crack passwords or convert Hashcat output back into John format.

## Requirements

- Windows 10/11 x64
- .NET 8 SDK to build, or a self-contained .NET 8 publish to run
- A John the Ripper `run` directory containing `john.exe` and the converter tools
- Python, Perl, Node.js, or Lua when a selected converter is a script

The application reports missing interpreters, incompatible Python versions, missing Python modules, and missing Perl modules. It does not install dependencies.

## Build and run

```powershell
dotnet build JohnHashExtractor.slnx
dotnet run --project src/JohnHashExtractor/JohnHashExtractor.csproj
```

The first launch asks for the John `run` directory. The path is stored in `%LOCALAPPDATA%\JohnHashExtractor\settings.json`.

## Implemented flow

1. Scan the selected `run` directory for `*2john` and optional `convert2john.*` tools.
2. Probe converter runtimes and dependencies.
3. Detect common file types from extension and basic signatures.
4. Select a dependency-ready converter, or allow manual selection.
5. Launch it with `ProcessStartInfo.ArgumentList`, with no shell command concatenation.
6. Display converter, command, return code, stdout, stderr, output format, and dependency status.
7. Select an output directory and one of two output formats.
8. Save John output as `<input-file>.hash`, or verified Hashcat-friendly output as `<input-file>.hashcat.hash`.

John output format preserves converter stdout verbatim. Hashcat output removes the leading John filename label only for verified Hashcat-compatible signatures such as `$7z$`, `$pkzip$`, `$rar5$`, `$keepass$`, `$office$`, and `$pdf$`. Unsupported output is rejected with a clear error instead of being written as a misleading `.hashcat.hash` file.

The output directory and selected format are stored in `%LOCALAPPDATA%\JohnHashExtractor\settings.json`.

## Smoke test

The small console project scans a real John installation and exercises the extraction service:

```powershell
dotnet run --project tests/JohnHashExtractor.Smoke/JohnHashExtractor.Smoke.csproj -- john-1.9.0-jumbo-1-win64/run
```

To exercise manual custom converters and raw output handling:

```powershell
dotnet run --project tests/JohnHashExtractor.Smoke/JohnHashExtractor.Smoke.csproj -- john-1.9.0-jumbo-1-win64/run --custom <input-file>
```

## Scope notes

Custom converters are global manual candidates only. They are never selected automatically, are invoked as `converter <input-file>`, and their stdout is saved verbatim without format conversion. Unsupported invocation or output behavior is reported as unsupported.
