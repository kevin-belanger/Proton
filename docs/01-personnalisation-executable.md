# Note technique 01 — Personnalisation d'un exécutable .NET single-file

**Statut :** établi expérimentalement, décisions arrêtées
**Date :** 2026-08-24
**Couvre :** analyse fonctionnelle §37 à §45, phase 6, phase 7
**Preuve :** `prototypes/config-pe/` — 4 générations enchaînées, 2 modes de publication

---

## 1. Décisions arrêtées

| Question | Décision |
| --- | --- |
| Publication | **Self-contained + single-file**, conservée. La distribution décrite en §2 tient. |
| Compression | **`EnableCompressionInSingleFile` activée.** 73,4 Mo → 37,5 Mo, sans effet sur la personnalisation. |
| Bibliothèque PE tierce | **Aucune.** Environ 750 lignes de C#, sans dépendance hors BCL. |
| Modification des ressources | `UpdateResource` **sur le PE isolé uniquement**, jamais sur le fichier publié. |
| Configuration embarquée | **Trailer en fin de fichier**, après le bundle — pas une ressource PE. |

Ces cinq points ferment autant de questions laissées ouvertes en §67.

---

## 2. Anatomie d'un exécutable single-file

Mesures relevées sur un exe .NET 10 publié en `SelfContained` + `PublishSingleFile`
pour `win-x64` :

```text
offset 0          9 971 712                                    73 442 838
   |                  |                                             |
   +------------------+---------------------------------------------+
   |  singlefilehost  |  175 fichiers embarques      |  manifeste   |
   |     (PE, 13 %)   |         (bundle, 87 %)       |              |
   +------------------+------------------------------+--------------+
      9 sections                                        ^
      dont .rsrc (1,35 Mo) puis .reloc                  |
                                                 pointe depuis .data
```

Le PE ne représente que **13 %** du fichier. C'est le `singlefilehost` : un host lié
statiquement qui embarque `hostfxr`, `hostpolicy` et CoreCLR — d'où sa taille, sans
rapport avec l'application elle-même.

### Le pointeur vers le manifeste

Il est stocké **dans la section `.data`**, aux 8 octets qui précèdent immédiatement
une signature de 32 octets (le SHA-256 de la chaîne `.net core bundle`) :

```text
8b 12 02 b9 6a 61 20 38 72 7b 93 02 14 d7 a0 32
13 f5 b9 e6 ef ae 33 18 ee 3b 2d ce 24 b3 6a ae
```

Le SDK écrit l'offset à cet emplacement au moment de la publication. Le rechercher à
l'exécution est fiable : la signature est unique dans le fichier.

### Le manifeste

Format version 6 (.NET 6 et suivants) :

```text
uint32  majorVersion            = 6
uint32  minorVersion
int32   nombre de fichiers      = 175
string  bundleID                          (longueur encodee sur 7 bits, puis UTF-8)
int64   offset deps.json                  <-- decalage absolu
int64   taille deps.json
int64   offset runtimeconfig.json         <-- decalage absolu
int64   taille runtimeconfig.json
uint64  flags
puis, par fichier :
  int64 offset                            <-- decalage absolu
  int64 taille
  int64 taille compressee                 (version >= 6 ; 0 si stocke tel quel)
  byte  type                              (1 = Assembly, 2 = NativeBinary, ...)
  string chemin relatif
```

> **Le fait déterminant :** tous ces décalages sont **absolus depuis le début du
> fichier**. Sur l'exe de référence, cela représente **177 champs** — 175 fichiers
> plus `deps.json` et `runtimeconfig.json`. Déplacer le bundle d'un seul octet les
> invalide tous.

---

## 3. Le piège : pourquoi la voie évidente échoue

Appliquer `BeginUpdateResource` / `UpdateResource` / `EndUpdateResource` directement
sur l'exe publié donne :

```text
73 430 550 -> 9 975 808 octets   (-63 454 742)
```

`EndUpdateResource` **reconstruit le fichier à partir de ses seuls en-têtes PE** et
supprime tout ce qui suit la dernière section. Le bundle disparaît intégralement.
L'exe produit se lance et meurt aussitôt :

```text
Failure processing application bundle; possible file corruption.
Arithmetic overflow while reading bundle.
```

Ce piège vaut pour **toute** bibliothèque de manipulation de ressources bâtie sur ces
API Win32 — ResourceHacker et assimilés inclus. Il ne se manifeste pas sur un exe
ordinaire, ce qui le rend d'autant plus facile à introduire par inadvertance.

---

## 4. Procédé retenu

```text
1. Retirer le trailer de configuration herite
2. Decouper a la fin des sections :  [ PE ] | [ bundle ]
3. Patcher les ressources du PE ISOLE — il n'y a plus rien a tronquer
4. Choisir un decalage multiple de 4 096 ; le remplissage absorbe la difference
5. Recoller :  [ PE patche ][ remplissage ][ bundle ]
6. Reecrire le pointeur du manifeste dans .data
7. Ajouter le decalage aux 177 champs du manifeste
8. Annexer le trailer de configuration en toute fin de fichier
```

### Pourquoi l'étape 1

Sans elle, chaque génération empilerait la configuration de son parent. §38 et CA-17
exigent qu'un enfant soit lui-même générateur : le retrait du trailer hérité est ce
qui rend la chaîne indéfiniment reproductible.

### Pourquoi l'étape 4

Les assemblies **stockés tels quels** sont mappés directement en mémoire par le host
et doivent rester alignés sur la taille de page. Contraindre le décalage à un multiple
de 4 096 préserve cet alignement quel que soit le sens de variation de `.rsrc`.
Le remplissage inséré vaut de 0 à 4 095 octets.

L'arrondi doit se faire **vers le haut y compris pour les valeurs négatives** — cas
réel lorsque l'icône de l'enfant est plus petite que celle du parent :

| Icône | Écart brut | Décalage retenu | Remplissage |
| --- | --- | --- | --- |
| 32×32 (4 286 o) | +4 096 | +4 096 | 0 |
| 64×64 (16 958 o) | +12 800 | +16 384 | 3 584 |
| 16×16 (1 150 o) | **−15 872** | −12 288 | 3 584 |

### Pourquoi le trailer plutôt qu'une ressource PE

Placer la configuration **après** le bundle la rend invisible au host : aucun décalage
n'est affecté, et l'étape 7 n'a pas à en tenir compte. Disposition :

```text
[ ... exe ... ][ JSON UTF-8 ][ longueur int32 LE ][ magic « PRTNCFG1 » ]
```

Lecture à l'exécution : ouvrir `Environment.ProcessPath`, lire les 12 derniers octets,
vérifier le magic, remonter de la longueur annoncée. Coût négligeable au démarrage.

> `Environment.ProcessPath` et non `Assembly.Location` : en single-file, cette
> dernière retourne une chaîne vide.

---

## 5. Règles à respecter dans le code de production

1. **Ne jamais appeler `UpdateResource` sur un fichier contenant un bundle.**
   Le patcher doit vérifier que la fin des sections coïncide avec la fin du fichier
   avant d'invoquer les API Win32, et refuser sinon.
2. **Refuser de produire un exe plutôt que d'en produire un muet.** Si la signature du
   bundle est introuvable ou le manifeste illisible, échouer avec un message explicite.
   Un exe amputé ne se distingue d'un exe sain que par sa taille.
3. **Contrôler la cohérence structurelle avant de publier le résultat** : relire le
   manifeste dans le fichier final, vérifier que le nombre d'entrées est inchangé et
   que le pointeur tombe à l'intérieur du fichier. Le prototype le fait juste avant
   le déplacement du temporaire.
4. **Ne contrôler l'alignement que sur les entrées non compressées.** Dans un bundle
   compressé, les assemblies ne sont pas alignés dès l'origine (0 sur 173 sur l'exe de
   référence) : ils sont décompressés en mémoire, l'alignement n'a plus d'objet. Un
   contrôle inconditionnel produit une fausse alerte — l'erreur a été commise puis
   corrigée dans le prototype.
5. **Génération atomique** (§43) : écrire un temporaire, valider, puis déplacer avec
   remplacement. L'exécutable source ne doit jamais être ouvert en écriture.
6. **Refuser une cible identique à la source.** Un exe ne se modifie jamais lui-même.

### Propriété observée

La génération est **déterministe** : régénérer avec la même configuration et la même
icône produit un fichier au SHA-256 identique. Utile pour §44 et pour d'éventuels
tests de non-régression.

---

## 6. Zones non couvertes

| Sujet | État |
| --- | --- |
| Métadonnées `VERSIONINFO` (§42) | Non implémenté. Même mécanique (`RT_VERSION`), sans risque nouveau pour le bundle, mais la ressource doit être reconstruite octet à octet. |
| Icônes multi-résolutions | Le code gère N images ; testé en mono-image seulement (16/32/64). Une icône réaliste (16+32+48+256, le 256 en PNG) reste à exercer. |
| Cible en cours d'exécution | Le déplacement échouera. À traduire en message clair plutôt qu'en exception brute (§54). |
| Signature Authenticode (§45) | Le recollage invalide toute signature du moteur. Conforme à l'analyse : l'enfant est non signé jusqu'à nouvelle signature. |
| `ReadyToRun`, trimming, win-arm64 | Non exercés. Le trimming en particulier modifie le nombre d'entrées du bundle — à revalider s'il est activé. |

---

## 7. Références

- Code de référence : `prototypes/config-pe/` — `PeInfo.cs`, `BundleManifest.cs`,
  `BundleAwarePatcher.cs`, `EmbeddedConfig.cs`
- Protocole expérimental et mesures : `prototypes/config-pe/README.md`
- Analyse fonctionnelle : §37 à §45, phase 6, phase 7, §67

> Sous Git Bash, `/config` est réécrit en chemin Windows par MSYS et l'argument
> n'arrive pas au programme. Tester depuis PowerShell ou `cmd`, ou utiliser `--config`.
