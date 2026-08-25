# Proton

Moteur Windows autonome permettant d'exécuter une application HTML / CSS / JavaScript
comme une application de bureau.

Proton démarre un serveur local, sert l'application depuis un dossier `app`, et lui
donne accès à des capacités hors de portée d'une page Web ordinaire : lecture et
écriture de fichiers, bases SQLite locales.

Une application Proton se distribue par simple copie :

```text
MonApplication.exe      ← un seul fichier
```

L'application Web est embarquée dans l'exécutable. Au premier démarrage, celui-ci crée
à côté de lui les dossiers dont il a besoin :

```text
MonApplication/
├── MonApplication.exe
└── data/
    ├── files/    ses fichiers
    └── db/       ses bases SQLite
```

Ni installateur, ni serveur, ni runtime à installer séparément.

---

## Démarrer

Téléchargez `Proton.exe`, placez-le dans un dossier vide et lancez-le. Il y crée
`app` et `data`, puis affiche une page d'accueil.

Remplacez ensuite le contenu de `app` par le vôtre. Vos pages sont servies à la
racine :

```text
app/index.html      →  /
app/css/style.css   →  /css/style.css
```

L'application n'a jamais à connaître son emplacement sur le disque, ni le port
retenu : les URL relatives suffisent.

### Les API

Aucune bibliothèque n'est requise — tout passe par `fetch`.

```js
// Identité de l'application, telle que l'exécutable la porte
const app = await (await fetch('/api/app')).json();

// Fichiers
await fetch('/files/notes.txt', { method: 'PUT', body: 'bonjour' });
const texte = await (await fetch('/files/notes.txt')).text();
const { entries } = await (await fetch('/files/dossier/')).json();

// Dossiers — la barre oblique finale les désigne
await fetch('/files/photos/', { method: 'PUT' });
await fetch('/files/photos/?recursive=1', { method: 'DELETE' });

// SQLite
await fetch('/api/sqlite/app.db/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
        sql: 'INSERT INTO notes(texte) VALUES($t)',
        parameters: { $t: 'bonjour' }
    })
});
```

`samples/Todo` est une application complète qui exerce toutes ces capacités.

### Personnaliser l'exécutable

Préparez `config/config.json` et `config/icon.ico` :

```json
{
  "name": "Gestion Inventaire",
  "executableName": "GestionInventaire.exe",
  "windowTitle": "Gestion Inventaire — Édition 2026",
  "version": "2.4.1",
  "company": "Atelier Kevin",
  "window": { "width": 1280, "height": 800, "resizable": true }
}
```

puis lancez :

```bash
Proton.exe /config
```

Vous obtenez `GestionInventaire.exe` : même moteur, avec son nom, son icône, ses
métadonnées Windows **et votre application embarquée**. `Proton.exe` reste intact.

Distribuez ce fichier, et rien d'autre. `data` se crée au premier démarrage.

Pour livrer un contenu initial — modèles, catalogue, base pré-remplie — ajoutez le
paramètre `data` :

```bash
Proton.exe /config data
```

Il embarque aussi `data/`, déposé au premier démarrage si ce dossier
n'existe pas encore.

---

## État

Les sept phases prévues sont implémentées. Le moteur démarre, sert son application,
expose ses API et sait produire des exécutables personnalisés.

| Phase | Contenu |
| --- | --- |
| 1–2 | Fenêtre, WebView2, Kestrel, port automatique, initialisation |
| 3 | API de fichiers, dossiers, `/api/app` |
| 4 | API SQLite — requêtes, écritures, transactions ; bases isolées dans `data/db` |
| 5 | Journal de diagnostic, traitement uniforme des erreurs |
| 6 | Mode `/config` — icône, métadonnées, configuration embarquée |
| 7 | Publication self-contained, fichier unique, compressé |

**128 tests automatisés.** Les critères d'acceptation de la V1 sont récapitulés en
fin d'analyse fonctionnelle.

Ce qui reste hors périmètre est énuméré en §60, et les simplifications assumées dans
[docs/02-perimetre-v1.md](docs/02-perimetre-v1.md).

---

## Compiler

```bash
dotnet publish src/Proton/Proton.csproj -c Release
```

L'exécutable est produit dans **`C:\proton\dist\Proton.exe`**, et les artefacts
intermédiaires dans `C:\proton\build`.

Les sorties ne restent volontairement pas dans le dépôt : une publication
self-contained pèse une soixantaine de mégaoctets, ce qui encombre l'arbre de travail
et devient franchement gênant lorsque le dossier est synchronisé par un service de
stockage en ligne. La racine de sortie se change par la variable d'environnement
`PROTON_OUTPUT_ROOT`.

```bash
dotnet test
```

---

## Documents

| Document | Contenu |
| --- | --- |
| [Analyse fonctionnelle](Proton%20-%20Analyse%20fonctionnelle.md) | La spécification : ce que Proton doit faire, et ses critères d'acceptation |
| [docs/](docs/) | Notes techniques — les mécanismes établis expérimentalement |
| [prototypes/](prototypes/) | Les prototypes qui ont tranché ces questions, avec leurs mesures |
| [samples/](samples/) | Applications d'exemple |

L'analyse décrit le **quoi**. Les notes de `docs/` décrivent le **comment**, uniquement
lorsqu'il a demandé une vérification. Les deux se renvoient l'un à l'autre plutôt que
de se répéter.

---

## Technologies

C# / .NET 10 · WebView2 · Kestrel · SQLite (`Microsoft.Data.Sqlite`) · publication
self-contained en fichier unique.

Le diagnostic est écrit dans `%LOCALAPPDATA%\Proton\logs\proton.log`.

---

## Licence

[MIT](LICENSE) — Copyright (c) 2026 Kevin Belanger.

Les applications construites avec Proton vous appartiennent : vous pouvez les
distribuer comme vous l'entendez, y compris commercialement et sans publier vos
sources. Le moteur étant embarqué dans chaque exécutable produit, son attribution y
est inscrite automatiquement — dans les propriétés du fichier et sur `/api/app`.
Vous n'avez rien à faire.
