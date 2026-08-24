using System.Buffers.Binary;
using System.Text;
using Proton.Personalization;

namespace Proton.Tests;

/// <summary>
/// Construction de la ressource RT_VERSION (§42).
///
/// La structure est faite de blocs imbriqués qui portent chacun leur longueur, et
/// dont l'alignement est strict : une erreur d'un octet rend l'ensemble illisible
/// par Windows, sans erreur visible ailleurs.
/// </summary>
public sealed class VersionInfoTests
{
    private static VersionInfo.Fields Sample => new()
    {
        ProductName = "Gestion Inventaire",
        FileDescription = "Gestion Inventaire — Édition 2026",
        InternalName = "GestionInventaire",
        OriginalFilename = "GestionInventaire.exe",
        CompanyName = "Atelier Kevin",
        Version = new Version(2, 4, 1, 0)
    };

    [Fact]
    public void La_longueur_declaree_couvre_toute_la_ressource()
    {
        byte[] resource = VersionInfo.Build(Sample);

        ushort declared = BinaryPrimitives.ReadUInt16LittleEndian(resource);

        // Le bloc racine annonce sa taille totale ; le tampon peut porter quelques
        // octets d'alignement au-delà, jamais moins.
        Assert.True(declared <= resource.Length,
            $"Longueur annoncée {declared}, tampon {resource.Length}.");
        Assert.True(resource.Length - declared < 4);
    }

    [Fact]
    public void Commence_par_la_cle_attendue()
    {
        byte[] resource = VersionInfo.Build(Sample);

        // wLength, wValueLength, wType, puis la clé en UTF-16.
        string key = Encoding.Unicode.GetString(resource, 6, "VS_VERSION_INFO".Length * 2);

        Assert.Equal("VS_VERSION_INFO", key);
    }

    [Fact]
    public void Contient_la_signature_binaire_de_VS_FIXEDFILEINFO()
    {
        byte[] resource = VersionInfo.Build(Sample);

        // 0xFEEF04BD est la seule ancre fiable : c'est ce que Windows recherche.
        int index = IndexOfUInt32(resource, 0xFEEF04BD);
        Assert.True(index > 0, "Signature VS_FIXEDFILEINFO absente.");

        // La version de fichier suit immédiatement la version de structure.
        uint most = BinaryPrimitives.ReadUInt32LittleEndian(resource.AsSpan(index + 8));
        Assert.Equal(2u << 16 | 4u, most);
    }

    [Theory]
    [InlineData("ProductName")]
    [InlineData("FileDescription")]
    [InlineData("InternalName")]
    [InlineData("OriginalFilename")]
    [InlineData("FileVersion")]
    [InlineData("CompanyName")]
    public void Contient_chaque_champ_attendu(string champ)
    {
        string texte = Encoding.Unicode.GetString(VersionInfo.Build(Sample));

        Assert.Contains(champ, texte, StringComparison.Ordinal);
    }

    [Fact]
    public void Conserve_les_accents()
    {
        string texte = Encoding.Unicode.GetString(VersionInfo.Build(Sample));

        // Les chaînes sont en UTF-16 : rien ne justifie de restreindre les métadonnées
        // à l'ASCII.
        Assert.Contains("Édition 2026", texte, StringComparison.Ordinal);
    }

    [Fact]
    public void Omet_les_champs_facultatifs_absents()
    {
        var sans = Sample with { CompanyName = null };

        string texte = Encoding.Unicode.GetString(VersionInfo.Build(sans));

        Assert.DoesNotContain("CompanyName", texte, StringComparison.Ordinal);
    }

    [Fact]
    public void Chaque_bloc_est_aligne_sur_quatre_octets()
    {
        // L'alignement conditionne la lisibilité de toute la structure.
        Assert.Equal(0, VersionInfo.Build(Sample).Length % 4);
    }

    private static int IndexOfUInt32(byte[] buffer, uint value)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(needle, value);

        for (int i = 0; i + 4 <= buffer.Length; i++)
            if (buffer.AsSpan(i, 4).SequenceEqual(needle))
                return i;

        return -1;
    }
}
