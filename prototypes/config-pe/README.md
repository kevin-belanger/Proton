# Prototype `/config` — reconstruction de l'exécutable

**Date :** 2026-08-24
**Objet :** vérifier expérimentalement la faisabilité des §37 à §44
**Verdict :** faisable — voir [`notes/01-personnalisation-executable.md`](../../notes/01-personnalisation-executable.md)
pour le procédé, les décisions arrêtées et les règles d'implémentation.

Ce document conserve le **protocole et les mesures**. Le savoir technique réutilisable
vit dans la note 01 : c'est elle qu'il faut lire avant d'écrire le code de production.

---

## La question posée

L'analyse exige (§37-39) qu'un exécutable Proton se recopie et personnalise sa copie —
icône, métadonnées, configuration embarquée — sans dépendre du SDK .NET, et que
l'enfant reste lui-même capable de générer un autre exécutable (§38, CA-17).

En parallèle, la phase 7 vise une publication *self-contained single-file*.

Ces deux exigences entrent en collision. C'était le risque principal du projet, d'où
ce prototype avant toute autre implémentation.

---

## Protocole

Trois essais en escalier, du moins risqué au plus risqué :

| # | Essai | Résultat |
| --- | --- | --- |
| 0 | Publier un exe single-file et l'inspecter | Structure relevée : PE 9,97 Mo + bundle 63,4 Mo, 177 décalages absolus |
| 1 | `UpdateResource` directement sur l'exe publié | **Échec** — bundle amputé de 63 454 742 octets, exe mort |
| 2 | Découpage, patch du PE isolé, recollage, rebasage | **Succès** — 4 générations enchaînées |

L'essai 1 est conservé dans le code (`--naive`) : il reproduit l'échec en une commande,
ce qui vaut mieux qu'une mise en garde écrite.

---

## Mesures

### Chaîne de quatre générations, chacune avec sa propre icône

| Génération | Icône source | Écart brut | Décalage | Remplissage | Démarrage |
| --- | --- | --- | --- | --- | --- |
| `ProtoPE.exe` (moteur) | — (bleu .NET) | — | — | — | OK |
| `GestionInventaire.exe` | 32×32, 4 286 o | +4 096 | +4 096 | 0 | OK |
| `SuiviChantier.exe` | 64×64, 16 958 o | +12 800 | +16 384 | 3 584 | OK |
| `PetiteAppli.exe` | 16×16, 1 150 o | **−15 872** | −12 288 | 3 584 | OK |

Icônes vérifiées pixel à pixel après extraction : bleu .NET / orange / vert / rouge.
Le cas d'une icône plus petite que celle du parent (décalage négatif) est donc couvert.

### Critères d'acceptation

| CA | Énoncé | Résultat |
| --- | --- | --- |
| CA-14 | Génération sans modifier la source | **OK** — SHA-256 du moteur identique après chaque passe |
| CA-15 | Nom et icône configurés appliqués | **OK** — vérifié sur les 3 enfants |
| CA-16 | `config/` supprimable après génération | **OK** |
| CA-17 | L'enfant régénère à son tour | **OK** — validé sur 4 générations |
| §43 | Génération atomique | **OK** — temporaire, validation, puis déplacement |
| §44 | Régénération réexécutable | **OK** — et déterministe (hash identique à la 2ᵉ passe) |

### Compression

| Variante | Taille du moteur | 2 générations enchaînées |
| --- | --- | --- |
| Sans compression | 73 442 838 o | OK |
| `EnableCompressionInSingleFile` | 37 530 724 o (**−49 %**) | OK |

Enseignement au passage : dans un bundle compressé, les assemblies ne sont pas alignés
dès l'origine (0 sur 173). Un contrôle d'alignement inconditionnel produit une fausse
alerte — corrigé ici, et consigné comme règle nº 4 dans la note 01.

---

## Contenu

| Fichier | Rôle |
| --- | --- |
| `PeInfo.cs` | Lecture des en-têtes PE, fin des sections, localisation de la signature du bundle |
| `BundleManifest.cs` | Analyse du manifeste et rebasage des décalages absolus |
| `BundleAwarePatcher.cs` | Découpage, patch du PE isolé, recollage aligné |
| `IconPatcher.cs` | Découpe d'un `.ico`, construction du `RT_GROUP_ICON`, appels Win32 |
| `EmbeddedConfig.cs` | Trailer de configuration : lecture, retrait, ajout |
| `Generator.cs` | Orchestration du mode `/config` et contrôles de cohérence |
| `Program.cs` | Modes normal, `/config`, `/bundle` |

Environ 750 lignes, sans dépendance hors BCL.

---

## Reproduire

```bash
dotnet publish -c Release -p:EnableCompressionInSingleFile=true -o pub
```

Puis, dans un dossier contenant `ProtoPE.exe`, `config/config.json` et `config/icon.ico` :

```text
ProtoPE.exe /config           # stratégie sûre
ProtoPE.exe /config --naive   # reproduit l'échec de l'essai 1
ProtoPE.exe /bundle <exe>     # inspection du PE et du manifeste
ProtoPE.exe                   # mode normal : identité, config lue, santé du runtime
```

Deux précautions :

- Sous Git Bash, `/config` est réécrit en chemin Windows par MSYS et n'atteint pas le
  programme. Utiliser PowerShell ou `cmd`, ou passer `--config`.
- Ne pas compiler dans un dossier synchronisé par un service de stockage en ligne :
  chaque publication y pousserait des dizaines de mégaoctets.
