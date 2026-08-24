using System.Text.Json;
using ProtoPE;

string self = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath indisponible.");

if (args.Length > 0 && (args[0] is "/config" or "--config"))
    return Generator.Run(self, Directory.GetCurrentDirectory(), naive: args.Contains("--naive"));

if (args.Length > 1 && args[0] is "/bundle" or "--bundle")
{
    var bi = PeInfo.Read(args[1]);
    bi.Dump(Console.Out);
    if (bi.IsSingleFileBundle)
        BundleManifest.Read(File.ReadAllBytes(args[1]), bi.BundleHeaderOffset).Dump(Console.Out, 200);
    return 0;
}
if (args.Length > 1 && args[0] is "/inspect")
{
    Console.WriteLine($"Inspection de {args[1]}");
    PeInfo.Read(args[1]).Dump(Console.Out);
    return 0;
}

// ---- Mode normal ----
var config = EmbeddedConfig.TryRead(self, out string? raw);

Console.WriteLine("======================================================");
Console.WriteLine($"  {config?.Name ?? "ProtoPE (moteur générique)"}");
Console.WriteLine("======================================================");
Console.WriteLine($"Exécutable        : {Path.GetFileName(self)}");
Console.WriteLine($"Emplacement       : {Path.GetDirectoryName(self)}");
Console.WriteLine($"Config embarquée  : {(raw is null ? "(aucune — moteur générique)" : raw)}");
if (config is not null)
{
    Console.WriteLine($"  → titre fenêtre : {config.WindowTitle ?? config.Name}");
    Console.WriteLine($"  → version       : {config.Version ?? "(non définie)"}");
}

Console.WriteLine();
Console.WriteLine("--- Santé du runtime (preuve que le bundle single-file est intact) ---");
Console.WriteLine($"Version .NET      : {Environment.Version}");

// Force le chargement d'assemblies encore non chargés : ils ne peuvent venir
// que du bundle. Si le bundle était corrompu, ceci échouerait.
var rx = new System.Text.RegularExpressions.Regex(@"^(\w+)-(\d+)$");
var m = rx.Match("bundle-42");
Console.WriteLine($"Regex             : {(m.Success ? $"OK ({m.Groups[1].Value}/{m.Groups[2].Value})" : "ÉCHEC")}");

byte[] hash = System.Security.Cryptography.SHA256.HashData("proton"u8.ToArray());
Console.WriteLine($"SHA-256           : {Convert.ToHexString(hash)[..16]}...");

var xdoc = System.Xml.Linq.XDocument.Parse("""<proton ok="1" />""");
Console.WriteLine($"XML               : ok={xdoc.Root!.Attribute("ok")!.Value}");
Console.WriteLine($"Assemblies chargés: {AppDomain.CurrentDomain.GetAssemblies().Length}");

Console.WriteLine();
Console.WriteLine("--- Structure PE de cet exécutable ---");
PeInfo.Read(self).Dump(Console.Out);

Console.WriteLine();
Console.WriteLine("RÉSULTAT : démarrage complet réussi.");
return 0;
