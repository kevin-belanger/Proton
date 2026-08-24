namespace Proton.Configuration;

/// <summary>
/// Identité de l'application, telle que la porte l'exécutable.
///
/// Elle provient de la configuration embarquée par le mode <c>/config</c> (§39). Tant
/// que celui-ci n'existe pas, seules les valeurs par défaut du moteur sont
/// disponibles — la route <c>/api/app</c> doit répondre malgré tout (§24.1).
/// </summary>
public sealed record AppConfiguration
{
    public required string Name { get; init; }
    public required string WindowTitle { get; init; }
    public string? Version { get; init; }
    public string? Company { get; init; }

    /// <summary>Identité du moteur, distincte de celle de l'application.</summary>
    public static string EngineName => "Proton";

    public static string EngineVersion =>
        typeof(AppConfiguration).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Configuration du moteur générique, lancé sans personnalisation.</summary>
    public static AppConfiguration Default { get; } = new()
    {
        Name = EngineName,
        WindowTitle = EngineName,
        Version = null,
        Company = null
    };

    /// <summary>
    /// Charge la configuration de l'exécutable en cours.
    /// </summary>
    /// <remarks>
    /// La lecture de la configuration embarquée arrivera avec le mode <c>/config</c>
    /// (phase 6) ; le procédé est établi dans <c>docs/01</c>. D'ici là, le moteur ne
    /// connaît que son identité par défaut.
    /// </remarks>
    public static AppConfiguration Load() => Default;
}
