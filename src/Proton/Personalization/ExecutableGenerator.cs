using System.Text.Json;
using System.Text.Json.Serialization;
using Proton.Configuration;

namespace Proton.Personalization;

/// <summary>Contenu de <c>config/config.json</c> (§40).</summary>
public sealed record ConfigFile
{
    public string? Name { get; init; }
    public string? ExecutableName { get; init; }
    public string? WindowTitle { get; init; }
    public string? Version { get; init; }
    public string? Company { get; init; }
    public WindowConfiguration? Window { get; init; }
}

/// <summary>Ce que la génération a produit, ou pourquoi elle a échoué.</summary>
public sealed record GenerationResult(bool Success, string Message, string? TargetPath = null);

/// <summary>
/// Génération d'un exécutable personnalisé (§37 à §44).
///
/// L'exécutable en cours ne se modifie jamais lui-même : il produit une copie
/// personnalisée, qui possède le moteur complet et pourra donc à son tour engendrer
/// une autre application (§38, CA-17).
/// </summary>
public static class ExecutableGenerator
{
    /// <param name="embedUserFolders">
    /// Embarquer aussi <c>data</c> et <c>db</c>, pour livrer un contenu initial (§39.1).
    /// </param>
    public static GenerationResult Run(
        string selfPath, string workingDirectory, TextWriter log, bool embedUserFolders = false)
    {
        string configDirectory = Path.Combine(workingDirectory, "config");
        string configPath = Path.Combine(configDirectory, "config.json");
        string iconPath = Path.Combine(configDirectory, "icon.ico");
        string appPath = Path.Combine(workingDirectory, EmbeddedPackage.AppFolder);

        log.WriteLine($"Source    : {selfPath}");
        log.WriteLine($"Config    : {configPath}");

        if (!Directory.Exists(appPath))
            return new GenerationResult(false,
                $"« {appPath} » est introuvable. L'application à embarquer doit exister.");

        if (!File.Exists(configPath))
            return new GenerationResult(false, $"« {configPath} » est introuvable.");

        ConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return new GenerationResult(false, $"config.json est illisible : {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(file?.Name) || string.IsNullOrWhiteSpace(file.ExecutableName))
            return new GenerationResult(false, "« name » et « executableName » sont obligatoires.");

        string targetName = file.ExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? file.ExecutableName
            : file.ExecutableName + ".exe";

        string target = Path.Combine(workingDirectory, targetName);

        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
            return new GenerationResult(false, "La cible est l'exécutable source ; un exécutable ne se modifie jamais lui-même.");

        log.WriteLine($"Cible     : {target}");

        string? icon = File.Exists(iconPath) ? iconPath : null;
        log.WriteLine(icon is null
            ? "Icône     : aucune (config/icon.ico absent)"
            : $"Icône     : {new FileInfo(iconPath).Length:N0} octets");

        var configuration = new AppConfiguration
        {
            Name = file.Name,
            // §40 : le titre reprend le nom lorsqu'il n'est pas précisé.
            WindowTitle = string.IsNullOrWhiteSpace(file.WindowTitle) ? file.Name : file.WindowTitle,
            Version = file.Version,
            Company = file.Company,
            Window = file.Window ?? new WindowConfiguration()
        };

        // §43 — génération atomique : un temporaire, une validation, puis un
        // déplacement. Aucun exécutable partiellement écrit ne doit apparaître.
        string temporary = target + $".tmp{Environment.ProcessId}";

        try
        {
            byte[] source = File.ReadAllBytes(selfPath);

            byte[] personalized = BundlePatcher.Personalize(
                source, icon, BuildVersionFields(file, targetName),
                Path.GetDirectoryName(temporary)!, out BundlePatcher.Report report);

            log.WriteLine($"Décalage  : {report.Delta:+#,##0;-#,##0;0} octets, "
                        + $"remplissage {report.Padding:N0}, "
                        + $"{report.RebasedFields} décalages réécrits pour {report.EmbeddedFiles} fichiers");

            if (!report.AlignmentPreserved)
                return new GenerationResult(false, "L'alignement des assemblies n'a pas été préservé.");

            List<EmbeddedPackage.FolderSource> folders =
            [
                new(EmbeddedPackage.AppFolder, appPath)
            ];

            if (embedUserFolders)
                folders.Add(new(EmbeddedPackage.DataFolder, Path.Combine(workingDirectory, EmbeddedPackage.DataFolder)));

            foreach (EmbeddedPackage.FolderSource folder in folders)
                log.WriteLine($"Embarqué  : {folder.Name}/ — {Describe(folder.Path)}");

            byte[] final = EmbeddedPackage.Append(personalized, configuration, folders);
            File.WriteAllBytes(temporary, final);

            log.WriteLine($"Archive   : {final.Length - personalized.Length:N0} octets");

            GenerationResult? failure = Verify(temporary, configuration, report);
            if (failure is not null)
                return failure;

            try
            {
                File.Move(temporary, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(target))
            {
                // Cause la plus fréquente et de loin : l'application cible tourne
                // encore. Le message du système ne le dit pas.
                return new GenerationResult(false,
                    $"« {targetName} » n'a pas pu être remplacé. "
                    + "L'application est-elle encore en cours d'exécution ?");
            }

            log.WriteLine($"Vérifié   : bundle cohérent, configuration relue");
            return new GenerationResult(true,
                $"{targetName} généré ({new FileInfo(target).Length:N0} octets).", target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            return new GenerationResult(false, ex.Message);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Contrôle de cohérence avant publication du résultat.
    /// </summary>
    /// <remarks>
    /// Un exécutable dont le bundle a été abîmé ne se distingue d'un exécutable sain
    /// que par sa taille : mieux vaut refuser de produire un fichier que d'en
    /// produire un muet.
    /// </remarks>
    private static GenerationResult? Verify(
        string path, AppConfiguration expected, BundlePatcher.Report report)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var info = PeInfo.Read(bytes, bytes.LongLength);

        if (!info.IsSingleFileBundle || info.BundleHeaderOffset >= EmbeddedPackage.PayloadLength(bytes))
            return new GenerationResult(false, "Le bundle du fichier produit est incohérent.");

        try
        {
            BundleManifest manifest = BundleManifest.Read(bytes, info.BundleHeaderOffset);

            if (manifest.Entries.Count != report.EmbeddedFiles)
                return new GenerationResult(false,
                    $"Le manifeste produit contient {manifest.Entries.Count} fichiers "
                    + $"au lieu de {report.EmbeddedFiles}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return new GenerationResult(false, $"Le manifeste produit est illisible : {ex.Message}");
        }

        AppConfiguration? relu = EmbeddedPackage.ReadConfiguration(path);

        return relu?.Name == expected.Name
            ? null
            : new GenerationResult(false, "La configuration embarquée n'a pas pu être relue.");
    }

    /// <summary>Résumé lisible du contenu d'un dossier à embarquer.</summary>
    private static string Describe(string path)
    {
        if (!Directory.Exists(path))
            return "absent";

        FileInfo[] files = new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories);

        return files.Length == 0
            ? "vide"
            : $"{files.Length} fichier(s), {files.Sum(f => f.Length):N0} octets";
    }

    private static VersionInfo.Fields BuildVersionFields(ConfigFile file, string targetName) => new()
    {
        ProductName = file.Name!,
        FileDescription = string.IsNullOrWhiteSpace(file.WindowTitle) ? file.Name! : file.WindowTitle,
        InternalName = Path.GetFileNameWithoutExtension(targetName),
        OriginalFilename = targetName,
        CompanyName = file.Company,
        Version = ParseVersion(file.Version),

        // La licence MIT demande que l'attribution du moteur accompagne toute copie.
        // L'exécutable produit contient Proton entier : c'est ici qu'elle se loge (§45.1).
        Comments = $"Built with {AppConfiguration.EngineName} {AppConfiguration.EngineVersion}"
                 + $" — {AppConfiguration.EngineCopyright}"
                 + $" ({AppConfiguration.EngineLicense} License)"
    };

    /// <summary>Une version absente ou mal formée ne doit pas faire échouer la génération.</summary>
    private static Version ParseVersion(string? text) =>
        Version.TryParse(text, out Version? version) ? version : new Version(1, 0, 0, 0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
