using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ProtoPE;

/// <summary>
/// Stratégie « naïve » : mise à jour des ressources PE par les API Win32
/// BeginUpdateResource / UpdateResource / EndUpdateResource.
/// </summary>
public static partial class IconPatcher
{
    private const int RT_ICON = 3;
    private const int RT_GROUP_ICON = 14;
    private const ushort LANG_NEUTRAL = 0;
    private const ushort FirstIconId = 1;
    private const ushort GroupIconId = 1;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResourceW(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
        ushort wLanguage, byte[]? lpData, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr hUpdate, bool fDiscard);

    /// <summary>Remplace l'icône principale du fichier indiqué, en place.</summary>
    public static void ApplyWin32(string exePath, string icoPath)
    {
        var (entries, images) = ParseIco(File.ReadAllBytes(icoPath));

        IntPtr h = BeginUpdateResourceW(exePath, false);
        if (h == IntPtr.Zero)
            throw new InvalidOperationException($"BeginUpdateResource a échoué (Win32 {Marshal.GetLastWin32Error()}).");

        bool ok = true;
        try
        {
            // Une ressource RT_ICON par image.
            for (int i = 0; i < images.Count; i++)
                ok &= UpdateResourceW(h, RT_ICON, (ushort)(FirstIconId + i), LANG_NEUTRAL,
                                      images[i], (uint)images[i].Length);

            // Une ressource RT_GROUP_ICON qui les référence.
            byte[] group = BuildGroupIcon(entries);
            ok &= UpdateResourceW(h, RT_GROUP_ICON, GroupIconId, LANG_NEUTRAL, group, (uint)group.Length);

            if (!ok)
                throw new InvalidOperationException($"UpdateResource a échoué (Win32 {Marshal.GetLastWin32Error()}).");
        }
        catch
        {
            EndUpdateResourceW(h, fDiscard: true);
            throw;
        }

        if (!EndUpdateResourceW(h, fDiscard: false))
            throw new InvalidOperationException($"EndUpdateResource a échoué (Win32 {Marshal.GetLastWin32Error()}).");
    }

    public record IconEntry(byte Width, byte Height, byte ColorCount, byte Reserved,
                            ushort Planes, ushort BitCount, uint BytesInRes);

    /// <summary>Découpe un fichier .ico en son répertoire et ses images brutes.</summary>
    public static (List<IconEntry> Entries, List<byte[]> Images) ParseIco(byte[] ico)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1)
            throw new InvalidDataException("Fichier ICO invalide (type attendu : 1).");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        if (count == 0) throw new InvalidDataException("Fichier ICO sans image.");

        var entries = new List<IconEntry>(count);
        var images = new List<byte[]>(count);

        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            uint bytesInRes = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(e + 8));
            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(e + 12));
            if (imageOffset + bytesInRes > ico.Length)
                throw new InvalidDataException($"Image {i} hors limites du fichier ICO.");

            entries.Add(new IconEntry(
                ico[e], ico[e + 1], ico[e + 2], ico[e + 3],
                BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(e + 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(e + 6)),
                bytesInRes));

            images.Add(ico[(int)imageOffset..(int)(imageOffset + bytesInRes)]);
        }
        return (entries, images);
    }

    /// <summary>Construit la ressource RT_GROUP_ICON (entrées de 14 octets, id au lieu d'offset).</summary>
    public static byte[] BuildGroupIcon(List<IconEntry> entries)
    {
        byte[] buf = new byte[6 + entries.Count * 14];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), 0);                     // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), 1);                     // type = icône
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4), (ushort)entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            int o = 6 + i * 14;
            var e = entries[i];
            buf[o] = e.Width; buf[o + 1] = e.Height; buf[o + 2] = e.ColorCount; buf[o + 3] = e.Reserved;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o + 4), e.Planes);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o + 6), e.BitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o + 8), e.BytesInRes);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o + 12), (ushort)(FirstIconId + i));
        }
        return buf;
    }
}
