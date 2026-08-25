using System.Text;

namespace Proton.Personalization;

/// <summary>
/// Lecture et rebasage du manifeste d'un bundle .NET single-file.
///
/// Disposition du fichier publié :
///   [ singlefilehost (PE) ][ padding ][ fichiers embarqués ][ manifeste ]
///
/// Tous les décalages stockés dans le manifeste sont ABSOLUS depuis le début
/// du fichier. Déplacer le bundle impose donc de tous les réécrire.
/// </summary>
public sealed class BundleManifest
{
    public required uint MajorVersion { get; init; }
    public required uint MinorVersion { get; init; }
    public required string BundleId { get; init; }
    public required List<Entry> Entries { get; init; }
    /// Positions, dans le fichier, des champs int64 contenant un décalage absolu.
    public required List<long> OffsetFieldPositions { get; init; }
    public required long HeaderOffset { get; init; }

    public sealed record Entry(string RelativePath, byte Type, long Offset, long Size, long CompressedSize);

    public static string TypeName(byte t) => t switch
    {
        0 => "Unknown", 1 => "Assembly", 2 => "NativeBinary",
        3 => "DepsJson", 4 => "RuntimeConfigJson", 5 => "Symbols",
        _ => $"({t})"
    };

    public static BundleManifest Read(byte[] file, long headerOffset)
    {
        var r = new Cursor(file, headerOffset);
        var offsetFields = new List<long>();

        uint major = r.UInt32();
        uint minor = r.UInt32();
        int count = r.Int32();

        if (major is 0 or > 100 || count < 0 || count > 100_000)
            throw new InvalidDataException($"Inconsistent bundle manifest (version {major}.{minor}, {count} entries).");

        string bundleId = r.String7Bit();

        if (major >= 2)
        {
            offsetFields.Add(r.Position); r.Int64();   // depsJson offset
            r.Int64();                                  // depsJson size
            offsetFields.Add(r.Position); r.Int64();   // runtimeConfigJson offset
            r.Int64();                                  // runtimeConfigJson size
            r.UInt64();                                 // flags
        }

        var entries = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            offsetFields.Add(r.Position);
            long offset = r.Int64();
            long size = r.Int64();
            long compressed = major >= 6 ? r.Int64() : 0;
            byte type = r.Byte();
            string path = r.String7Bit();
            entries.Add(new Entry(path, type, offset, size, compressed));
        }

        return new BundleManifest
        {
            MajorVersion = major, MinorVersion = minor, BundleId = bundleId,
            Entries = entries, OffsetFieldPositions = offsetFields, HeaderOffset = headerOffset
        };
    }

    /// <summary>Ajoute <paramref name="delta"/> à tous les décalages absolus du manifeste.</summary>
    public static void Rebase(byte[] file, BundleManifest manifest, long delta)
    {
        foreach (long pos in manifest.OffsetFieldPositions)
        {
            long current = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan((int)pos));
            // Un décalage nul signifie « absent » (deps.json / runtimeconfig.json optionnels).
            if (current == 0) continue;
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan((int)pos), current + delta);
        }
    }

    /// <summary>Début réel des données du bundle : le plus petit décalage de fichier embarqué.</summary>
    public long DataStart => Entries.Count == 0 ? HeaderOffset : Entries.Min(e => e.Offset);

    public void Dump(TextWriter w, int maxEntries = 8)
    {
        w.WriteLine($"  Version manifeste     : {MajorVersion}.{MinorVersion}");
        w.WriteLine($"  Bundle ID             : {BundleId}");
        w.WriteLine($"  Fichiers embarqués    : {Entries.Count}");
        w.WriteLine($"  Début des données     : {DataStart:N0}");
        w.WriteLine($"  Champs de décalage    : {OffsetFieldPositions.Count}");
        foreach (var e in Entries.OrderBy(e => e.Offset).Take(maxEntries))
            w.WriteLine($"      @{e.Offset,12:N0}  {e.Size,11:N0}  {TypeName(e.Type),-13} {e.RelativePath}");
        if (Entries.Count > maxEntries)
            w.WriteLine($"      … et {Entries.Count - maxEntries} autres");

        // Contrôle d'alignement : seuls les assemblies STOCKÉS TELS QUELS sont
        // mappés directement en mémoire et doivent rester alignés sur 4 096.
        // Dans un bundle compressé, ils sont décompressés en mémoire : l'alignement
        // n'a alors plus aucune signification.
        var mappable = Entries.Where(e => e.Type == 1 && e.CompressedSize == 0).ToList();
        var misaligned = mappable.Where(e => e.Offset % 4096 != 0).ToList();
        w.WriteLine($"  Assemblies mappables  : {mappable.Count} / {Entries.Count(e => e.Type == 1)} (le reste est compressé)");
        w.WriteLine($"  Alignement 4 K        : {(misaligned.Count == 0 ? "respecté" : $"{misaligned.Count} DÉSALIGNÉS")}");
    }

    private sealed class Cursor(byte[] buf, long start)
    {
        private int _p = (int)start;
        public long Position => _p;

        public byte Byte() => buf[_p++];
        public uint UInt32() { var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(_p)); _p += 4; return v; }
        public int Int32() { var v = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(_p)); _p += 4; return v; }
        public long Int64() { var v = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(_p)); _p += 8; return v; }
        public ulong UInt64() { var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(_p)); _p += 8; return v; }

        /// Chaîne au format BinaryWriter : longueur encodée sur 7 bits, puis UTF-8.
        public string String7Bit()
        {
            int len = 0, shift = 0;
            while (true)
            {
                byte b = Byte();
                len |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) throw new InvalidDataException("Invalid string length in the manifest.");
            }
            string s = Encoding.UTF8.GetString(buf, _p, len);
            _p += len;
            return s;
        }
    }
}
