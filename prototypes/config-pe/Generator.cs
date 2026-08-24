using System.Text.Json;

namespace ProtoPE;

public static class Generator
{
    public static int Run(string selfPath, string workDir, bool naive)
    {
        string configDir = Path.Combine(workDir, "config");
        string configJson = Path.Combine(configDir, "config.json");
        string iconPath = Path.Combine(configDir, "icon.ico");

        Console.WriteLine("=== Mode /config ===");
        Console.WriteLine($"Source    : {selfPath}");
        Console.WriteLine($"Stratégie : {(naive ? "NAÏVE (UpdateResource direct)" : "SÛRE (découpage / recollage / rebasage)")}");

        if (!File.Exists(configJson))
        {
            Console.Error.WriteLine($"ERREUR : {configJson} est introuvable.");
            return 2;
        }

        AppConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configJson),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"ERREUR : config.json illisible — {ex.Message}");
            return 2;
        }

        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Name) || string.IsNullOrWhiteSpace(cfg.ExecutableName))
        {
            Console.Error.WriteLine("ERREUR : 'name' et 'executableName' sont obligatoires.");
            return 2;
        }

        string targetName = cfg.ExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? cfg.ExecutableName : cfg.ExecutableName + ".exe";
        string target = Path.Combine(workDir, targetName);

        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("ERREUR : la cible est l'exécutable source. Un exe ne se modifie jamais lui-même.");
            return 2;
        }

        Console.WriteLine($"Cible     : {target}");
        Console.WriteLine();

        string temp = target + $".tmp{Environment.ProcessId}";
        try
        {
            byte[] source = File.ReadAllBytes(selfPath);
            var before = PeInfo.Read(source, source.LongLength);
            Console.WriteLine("[1] Exécutable source :");
            before.Dump(Console.Out);
            if (before.IsSingleFileBundle)
                BundleManifest.Read(source, before.BundleHeaderOffset).Dump(Console.Out, 5);
            Console.WriteLine();

            string? ico = File.Exists(iconPath) ? iconPath : null;
            Console.WriteLine(ico is null
                ? "[2] Pas d'icon.ico — l'icône ne sera pas modifiée."
                : $"[2] Icône : {new FileInfo(iconPath).Length:N0} octets");

            byte[] personalized;
            if (naive)
            {
                byte[] stripped = EmbeddedConfig.Strip(source);
                File.WriteAllBytes(temp, stripped);
                if (ico is not null) IconPatcher.ApplyWin32(temp, ico);
                personalized = File.ReadAllBytes(temp);
            }
            else
            {
                personalized = BundleAwarePatcher.Personalize(source, ico, Path.GetDirectoryName(temp)!, out var rep);
                Console.WriteLine();
                Console.WriteLine("[3] Recollage du bundle :");
                Console.WriteLine($"      Fin du PE          : {rep.OldPeEnd:N0} → {rep.NewPeEnd:N0}");
                Console.WriteLine($"      Écart brut         : {rep.RawDelta:+#,##0;-#,##0;0} octets");
                Console.WriteLine($"      Décalage retenu    : {rep.Delta:+#,##0;-#,##0;0} octets (multiple de 4 096)");
                Console.WriteLine($"      Remplissage inséré : {rep.Padding:N0} octets");
                Console.WriteLine($"      Manifeste          : {rep.OldHeaderOffset:N0} → {rep.NewHeaderOffset:N0}");
                Console.WriteLine($"      Décalages réécrits : {rep.RebasedFields} (pour {rep.EmbeddedFiles} fichiers embarqués)");
                Console.WriteLine($"      Alignement 4 K     : {(rep.AlignmentPreserved ? "préservé" : "ROMPU")}");
            }

            byte[] final = EmbeddedConfig.Append(personalized, cfg);
            File.WriteAllBytes(temp, final);
            Console.WriteLine();
            Console.WriteLine($"[4] Configuration embarquée : {final.Length - personalized.Length:N0} octets de trailer.");

            // --- Contrôles avant publication du résultat ---
            var after = PeInfo.Read(temp);
            Console.WriteLine();
            Console.WriteLine("[5] Vérification du fichier généré :");
            after.Dump(Console.Out);

            bool ok = after.IsSingleFileBundle && after.BundleHeaderOffset < EmbeddedConfig.PayloadLength(final);
            if (ok)
            {
                try
                {
                    BundleManifest.Read(final, after.BundleHeaderOffset).Dump(Console.Out, 5);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Manifeste illisible : {ex.Message}");
                    ok = false;
                }
            }

            var check = EmbeddedConfig.TryRead(temp, out string? raw);
            if (check is null || check.Name != cfg.Name)
            {
                Console.Error.WriteLine("ERREUR : relecture de la configuration embarquée impossible.");
                return 3;
            }
            Console.WriteLine($"      Config relue       : {raw}");

            File.Move(temp, target, overwrite: true);
            Console.WriteLine();
            Console.WriteLine($"=== Généré : {targetName} ({new FileInfo(target).Length:N0} octets) — cohérence structurelle : {(ok ? "OK" : "ÉCHEC")} ===");
            return ok ? 0 : 4;
        }
        finally
        {
            if (File.Exists(temp)) { try { File.Delete(temp); } catch { } }
        }
    }
}
