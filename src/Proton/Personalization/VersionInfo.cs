using System.Buffers.Binary;
using System.Text;

namespace Proton.Personalization;

/// <summary>
/// Construction d'une ressource <c>RT_VERSION</c> (§42).
///
/// Ce sont ces métadonnées que Windows affiche dans l'onglet « Détails » des
/// propriétés d'un fichier. Aucune API ne les compose : la ressource est une
/// structure <c>VS_VERSIONINFO</c> à écrire octet par octet, faite de blocs
/// imbriqués qui portent chacun leur longueur totale — laquelle n'est connue
/// qu'une fois les enfants écrits.
///
/// D'où la méthode employée : réserver deux octets, écrire le contenu, puis revenir
/// inscrire la longueur. Chaque bloc est aligné sur 4 octets, y compris entre la clé
/// et la valeur.
/// </summary>
public static class VersionInfo
{
    /// Anglais (États-Unis), Unicode. Les chaînes étant en UTF-16, ce couple décrit
    /// correctement une ressource quelle que soit la langue de son contenu.
    private const string LanguageCodePage = "040904B0";
    private const uint Translation = 0x04B00409;

    private const uint FixedFileInfoSignature = 0xFEEF04BD;

    /// <summary>Champs à inscrire dans la ressource.</summary>
    public sealed record Fields
    {
        public required string ProductName { get; init; }
        public required string FileDescription { get; init; }
        public required string InternalName { get; init; }
        public required string OriginalFilename { get; init; }
        public string? CompanyName { get; init; }
        public string? LegalCopyright { get; init; }

        /// <summary>Champ libre. Proton y inscrit sa propre attribution (§45.1).</summary>
        public string? Comments { get; init; }
        public Version Version { get; init; } = new(1, 0, 0, 0);
    }

    public static byte[] Build(Fields fields)
    {
        var writer = new BlockWriter();

        writer.BeginBlock("VS_VERSION_INFO", valueLength: 52, isText: false);
        WriteFixedFileInfo(writer, fields.Version);
        WriteStringFileInfo(writer, fields);
        WriteVarFileInfo(writer);
        writer.EndBlock();

        return writer.ToArray();
    }

    /// <summary>Partie binaire, la seule que Windows sache interpréter sans convention.</summary>
    private static void WriteFixedFileInfo(BlockWriter writer, Version version)
    {
        uint most = (uint)(version.Major << 16 | (ushort)version.Minor);
        uint least = (uint)(version.Build << 16 | (ushort)Math.Max(version.Revision, 0));

        writer.UInt32(FixedFileInfoSignature);
        writer.UInt32(0x00010000);   // version de la structure
        writer.UInt32(most);         // version de fichier
        writer.UInt32(least);
        writer.UInt32(most);         // version de produit
        writer.UInt32(least);
        writer.UInt32(0x3F);         // masque des indicateurs
        writer.UInt32(0);            // indicateurs
        writer.UInt32(0x00040004);   // VOS_NT_WINDOWS32
        writer.UInt32(0x00000001);   // VFT_APP
        writer.UInt32(0);            // sous-type
        writer.UInt32(0);            // date, partie haute
        writer.UInt32(0);            // date, partie basse
    }

    private static void WriteStringFileInfo(BlockWriter writer, Fields fields)
    {
        writer.BeginBlock("StringFileInfo", valueLength: 0, isText: true);
        writer.BeginBlock(LanguageCodePage, valueLength: 0, isText: true);

        writer.StringValue("ProductName", fields.ProductName);
        writer.StringValue("FileDescription", fields.FileDescription);
        writer.StringValue("InternalName", fields.InternalName);
        writer.StringValue("OriginalFilename", fields.OriginalFilename);
        writer.StringValue("FileVersion", fields.Version.ToString());
        writer.StringValue("ProductVersion", fields.Version.ToString());

        if (!string.IsNullOrWhiteSpace(fields.CompanyName))
            writer.StringValue("CompanyName", fields.CompanyName);

        if (!string.IsNullOrWhiteSpace(fields.LegalCopyright))
            writer.StringValue("LegalCopyright", fields.LegalCopyright);

        if (!string.IsNullOrWhiteSpace(fields.Comments))
            writer.StringValue("Comments", fields.Comments);

        writer.EndBlock();
        writer.EndBlock();
    }

    private static void WriteVarFileInfo(BlockWriter writer)
    {
        writer.BeginBlock("VarFileInfo", valueLength: 0, isText: true);

        writer.BeginBlock("Translation", valueLength: 4, isText: false);
        writer.UInt32(Translation);
        writer.EndBlock();

        writer.EndBlock();
    }

    /// <summary>
    /// Écrit des blocs imbriqués dont la longueur n'est connue qu'après coup.
    /// </summary>
    private sealed class BlockWriter
    {
        private readonly List<byte> _buffer = [];
        private readonly Stack<int> _starts = new();

        public void BeginBlock(string key, ushort valueLength, bool isText)
        {
            Align();
            _starts.Push(_buffer.Count);

            UInt16(0);                              // longueur, complétée par EndBlock
            UInt16(valueLength);
            UInt16((ushort)(isText ? 1 : 0));
            Key(key);
            Align();
        }

        public void EndBlock()
        {
            int start = _starts.Pop();
            ushort length = (ushort)(_buffer.Count - start);

            _buffer[start] = (byte)(length & 0xFF);
            _buffer[start + 1] = (byte)(length >> 8);
        }

        /// <summary>
        /// Écrit une paire nom/valeur textuelle.
        /// </summary>
        /// <remarks>
        /// La longueur de valeur se compte ici en <b>caractères</b>, terminateur
        /// compris, et non en octets — l'une des rares irrégularités du format.
        /// </remarks>
        public void StringValue(string key, string value)
        {
            BeginBlock(key, (ushort)(value.Length + 1), isText: true);
            Key(value);
            EndBlock();
        }

        public void UInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            _buffer.AddRange(bytes);
        }

        private void UInt16(ushort value)
        {
            _buffer.Add((byte)(value & 0xFF));
            _buffer.Add((byte)(value >> 8));
        }

        /// <summary>Chaîne UTF-16 terminée par un caractère nul.</summary>
        private void Key(string text)
        {
            _buffer.AddRange(Encoding.Unicode.GetBytes(text));
            _buffer.Add(0);
            _buffer.Add(0);
        }

        private void Align()
        {
            while (_buffer.Count % 4 != 0)
                _buffer.Add(0);
        }

        public byte[] ToArray()
        {
            Align();
            return [.. _buffer];
        }
    }
}
