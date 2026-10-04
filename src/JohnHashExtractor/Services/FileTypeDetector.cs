using System.Text;

namespace JohnHashExtractor.Services;

public sealed record DetectedFileType(string DisplayName, string Extension, IReadOnlyList<string> ConverterFamilies);

public static class FileTypeDetector
{
    private static readonly IReadOnlyDictionary<string, DetectedFileType> ByExtension =
        new Dictionary<string, DetectedFileType>(StringComparer.OrdinalIgnoreCase)
        {
            [".zip"] = new("ZIP archive", ".zip", new[] { "zip" }),
            [".rar"] = new("RAR archive", ".rar", new[] { "rar" }),
            [".7z"] = new("7-Zip archive", ".7z", new[] { "7z" }),
            [".kdbx"] = new("KeePass database", ".kdbx", new[] { "keepass" }),
            [".pdf"] = new("PDF document", ".pdf", new[] { "pdf" }),
            [".doc"] = new("Microsoft Office document", ".doc", new[] { "office" }),
            [".docx"] = new("Microsoft Office document", ".docx", new[] { "office" }),
            [".xls"] = new("Microsoft Office document", ".xls", new[] { "office" }),
            [".xlsx"] = new("Microsoft Office document", ".xlsx", new[] { "office" }),
            [".ppt"] = new("Microsoft Office document", ".ppt", new[] { "office" }),
            [".pptx"] = new("Microsoft Office document", ".pptx", new[] { "office" }),
            [".odt"] = new("OpenDocument document", ".odt", new[] { "office" }),
            [".ods"] = new("OpenDocument spreadsheet", ".ods", new[] { "office" }),
            [".odp"] = new("OpenDocument presentation", ".odp", new[] { "office" }),
            [".gpg"] = new("PGP/GPG file", ".gpg", new[] { "gpg" }),
            [".pgp"] = new("PGP/GPG file", ".pgp", new[] { "gpg" }),
            [".asc"] = new("PGP/GPG file", ".asc", new[] { "gpg" }),
            [".dmg"] = new("Apple DMG image", ".dmg", new[] { "dmg" }),
            [".keychain"] = new("Apple keychain", ".keychain", new[] { "keychain" }),
            [".keyring"] = new("GNOME keyring", ".keyring", new[] { "keyring" }),
            [".kwl"] = new("KWallet file", ".kwl", new[] { "kwallet" }),
            [".psafe3"] = new("Password Safe database", ".psafe3", new[] { "pwsafe" }),
            [".pfx"] = new("PFX/PKCS#12 file", ".pfx", new[] { "pfx" }),
            [".pem"] = new("PEM file", ".pem", new[] { "pem" }),
            [".pcap"] = new("Packet capture", ".pcap", new[] { "pcap" }),
            [".pcapng"] = new("Packet capture", ".pcapng", new[] { "pcap" }),
            [".hccapx"] = new("WPA capture", ".hccapx", new[] { "hccapx" })
        };

    public static DetectedFileType Detect(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (ByExtension.TryGetValue(extension, out var known))
        {
            return known;
        }

        var signature = DetectSignature(filePath);
        return signature ?? new DetectedFileType(
            "Unknown file type",
            extension,
            Array.Empty<string>());
    }

    public static IReadOnlyList<string> GetConverterFamilies(DetectedFileType type) => type.ConverterFamilies;

    private static DetectedFileType? DetectSignature(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[8];
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (read < 4)
            {
                return null;
            }

            if (header[0] == 'P' && header[1] == 'K' && (header[2] == 3 || header[2] == 5 || header[2] == 7))
            {
                return new DetectedFileType("ZIP archive", ".zip", new[] { "zip" });
            }

            if (header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F')
            {
                return new DetectedFileType("PDF document", ".pdf", new[] { "pdf" });
            }

            if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC && header[3] == 0xAF)
            {
                return new DetectedFileType("7-Zip archive", ".7z", new[] { "7z" });
            }

            if (read >= 7 && header[0] == 'R' && header[1] == 'a' && header[2] == 'r' && header[3] == '!')
            {
                return new DetectedFileType("RAR archive", ".rar", new[] { "rar" });
            }
        }
        catch (IOException)
        {
        }

        return null;
    }
}
