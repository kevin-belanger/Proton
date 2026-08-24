using Proton.WebView;

namespace Proton.Tests;

/// <summary>
/// Nommage des ressources téléchargées (§51.2).
///
/// Le nom provient d'une URL : il doit être ramené à un nom de fichier valide, et ne
/// jamais pouvoir désigner un autre endroit du disque.
/// </summary>
public sealed class ResourceOpenerTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    public ResourceOpenerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("rapport.pdf", "rapport.pdf")]
    [InlineData("note accentuée.txt", "note accentuée.txt")]
    [InlineData("a/b/evasion.txt", "a_b_evasion.txt")]
    [InlineData("..\\..\\ailleurs.exe", ".._.._ailleurs.exe")]
    [InlineData("fichier:flux.txt", "fichier_flux.txt")]
    [InlineData("", "proton-download")]
    [InlineData(null, "proton-download")]
    [InlineData("   ", "proton-download")]
    // Windows retire les points et espaces finaux : un nom qui s'y réduirait ne
    // désignerait plus rien (§17.2).
    [InlineData("...", "proton-download")]
    [InlineData("rapport.", "rapport")]
    public void Ramene_le_nom_a_un_nom_de_fichier_valide(string? entree, string attendu) =>
        Assert.Equal(attendu, ResourceOpener.SanitiseFileName(entree));

    [Fact]
    public void Le_nom_assaini_ne_contient_aucun_separateur()
    {
        string nom = ResourceOpener.SanitiseFileName("../../Windows/System32/config");

        Assert.DoesNotContain(Path.DirectorySeparatorChar, nom);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, nom);
    }

    [Fact]
    public void N_ecrase_jamais_un_telechargement_precedent()
    {
        // L'utilisateur a peut-être encore ouvert le fichier de tout à l'heure.
        File.WriteAllText(Path.Combine(_folder, "rapport.pdf"), "premier");

        string second = ResourceOpener.ChooseDestination(_folder, "rapport.pdf");
        Assert.Equal(Path.Combine(_folder, "rapport (1).pdf"), second);

        File.WriteAllText(second, "deuxième");
        Assert.Equal(Path.Combine(_folder, "rapport (2).pdf"),
            ResourceOpener.ChooseDestination(_folder, "rapport.pdf"));
    }

    [Fact]
    public void Utilise_le_nom_demande_lorsqu_il_est_libre()
    {
        Assert.Equal(Path.Combine(_folder, "neuf.txt"),
            ResourceOpener.ChooseDestination(_folder, "neuf.txt"));
    }
}
