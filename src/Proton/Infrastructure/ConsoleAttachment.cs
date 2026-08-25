using System.Runtime.InteropServices;

namespace Proton.Infrastructure;

/// <summary>
/// Rattache le processus à la console qui l'a lancé.
///
/// Proton est publié en application graphique : il ne possède pas de console, et sa
/// sortie standard n'aboutit nulle part. Le mode <c>/generate</c> étant un outil de
/// ligne de commande, il doit pouvoir écrire là où le développeur l'a lancé.
///
/// Lorsqu'aucune console n'existe — un double-clic depuis l'Explorateur — la
/// tentative échoue sans conséquence, et l'appelant se rabat sur une autre forme
/// de restitution.
/// </summary>
public static class ConsoleAttachment
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    /// <summary>Tente le rattachement. Retourne false si aucune console n'est disponible.</summary>
    public static bool TryAttach()
    {
        if (!AttachConsole(AttachParentProcess))
            return false;

        try
        {
            // Les flux ont déjà été ouverts sur le néant au démarrage du processus :
            // il faut les rouvrir sur la console fraîchement rattachée.
            var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
