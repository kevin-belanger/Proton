using Proton.FileApi;

namespace Proton.Tests;

/// <summary>
/// Confinement des chemins (§14, CA-10).
///
/// Le confinement est une clôture d'API : il empêche une application de sortir de
/// `data` par accident ou par un chemin mal formé. Ces tests couvrent les formes
/// connues plutôt que le seul cas nominal.
/// </summary>
public sealed class DataPathTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    private readonly DataPath _paths;

    public DataPathTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        _paths = new DataPath(Path.Combine(_root, "data"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // --- Chemins acceptés ---------------------------------------------------------

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/notes.txt", "notes.txt")]
    [InlineData("/dossier/notes.txt", "dossier/notes.txt")]
    [InlineData("/a/b/c/d.bin", "a/b/c/d.bin")]
    [InlineData("//doubles///barres//", "doubles/barres")]
    [InlineData("/./ici/./aussi", "ici/aussi")]
    // Les segments vides sont normalisés avant toute combinaison : ceci ne désigne
    // pas un partage réseau mais bien un sous-dossier de `data`.
    [InlineData("//serveur/partage/fichier", "serveur/partage/fichier")]
    [InlineData("/accentué éàü.txt", "accentué éàü.txt")]
    [InlineData("/nom avec espaces.txt", "nom avec espaces.txt")]
    public void Accepte_les_chemins_confines(string demande, string attendu)
    {
        DataPathResult resultat = _paths.Resolve(demande);

        Assert.True(resultat.IsValid, $"« {demande} » rejeté : {resultat.Rejection}");
        Assert.Equal(attendu, resultat.RelativePath);
        Assert.StartsWith(_paths.Root, resultat.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    // --- Remontées ----------------------------------------------------------------

    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/../../Windows/System32/config")]
    [InlineData("/dossier/../../dehors.txt")]
    [InlineData("/a/b/../../../evasion")]
    [InlineData("/..")]
    [InlineData("/dossier/..")]
    public void Rejette_les_remontees(string demande)
    {
        DataPathResult resultat = _paths.Resolve(demande);

        Assert.False(resultat.IsValid);
        Assert.Equal(PathRejection.Traversal, resultat.Rejection);
    }

    // --- Chemins absolus ----------------------------------------------------------

    [Theory]
    [InlineData("/C:/Windows/System32")]
    [InlineData("/C:")]
    [InlineData("/dossier\\..\\..\\dehors")]
    [InlineData("/\\\\serveur\\partage")]
    public void Rejette_les_chemins_absolus_et_les_separateurs_windows(string demande)
    {
        DataPathResult resultat = _paths.Resolve(demande);

        // Un « \ » dans une URL n'a aucun usage légitime, et Windows le traiterait
        // comme un séparateur : ce serait une seconde syntaxe de remontée.
        Assert.False(resultat.IsValid);
        Assert.Equal(PathRejection.Absolute, resultat.Rejection);
    }

    [Theory]
    [InlineData("/fichier\0.txt")]
    [InlineData("/fichier<>.txt")]
    [InlineData("/fichier|tube.txt")]
    [InlineData("/fichier*.txt")]
    [InlineData("/fichier?.txt")]
    public void Rejette_les_caracteres_interdits(string demande)
    {
        DataPathResult resultat = _paths.Resolve(demande);

        Assert.False(resultat.IsValid);
        Assert.Equal(PathRejection.InvalidCharacter, resultat.Rejection);
    }

    // --- Voisinage de la racine ---------------------------------------------------

    [Fact]
    public void Un_dossier_voisin_au_nom_proche_n_est_pas_un_descendant()
    {
        // « data-public » commence par « data » sans être dedans : la comparaison
        // doit porter sur le séparateur, non sur le préfixe textuel.
        Directory.CreateDirectory(Path.Combine(_root, "data-public"));

        DataPathResult resultat = _paths.Resolve("/../data-public/vole.txt");

        Assert.False(resultat.IsValid);
    }

    // --- Liens ---------------------------------------------------------------------

    [Fact]
    public void Un_lien_place_dans_data_est_suivi_deliberement()
    {
        // L'API ne crée aucun lien : pour qu'il en existe un dans `data`, il faut que
        // l'utilisateur ou un autre programme l'y ait placé — et l'un comme l'autre
        // ont déjà accès au disque. Le refuser ne protégerait de rien, mais casserait
        // un usage voulu : rediriger un sous-dossier volumineux vers un autre disque.
        //
        // Le confinement est une clôture d'API, non une barrière contre un adversaire
        // (§14, §34).
        string ailleurs = Path.Combine(_root, "autre-disque");
        Directory.CreateDirectory(ailleurs);
        File.WriteAllText(Path.Combine(ailleurs, "photo.jpg"), "données");

        string lien = Path.Combine(_paths.Root, "photos");
        Assert.True(TryCreateJunction(lien, ailleurs), "La jonction n'a pas pu être créée.");

        DataPathResult resultat = _paths.Resolve("/photos/photo.jpg");

        Assert.True(resultat.IsValid);
        Assert.Equal("photos/photo.jpg", resultat.RelativePath);
    }

    /// <summary>Crée une jonction NTFS. Aucune API managée ne le permet directement.</summary>
    private static bool TryCreateJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(link);
    }

    // --- Nature de la ressource ----------------------------------------------------

    [Theory]
    [InlineData("/notes", false)]
    [InlineData("/notes/", true)]
    [InlineData("/", true)]
    [InlineData("", true)]
    [InlineData("/a/b/", true)]
    [InlineData("/a/b", false)]
    public void La_barre_oblique_finale_designe_un_dossier(string demande, bool dossier)
    {
        // §22.1 : c'est elle qui distingue un fichier d'un dossier.
        Assert.Equal(dossier, _paths.Resolve(demande).IsDirectoryRequest);
    }
}
