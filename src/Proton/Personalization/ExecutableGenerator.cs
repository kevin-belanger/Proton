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
                $"\"{appPath}\" was not found. The application to embed must exist.");

        if (!File.Exists(configPath))
            return new GenerationResult(false, $"\"{configPath}\" was not found.");

        ConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return new GenerationResult(false, $"config.json could not be read: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(file?.Name) || string.IsNullOrWhiteSpace(file.ExecutableName))
            return new GenerationResult(false, "\"name\" and \"executableName\" are required.");

        string targetName = file.ExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? file.ExecutableName
            : file.ExecutableName + ".exe";

        string target = Path.Combine(workingDirectory, targetName);

        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
            return new GenerationResult(false, "The target is the source executable; an executable never modifies itself.");

        log.WriteLine($"Target    : {target}");

        string? icon = File.Exists(iconPath) ? iconPath : null;
        log.WriteLine(icon is null
            ? "Icon      : none (config/icon.ico not found)"
            : $"Icon      : {new FileInfo(iconPath).Length:N0} bytes");

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

            log.WriteLine($"Shift     : {report.Delta:+#,##0;-#,##0;0} bytes, "
                        + $"{report.Padding:N0} padding, "
                        + $"{report.RebasedFields} offsets rewritten for {report.EmbeddedFiles} files");

            if (!report.AlignmentPreserved)
                return new GenerationResult(false, "Assembly alignment was not preserved.");

            List<EmbeddedPackage.FolderSource> folders =
            [
                new(EmbeddedPackage.AppFolder, appPath)
            ];

            if (embedUserFolders)
                folders.Add(new(EmbeddedPackage.DataFolder, Path.Combine(workingDirectory, EmbeddedPackage.DataFolder)));

            foreach (EmbeddedPackage.FolderSource folder in folders)
                log.WriteLine($"Embedded  : {folder.Name}/ — {Describe(folder.Path)}");

            byte[] final = EmbeddedPackage.Append(personalized, configuration, folders);
            File.WriteAllBytes(temporary, final);

            log.WriteLine($"Archive   : {final.Length - personalized.Length:N0} bytes");

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
                    $"\"{targetName}\" could not be replaced. "
                    + "Is the application still running?");
            }

            log.WriteLine("Verified  : bundle consistent, configuration read back");
            return new GenerationResult(true,
                $"{targetName} generated ({new FileInfo(target).Length:N0} bytes).", target);
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
            return new GenerationResult(false, "The bundle of the generated file is inconsistent.");

        try
        {
            BundleManifest manifest = BundleManifest.Read(bytes, info.BundleHeaderOffset);

            if (manifest.Entries.Count != report.EmbeddedFiles)
                return new GenerationResult(false,
                    $"The generated manifest holds {manifest.Entries.Count} files "
                    + $"instead of {report.EmbeddedFiles}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return new GenerationResult(false, $"The generated manifest could not be read: {ex.Message}");
        }

        AppConfiguration? relu = EmbeddedPackage.ReadConfiguration(path);

        return relu?.Name == expected.Name
            ? null
            : new GenerationResult(false, "The embedded configuration could not be read back.");
    }

    /// <summary>Résumé lisible du contenu d'un dossier à embarquer.</summary>
    private static string Describe(string path)
    {
        if (!Directory.Exists(path))
            return "missing";

        FileInfo[] files = new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories);

        return files.Length == 0
            ? "empty"
            : $"{files.Length} file(s), {files.Sum(f => f.Length):N0} bytes";
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
