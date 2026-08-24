using System.Diagnostics;
using System.Runtime.InteropServices;
using Proton.Infrastructure;

namespace Proton.WebView;

/// <summary>
/// Ouverture d'une ressource servie par Proton (§51.2).
///
/// Lorsqu'une navigation vers <c>/data</c> ou <c>/api</c> est interceptée (§51.1), le
/// fichier est téléchargé puis confié à l'application que le système lui associe —
/// le comportement d'un client de messagerie ouvrant une pièce jointe.
///
/// Le confier au navigateur ne conviendrait pas : le document s'afficherait dans
/// Edge plutôt que dans le lecteur choisi par l'utilisateur, et l'adresse contient un
/// port éphémère (§9.2) — l'onglet cesserait de fonctionner dès la fermeture de
/// l'application, et l'entrée laissée dans l'historique serait morte.
/// </summary>
public static class ResourceOpener
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Télécharge la ressource et l'ouvre. L'appel rend la main immédiatement :
    /// l'interface ne doit pas se figer le temps d'un téléchargement.
    /// </summary>
    public static async Task OpenAsync(Uri uri)
    {
        try
        {
            string path = await DownloadAsync(uri).ConfigureAwait(true);
            Launch(path);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error($"Impossible d'ouvrir « {uri.AbsolutePath} ».", ex);

            MessageBox.Show(
                $"""
                 Le fichier n'a pas pu être ouvert.

                 {ex.Message}
                 """,
                "Proton", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static async Task<string> DownloadAsync(Uri uri)
    {
        using HttpResponseMessage response = await Client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(true);

        response.EnsureSuccessStatusCode();

        string destination = ChooseDestination(DownloadsFolder(), ResolveFileName(uri, response));

        await using (var file = File.Create(destination))
        {
            await response.Content.CopyToAsync(file).ConfigureAwait(true);
        }

        DiagnosticLog.Info($"Ressource téléchargée : {uri.AbsolutePath} → {destination}");
        return destination;
    }

    /// <summary>
    /// Nom sous lequel enregistrer la ressource.
    /// </summary>
    /// <remarks>
    /// L'en-tête <c>Content-Disposition</c> prime lorsqu'il est présent : c'est le
    /// nom que le serveur a choisi. À défaut, celui de l'URL.
    /// </remarks>
    private static string ResolveFileName(Uri uri, HttpResponseMessage response)
    {
        string? name = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

        name ??= Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));

        return SanitiseFileName(name);
    }

    /// <summary>
    /// Rend un nom d'URL utilisable comme nom de fichier.
    /// </summary>
    /// <remarks>
    /// Ce nom vient d'une adresse : rien ne garantit qu'il soit valide, et il ne doit
    /// surtout pas pouvoir désigner un autre dossier.
    /// </remarks>
    internal static string SanitiseFileName(string? name)
    {
        // Une route d'API peut ne pas se terminer par un nom de fichier.
        if (string.IsNullOrWhiteSpace(name))
            return "proton-download";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        // Windows retire les points et espaces finaux (§17.2) : un nom qui s'y
        // réduirait ne désignerait plus rien.
        name = name.TrimEnd('.', ' ');

        return name.Length == 0 ? "proton-download" : name;
    }

    /// <summary>
    /// Choisit un chemin libre dans le dossier des téléchargements.
    /// </summary>
    /// <remarks>
    /// Ouvrir deux fois la même pièce jointe ne doit pas écraser le fichier
    /// précédent, que l'utilisateur a peut-être encore ouvert.
    /// </remarks>
    internal static string ChooseDestination(string folder, string fileName)
    {
        string candidate = Path.Combine(folder, fileName);

        if (!File.Exists(candidate))
            return candidate;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int i = 1; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static void Launch(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    // --- Dossier des téléchargements -------------------------------------------------

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    /// <summary>
    /// Dossier des téléchargements de l'utilisateur.
    /// </summary>
    /// <remarks>
    /// <c>Environment.SpecialFolder</c> ne le connaît pas : il faut interroger le
    /// shell, car l'utilisateur a pu le déplacer ailleurs que sous son profil.
    /// </remarks>
    private static string DownloadsFolder()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out buffer) == 0)
            {
                string? path = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(buffer);
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
