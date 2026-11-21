using System.Reflection.PortableExecutable;
using System.Text;

namespace Flavirus.Core.Engine;

public sealed class PEAnalyzer
{
    private const int MaxImportDescriptors = 4096;
    private const int MaxImportedFunctions = 100000;
    private const int MaxImportedNameLength = 512;

    private static readonly HashSet<string> SuspiciousApiNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CreateRemoteThread", "VirtualAllocEx", "WriteProcessMemory",
            "SetWindowsHookEx", "GetAsyncKeyState", "NtUnmapViewOfSection",
            "RtlMoveMemory", "CreateProcessA", "WinExec", "ShellExecuteA"
        };

    public PEInfo Analyze(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var info = new PEInfo { FilePath = filePath };

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            info.FileSize = stream.Length;
            using var peReader = new PEReader(stream);
            var headers = peReader.PEHeaders;
            var peHeader = headers.PEHeader;
            if (peHeader is null)
                return info;

            info.IsPortableExecutable = true;
            info.Machine = headers.CoffHeader.Machine.ToString();
            info.IsDLL = headers.CoffHeader.Characteristics.HasFlag(
                Characteristics.Dll);
            ReadImports(peReader, peHeader, info);
            info.IsSuspicious = info.SuspiciousAPIs.Count > 0;
        }
        catch (BadImageFormatException ex)
        {
            info.AnalysisError = ex.Message;
        }
        catch (IOException ex)
        {
            info.AnalysisError = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            info.AnalysisError = ex.Message;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            info.AnalysisError = ex.Message;
        }
        catch (OverflowException ex)
        {
            info.AnalysisError = ex.Message;
        }

        return info;
    }

    private static void ReadImports(
        PEReader peReader,
        PEHeader peHeader,
        PEInfo info)
    {
        var importDirectory = peHeader.ImportTableDirectory;
        if (importDirectory.RelativeVirtualAddress == 0 ||
            importDirectory.Size < sizeof(uint) * 5)
        {
            return;
        }

        var isPe32Plus = peHeader.Magic == PEMagic.PE32Plus;
        var importReader = peReader
            .GetSectionData(importDirectory.RelativeVirtualAddress)
            .GetReader();
        var descriptorCount = Math.Min(
            importDirectory.Size / (sizeof(uint) * 5),
            MaxImportDescriptors);
        var remainingFunctionLimit = MaxImportedFunctions;

        for (var index = 0;
             index < descriptorCount && importReader.RemainingBytes > 0;
             index++)
        {
            if (importReader.RemainingBytes < sizeof(uint) * 5)
                break;

            var lookupTableRva = importReader.ReadUInt32();
            importReader.ReadUInt32();
            importReader.ReadUInt32();
            var nameRva = importReader.ReadUInt32();
            var addressTableRva = importReader.ReadUInt32();
            if (lookupTableRva == 0 && nameRva == 0 && addressTableRva == 0)
                break;

            if (nameRva != 0)
            {
                var libraryName = ReadAsciiString(peReader, checked((int)nameRva));
                if (libraryName.Length > 0)
                    info.ImportedLibraries.Add(libraryName);
            }

            var thunkRva = lookupTableRva != 0 ? lookupTableRva : addressTableRva;
            if (thunkRva != 0)
                ReadImportedFunctions(
                    peReader,
                    checked((int)thunkRva),
                    isPe32Plus,
                    info,
                    ref remainingFunctionLimit);
            if (remainingFunctionLimit == 0)
                break;
        }
    }

    private static void ReadImportedFunctions(
        PEReader peReader,
        int thunkRva,
        bool isPe32Plus,
        PEInfo info,
        ref int remainingFunctionLimit)
    {
        var thunkReader = peReader.GetSectionData(thunkRva).GetReader();
        var ordinalMask = isPe32Plus
            ? 0x8000000000000000UL
            : 0x80000000UL;

        for (var count = 0;
             count < remainingFunctionLimit && thunkReader.RemainingBytes > 0;
             count++)
        {
            remainingFunctionLimit--;
            var thunk = isPe32Plus
                ? thunkReader.ReadUInt64()
                : thunkReader.ReadUInt32();
            if (thunk == 0)
                break;
            if ((thunk & ordinalMask) != 0)
                continue;

            var nameRva = thunk & ~ordinalMask;
            if (nameRva > int.MaxValue)
                continue;

            var nameReader = peReader.GetSectionData((int)nameRva).GetReader();
            if (nameReader.RemainingBytes < sizeof(ushort))
                continue;
            nameReader.ReadUInt16();
            var functionName = ReadAsciiString(nameReader);
            if (SuspiciousApiNames.Contains(functionName) &&
                !info.SuspiciousAPIs.Contains(
                    functionName, StringComparer.OrdinalIgnoreCase))
            {
                info.SuspiciousAPIs.Add(functionName);
            }
        }
    }

    private static string ReadAsciiString(PEReader peReader, int relativeVirtualAddress) =>
        ReadAsciiString(peReader.GetSectionData(relativeVirtualAddress).GetReader());

    private static string ReadAsciiString(
        System.Reflection.Metadata.BlobReader reader)
    {
        var bytes = new List<byte>();
        while (reader.RemainingBytes > 0 && bytes.Count < MaxImportedNameLength)
        {
            var value = reader.ReadByte();
            if (value == 0)
                break;
            bytes.Add(value);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}

public sealed class PEInfo
{
    public string FilePath { get; set; } = "";
    public bool IsPortableExecutable { get; set; }
    public bool IsSuspicious { get; set; }
    public string Machine { get; set; } = "";
    public bool IsDLL { get; set; }
    public long FileSize { get; set; }
    public List<string> ImportedLibraries { get; } = new();
    public List<string> SuspiciousAPIs { get; } = new();
    public string? AnalysisError { get; set; }
}
