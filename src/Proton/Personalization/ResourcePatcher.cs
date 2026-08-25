using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Proton.Personalization;

/// <summary>
/// Mise à jour des ressources Windows d'un exécutable : icône (§41) et métadonnées
/// (§42).
///
/// <b>Ces API ne doivent jamais être appliquées à un exécutable contenant un bundle
/// single-file.</b> <c>EndUpdateResource</c> reconstruit le fichier à partir de ses
/// seuls en-têtes PE et supprime tout ce qui suit la dernière section : le bundle
/// disparaîtrait en entier. C'est à <see cref="BundlePatcher"/> qu'il revient
/// d'isoler le PE au préalable — voir <c>notes/01-personnalisation-executable.md</c>.
/// </summary>
public static class ResourcePatcher
{
    private const int RT_ICON = 3;
    private const int RT_GROUP_ICON = 14;
    private const int RT_VERSION = 16;

    private const ushort LangNeutral = 0;
    private const ushort FirstIconId = 1;
    private const ushort GroupIconId = 1;
    private const ushort VersionId = 1;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResourceW(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
        ushort wLanguage, byte[]? lpData, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr hUpdate, bool fDiscard);

    /// <summary>
    /// Applique l'icône et les métadonnées en une seule session.
    /// </summary>
    /// <remarks>
    /// Une session unique plutôt que deux : le fichier n'est réécrit qu'une fois, et
    /// il ne peut pas rester dans un état où l'icône serait à jour mais pas les
    /// métadonnées.
    /// </remarks>
    public static void Apply(string exePath, string? icoPath, VersionInfo.Fields? version)
    {
        if (icoPath is null && version is null)
            return;

        IntPtr handle = BeginUpdateResourceW(exePath, bDeleteExistingResources: false);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Opening the resources failed (Win32 {Marshal.GetLastWin32Error()}).");

        try
        {
            if (icoPath is not null)
                WriteIcon(handle, File.ReadAllBytes(icoPath));

            if (version is not null)
                // La ressource existante est en langue neutre : y écrire dans une autre
                // langue en créerait une seconde, et Windows continuerait de lire la
                // première. Il faut remplacer, non ajouter.
                Write(handle, RT_VERSION, VersionId, LangNeutral, VersionInfo.Build(version));
        }
        catch
        {
            EndUpdateResourceW(handle, fDiscard: true);
            throw;
        }

        if (!EndUpdateResourceW(handle, fDiscard: false))
            throw new InvalidOperationException(
                $"Writing the resources failed (Win32 {Marshal.GetLastWin32Error()}).");
    }

    private static void WriteIcon(IntPtr handle, byte[] ico)
    {
        (List<IconEntry> entries, List<byte[]> images) = ParseIco(ico);

        // Une ressource RT_ICON par image, puis un RT_GROUP_ICON qui les référence :
        // c'est ce groupe que Windows consulte pour choisir la taille adaptée.
        for (int i = 0; i < images.Count; i++)
            Write(handle, RT_ICON, (ushort)(FirstIconId + i), LangNeutral, images[i]);

        Write(handle, RT_GROUP_ICON, GroupIconId, LangNeutral, BuildGroupIcon(entries));
    }

    private static void Write(IntPtr handle, int type, ushort id, ushort language, byte[] data)
    {
        if (!UpdateResourceW(handle, type, id, language, data, (uint)data.Length))
            throw new InvalidOperationException(
                $"Updating a resource failed (Win32 {Marshal.GetLastWin32Error()}).");
    }

    public record IconEntry(byte Width, byte Height, byte ColorCount, byte Reserved,
                            ushort Planes, ushort BitCount, uint BytesInRes);

    /// <summary>Découpe un fichier .ico en son répertoire et ses images brutes.</summary>
    public static (List<IconEntry> Entries, List<byte[]> Images) ParseIco(byte[] ico)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1)
            throw new InvalidDataException("Invalid ICO file: expected type 1.");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        if (count == 0)
            throw new InvalidDataException("The ICO file holds no image.");

        var entries = new List<IconEntry>(count);
        var images = new List<byte[]>(count);

        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            uint bytesInRes = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(e + 8));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(e + 12));

            if (offset + bytesInRes > ico.Length)
                throw new InvalidDataException($"Image {i} falls outside the ICO file.");

            entries.Add(new IconEntry(
                ico[e], ico[e + 1], ico[e + 2], ico[e + 3],
                BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(e + 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(e + 6)),
                bytesInRes));

            images.Add(ico[(int)offset..(int)(offset + bytesInRes)]);
        }

        return (entries, images);
    }

    /// <summary>
    /// Construit la ressource RT_GROUP_ICON.
    /// </summary>
    /// <remarks>
    /// Ses entrées font 14 octets et non 16 : le décalage de l'image y est remplacé
    /// par l'identifiant de la ressource RT_ICON correspondante.
    /// </remarks>
    public static byte[] BuildGroupIcon(List<IconEntry> entries)
    {
        byte[] buffer = new byte[6 + entries.Count * 14];

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            int o = 6 + i * 14;
            IconEntry e = entries[i];

            buffer[o] = e.Width;
            buffer[o + 1] = e.Height;
            buffer[o + 2] = e.ColorCount;
            buffer[o + 3] = e.Reserved;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(o + 4), e.Planes);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(o + 6), e.BitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(o + 8), e.BytesInRes);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(o + 12), (ushort)(FirstIconId + i));
        }

        return buffer;
    }
}
