using Proton.Hosting;

namespace Proton.Tests;

/// <summary>
/// Délimitation des espaces réservés aux API (§49).
///
/// La même liste sert au serveur, qui y route les API, et à la fenêtre, qui refuse
/// d'y naviguer : une erreur de frontière se paierait donc deux fois.
/// </summary>
public sealed class ReservedSpacesTests
{
    [Theory]
    [InlineData("/files")]
    [InlineData("/files/")]
    [InlineData("/files/settings.json")]
    [InlineData("/files/attachments/7/rapport.pdf")]
    [InlineData("/api")]
    [InlineData("/api/app")]
    [InlineData("/api/sqlite/todo.db/query")]
    [InlineData("/FILES/settings.json")]
    public void Reconnait_les_espaces_reserves(string path) =>
        Assert.True(ReservedSpaces.Contains(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/css/style.css")]
    // Le préfixe se compare par segment : ces chemins appartiennent à l'application,
    // même s'ils commencent par les mêmes lettres.
    [InlineData("/fileserver.html")]
    [InlineData("/files-export.csv")]
    [InlineData("/apidoc.html")]
    [InlineData("/application/index.html")]
    public void Laisse_le_reste_a_l_application(string path) =>
        Assert.False(ReservedSpaces.Contains(path));
}
