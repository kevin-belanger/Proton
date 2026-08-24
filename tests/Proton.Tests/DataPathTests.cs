using Proton.FileApi;

namespace Proton.Tests;

/// <summary>
/// Confinement des chemins (§14, CA-10).
///
/// C'est la seule barrière entre une application Web et le reste du disque. Ces
/// tests couvrent les formes connues d'évasion plutôt que le seul cas nominal.
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
    public void Rejette_un_chemin_traversant_un_lien_qui_sort()
    {
        string dehors = Path.Combine(_root, "dehors");
        Directory.CreateDirectory(dehors);
        File.WriteAllText(Path.Combine(dehors, "secret.txt"), "confidentiel");

        string lien = Path.Combine(_paths.Root, "evasion");
        try
        {
            Directory.CreateSymbolicLink(lien, dehors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La création de liens exige des droits particuliers selon la
            // configuration de Windows ; le cas ne peut alors pas être exercé.
            return;
        }

        // Le chemin est syntaxiquement irréprochable et reste, sur le papier, à
        // l'intérieur de `data`. Seule la résolution du lien révèle l'évasion.
        Assert.False(_paths.Resolve("/evasion/secret.txt").IsValid);
        Assert.Equal(PathRejection.LinkEscape, _paths.Resolve("/evasion/secret.txt").Rejection);
        Assert.False(_paths.Resolve("/evasion").IsValid);
    }

    [Fact]
    public void Rejette_un_chemin_traversant_une_jonction_qui_sort()
    {
        // Les jonctions se créent sans privilège particulier, contrairement aux liens
        // symboliques : c'est donc le vecteur réaliste, et celui qui doit être couvert.
        string dehors = Path.Combine(_root, "dehors-jonction");
        Directory.CreateDirectory(dehors);
        File.WriteAllText(Path.Combine(dehors, "confidentiel.txt"), "secret");

        string lien = Path.Combine(_paths.Root, "jonction");
        Assert.True(TryCreateJunction(lien, dehors), "La jonction n'a pas pu être créée.");

        Assert.True(File.Exists(Path.Combine(lien, "confidentiel.txt")),
            "La jonction devrait donner accès au fichier : sans cela le test ne prouve rien.");

        DataPathResult resultat = _paths.Resolve("/jonction/confidentiel.txt");

        Assert.False(resultat.IsValid);
        Assert.Equal(PathRejection.LinkEscape, resultat.Rejection);
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

    [Fact]
    public void Accepte_un_lien_qui_reste_a_l_interieur()
    {
        string cible = Path.Combine(_paths.Root, "reel");
        Directory.CreateDirectory(cible);

        string lien = Path.Combine(_paths.Root, "raccourci");
        try
        {
            Directory.CreateSymbolicLink(lien, cible);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        // Un lien interne ne fait sortir de nulle part : rien ne justifie de le
        // refuser.
        Assert.True(_paths.Resolve("/raccourci").IsValid);
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
