using System.Buffers.Binary;

namespace Proton.Personalization;

/// <summary>
/// Personnalisation d'un exécutable .NET single-file sans détruire son bundle.
///
/// Les API Win32 de mise à jour de ressources réécrivent le fichier à partir de
/// ses seuls en-têtes PE et suppriment tout ce qui suit la dernière section :
/// appliquées directement, elles amputent le bundle. La parade consiste à
/// isoler le PE, le patcher seul, puis recoller le bundle en réécrivant les
/// décalages qu'il contient.
/// </summary>
public static class BundlePatcher
{
    /// Les assemblies embarqués sont mappés directement en mémoire : leur
    /// décalage doit rester aligné sur la taille de page.
    private const int PageAlignment = 4096;

    public sealed record Report(
        long OldPeEnd, long NewPeEnd, long RawDelta, long Delta, long Padding,
        long OldHeaderOffset, long NewHeaderOffset, int RebasedFields, int EmbeddedFiles,
        bool AlignmentPreserved);

    public static byte[] Personalize(
        byte[] source, string? icoPath, VersionInfo.Fields? version, string scratchDir, out Report report)
    {
        byte[] stripped = EmbeddedPackage.Strip(source);
        var info = PeInfo.Read(stripped, stripped.LongLength);

        if (!info.IsSingleFileBundle)
            throw new InvalidOperationException("L'exécutable source ne contient pas de bundle single-file.");

        long oldPeEnd = info.PeEnd;
        byte[] peOnly = stripped[..(int)oldPeEnd];
        byte[] tail = stripped[(int)oldPeEnd..];

        // --- Patch des ressources sur le PE isolé ---
        string peTemp = Path.Combine(scratchDir, $"pe-{Environment.ProcessId}.tmp");
        byte[] newPe;
        try
        {
            File.WriteAllBytes(peTemp, peOnly);
            ResourcePatcher.Apply(peTemp, icoPath, version);
            newPe = File.ReadAllBytes(peTemp);
        }
        finally
        {
            if (File.Exists(peTemp)) { try { File.Delete(peTemp); } catch { } }
        }

        var newInfo = PeInfo.Read(newPe, newPe.LongLength);
        if (newInfo.PeEnd != newPe.Length)
            throw new InvalidOperationException(
                $"Le PE patché contient {newPe.Length - newInfo.PeEnd} octets inattendus après ses sections.");

        // --- Choix du décalage : multiple de la page, pour préserver l'alignement ---
        long rawDelta = newPe.Length - oldPeEnd;
        long delta = AlignUp(rawDelta, PageAlignment);
        long padding = delta - rawDelta;

        byte[] result = new byte[newPe.Length + padding + tail.Length];
        newPe.CopyTo(result, 0);
        // La zone de padding reste à zéro.
        tail.CopyTo(result, (int)(newPe.Length + padding));

        // --- Réécriture du pointeur global vers le manifeste ---
        long sigOffset = PeInfo.IndexOf(result, PeInfo.BundleSignature);
        if (sigOffset < 8)
            throw new InvalidOperationException("Signature de bundle introuvable dans le PE patché.");

        long newHeaderOffset = info.BundleHeaderOffset + delta;
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan((int)sigOffset - 8), newHeaderOffset);

        // --- Réécriture des décalages internes du manifeste ---
        var manifest = BundleManifest.Read(result, newHeaderOffset);
        BundleManifest.Rebase(result, manifest, delta);

        var rebased = BundleManifest.Read(result, newHeaderOffset);
        bool aligned = rebased.Entries.All(e => e.Type != 1 || e.CompressedSize != 0 || e.Offset % PageAlignment == 0);

        report = new Report(
            oldPeEnd, newPe.Length, rawDelta, delta, padding,
            info.BundleHeaderOffset, newHeaderOffset,
            manifest.OffsetFieldPositions.Count, manifest.Entries.Count, aligned);

        return result;
    }

    /// Arrondi vers +∞ au multiple d'alignement, y compris pour les valeurs négatives.
    private static long AlignUp(long value, long alignment)
    {
        long r = value % alignment;
        if (r == 0) return value;
        return value > 0 ? value + (alignment - r) : value - r;
    }
}
