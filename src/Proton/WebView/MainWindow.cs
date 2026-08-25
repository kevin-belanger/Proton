using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Proton.Configuration;
using Proton.Infrastructure;

namespace Proton.WebView;

/// <summary>
/// Fenêtre principale d'une application Proton : une fenêtre Windows ordinaire dont
/// toute la surface est occupée par une WebView2.
///
/// Aucun élément de navigateur n'est visible — ni barre d'adresse, ni boutons de
/// navigation, ni onglets (§11). L'utilisateur doit percevoir une application native.
/// </summary>
public sealed class MainWindow : Form
{
    private readonly Uri _startAddress;
    private readonly WebView2 _webView;

    public MainWindow(Uri startAddress, AppConfiguration configuration)
    {
        _startAddress = startAddress;

        Text = configuration.WindowTitle;
        Width = configuration.Window.Width;
        Height = configuration.Window.Height;
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;

        // Une fenêtre non redimensionnable conserve une barre de titre et son bouton
        // de fermeture : seule la poignée de redimensionnement disparaît (§40).
        if (!configuration.Window.Resizable)
        {
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
        }

        // L'icône que porte l'exécutable, pour la barre de titre et la barre des
        // tâches (§41).
        Icon = WindowIcon.Load();

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
    }

    /// <summary>Code de sortie du processus, renseigné en cas d'échec d'affichage.</summary>
    public int ExitCode { get; private set; }

    /// <summary>
    /// L'initialisation de la WebView est asynchrone et se déroule une fois la boucle
    /// de messages en place : c'est le seul contexte où ses continuations reviennent
    /// sur le thread de l'interface.
    /// </summary>
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            await InitialiseWebViewAsync().ConfigureAwait(true);
        }
        catch (WebViewUnavailableException ex)
        {
            ErrorDialog.ShowWebViewFailure(ex);
            ExitCode = 2;
            Close();
        }
        catch (Exception ex)
        {
            ErrorDialog.ShowStartupFailure(ex);
            ExitCode = 1;
            Close();
        }
    }

    private async Task InitialiseWebViewAsync()
    {
        CoreWebView2Environment environment;

        try
        {
            // Le dossier de données de la WebView doit rester hors du dossier de
            // l'application : sans cela WebView2 crée un dossier de cache à côté de
            // l'exécutable, ce qui contredit la simplicité de distribution du §2.
            environment = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: ResolveUserDataFolder())
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException)
        {
            throw new WebViewUnavailableException(
                "No usable WebView2 environment was found on this computer.", ex);
        }

        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        ConfigureBrowserBehaviour(_webView.CoreWebView2);

        _webView.CoreWebView2.Navigate(_startAddress.ToString());
    }

    private void ConfigureBrowserBehaviour(CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;

        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsSwipeNavigationEnabled = false;

        // Aucune navigation ne doit faire disparaître l'application (§51). La fenêtre
        // n'ayant ni bouton Précédent ni barre d'adresse (§11), il n'existerait aucun
        // moyen d'en revenir : l'utilisateur devrait fermer et relancer.
        //
        // NavigationStarting ne concerne que le document principal. Les images, les
        // feuilles de style, les requêtes fetch et les cadres passent par d'autres
        // événements et ne sont donc jamais affectés par ce filtre.
        core.NavigationStarting += (_, e) =>
        {
            if (!LeavesTheApplication(e.Uri))
                return;

            e.Cancel = true;
            Redirect(e.Uri);
        };

        // Idem pour tout ce qui demanderait une nouvelle fenêtre : Proton V1 ne gère
        // qu'une fenêtre (§60). C'est par ici que passe un lien portant `target`.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Redirect(e.Uri);
        };
    }

    /// <summary>
    /// Achemine une adresse que la WebView ne doit pas suivre.
    /// </summary>
    /// <remarks>
    /// Une ressource servie par Proton est téléchargée puis ouverte avec
    /// l'application associée (§51.2) ; une adresse étrangère part vers le navigateur
    /// par défaut (§51).
    /// </remarks>
    private void Redirect(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            return;

        if (IsOwnOrigin(parsed))
        {
            // Volontairement sans await : la fenêtre ne doit pas se figer le temps du
            // téléchargement, qui peut porter sur un fichier volumineux.
            _ = ResourceOpener.OpenAsync(parsed);
            return;
        }

        OpenInDefaultBrowser(uri);
    }

    /// <summary>
    /// Indique qu'une navigation ferait quitter l'application à la WebView.
    /// </summary>
    /// <remarks>
    /// Deux cas : une origine étrangère, et les espaces réservés aux API. Ces
    /// derniers servent des données, non des pages — ouvrir une pièce jointe par
    /// <c>/data/rapport.pdf</c> afficherait le document à la place de l'application.
    /// Les pages de <c>app</c> restent libres de naviguer entre elles.
    /// </remarks>
    private bool LeavesTheApplication(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            return false;

        return !IsOwnOrigin(parsed) || Hosting.ReservedSpaces.Contains(parsed.AbsolutePath);
    }

    /// <summary>L'adresse est-elle servie par cette instance de Proton ?</summary>
    private bool IsOwnOrigin(Uri uri) =>
        uri.IsLoopback && uri.Port == _startAddress.Port;

    private static void OpenInDefaultBrowser(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            return;

        // Seuls les schémas Web sont relayés : un `file:` ou un schéma exotique
        // provenant de la page ne doit pas devenir un moyen de lancer un programme.
        if (parsed.Scheme is not ("http" or "https"))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(parsed.ToString()) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // L'absence de navigateur par défaut ne doit pas interrompre l'application.
        }
    }

    /// <summary>
    /// Dossier de travail de la WebView, dans le profil de l'utilisateur.
    /// </summary>
    private static string ResolveUserDataFolder()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        string folder = Path.Combine(root, "Proton", "WebView2");
        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>
/// Aucun runtime WebView2 utilisable n'est disponible sur la machine (§55).
/// </summary>
public sealed class WebViewUnavailableException(string message, Exception inner)
    : Exception(message, inner);
