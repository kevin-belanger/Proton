using Proton.Personalization;

namespace Proton.Configuration;

/// <summary>Dimensions et comportement de la fenêtre principale (§40).</summary>
public sealed record WindowConfiguration
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 800;
    public bool Resizable { get; init; } = true;
}

/// <summary>
/// Identité de l'application, telle que la porte l'exécutable.
///
/// Elle provient de la configuration embarquée par le mode <c>/generate</c> (§39), lue
/// en fin de fichier au démarrage. Un exécutable non personnalisé — le moteur
/// générique — n'en possède pas et retombe sur les valeurs par défaut.
/// </summary>
public sealed record AppConfiguration
{
    public required string Name { get; init; }
    public required string WindowTitle { get; init; }
    public string? Version { get; init; }
    public string? Company { get; init; }
    public WindowConfiguration Window { get; init; } = new();

    /// <summary>Identité du moteur, distincte de celle de l'application.</summary>
    public static string EngineName => "Proton";

    public static string EngineVersion =>
        typeof(AppConfiguration).Assembly.GetName().Version?.ToString(3) ?? "1.2.0";

    /// <summary>
    /// Licence du moteur.
    /// </summary>
    /// <remarks>
    /// Le moteur est embarqué dans chaque application produite : la licence MIT
    /// demande que son attribution y figure. Elle est portée à deux endroits — les
    /// métadonnées Windows de l'exécutable, et cette route (§45.1).
    /// </remarks>
    public static string EngineLicense => "MIT";

    public static string EngineCopyright => "Copyright (c) 2026 Kevin Belanger";

    /// <summary>Configuration du moteur générique, lancé sans personnalisation.</summary>
    public static AppConfiguration Default { get; } = new()
    {
        Name = EngineName,
        WindowTitle = EngineName
    };

    /// <summary>
    /// Charge la configuration embarquée dans l'exécutable en cours, ou les valeurs
    /// par défaut du moteur.
    /// </summary>
    public static AppConfiguration Load()
    {
        string? executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
            return Default;

        try
        {
            return EmbeddedPackage.ReadConfiguration(executable) ?? Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un exécutable illisible ne doit pas empêcher l'application de démarrer :
            // elle fonctionnera sous l'identité du moteur.
            return Default;
        }
    }
}
