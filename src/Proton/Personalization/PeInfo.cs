using System.Buffers.Binary;

namespace Proton.Personalization;

/// <summary>
/// Lecture bas niveau d'un exécutable PE Windows, avec détection du bundle
/// .NET single-file annexé après la fin des sections.
/// </summary>
public sealed class PeInfo
{
    /// SHA-256 de ".net core bundle" — placeholder écrit dans l'apphost par HostWriter.
    /// Les 8 octets qui PRÉCÈDENT cette signature contiennent le bundle header offset (int64).
    public static readonly byte[] BundleSignature =
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

    public required long FileLength { get; init; }
    /// Fin du contenu PE proprement dit : max(PointerToRawData + SizeOfRawData).
    public required long PeEnd { get; init; }
    /// Position de la signature du bundle dans le fichier, ou -1.
    public required long BundleSignatureOffset { get; init; }
    /// Valeur actuellement stockée du bundle header offset, ou -1.
    public required long BundleHeaderOffset { get; init; }
    public required List<(string Name, long RawPtr, long RawSize, uint VirtAddr, uint VirtSize)> Sections { get; init; }

    public bool IsSingleFileBundle => BundleSignatureOffset >= 0 && BundleHeaderOffset > 0;
    /// Octets présents après la fin des sections PE (données du bundle + header).
    public long TrailingBytes => FileLength - PeEnd;

    public static PeInfo Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Read(bytes, bytes.LongLength);
    }

    public static PeInfo Read(byte[] b, long fileLength)
    {
        if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z')
            throw new InvalidDataException("Signature DOS 'MZ' absente.");

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x3C));
        if (b[peOffset] != 'P' || b[peOffset + 1] != 'E')
            throw new InvalidDataException("Signature 'PE\0\0' absente.");

        int coff = peOffset + 4;
        int numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(coff + 2));
        int sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(coff + 16));
        int sectionTable = coff + 20 + sizeOfOptionalHeader;

        var sections = new List<(string, long, long, uint, uint)>();
        long peEnd = 0;
        for (int i = 0; i < numberOfSections; i++)
        {
            int s = sectionTable + i * 40;
            string name = System.Text.Encoding.ASCII.GetString(b, s, 8).TrimEnd('\0');
            uint virtSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 8));
            uint virtAddr = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 12));
            uint rawSize  = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 16));
            uint rawPtr   = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 20));
            sections.Add((name, rawPtr, rawSize, virtAddr, virtSize));
            if (rawSize > 0 && rawPtr + rawSize > peEnd)
                peEnd = rawPtr + rawSize;
        }

        long sigOffset = IndexOf(b, BundleSignature);
        long headerOffset = -1;
        if (sigOffset >= 8)
            headerOffset = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan((int)sigOffset - 8));

        return new PeInfo
        {
            FileLength = fileLength,
            PeEnd = peEnd,
            BundleSignatureOffset = sigOffset,
            BundleHeaderOffset = headerOffset,
            Sections = sections
        };
    }

    public static long IndexOf(byte[] haystack, byte[] needle)
    {
        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            if (haystack[i] != needle[0]) continue;
            int j = 1;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    public void Dump(TextWriter w)
    {
        w.WriteLine($"  Taille fichier        : {FileLength:N0} octets");
        w.WriteLine($"  Sections ({Sections.Count}) :");
        foreach (var s in Sections)
            w.WriteLine($"      {s.Name,-10} raw @ {s.RawPtr,12:N0}  taille {s.RawSize,12:N0}  → fin {s.RawPtr + s.RawSize,12:N0}");
        w.WriteLine($"  Fin des sections PE   : {PeEnd:N0}");
        w.WriteLine($"  Octets après le PE    : {TrailingBytes:N0}");
        w.WriteLine($"  Signature bundle @    : {(BundleSignatureOffset < 0 ? "(absente)" : BundleSignatureOffset.ToString("N0"))}");
        w.WriteLine($"  Bundle header offset  : {(BundleHeaderOffset < 0 ? "(n/a)" : BundleHeaderOffset.ToString("N0"))}");
        w.WriteLine($"  Bundle valide ?       : {(IsSingleFileBundle && BundleHeaderOffset < FileLength ? "OUI" : "NON")}");
    }
}
