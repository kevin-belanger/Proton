# Proton

Moteur Windows autonome permettant d'exécuter une application HTML / CSS / JavaScript
comme une application de bureau.

Proton démarre un serveur local, sert l'application depuis un dossier `app`, et lui
donne accès à des capacités normalement hors de portée d'une page Web : lecture et
écriture de fichiers, bases SQLite locales, et à terme d'autres fonctions natives.

Une application Proton se distribue par simple copie :

```text
MonApplication/
├── MonApplication.exe
├── app/          l'application Web
└── data/         ses fichiers et bases de données
```

Ni installateur, ni serveur, ni runtime à installer séparément.

---

## État

**En cours de conception.** Aucune implémentation n'existe encore ; le risque
technique principal — la génération d'exécutables personnalisés — a été levé par un
prototype.

---

## Documents

| Document | Contenu |
| --- | --- |
| [Analyse fonctionnelle](Proton%20-%20Analyse%20fonctionnelle.md) | La spécification : ce que Proton doit faire, et les critères d'acceptation de la V1 |
| [docs/](docs/) | Notes techniques — les mécanismes établis expérimentalement |
| [prototypes/](prototypes/) | Les prototypes qui ont tranché ces questions, avec leurs mesures |

L'analyse décrit le **quoi**. Les notes de `docs/` décrivent le **comment**, uniquement
lorsqu'il a demandé une vérification. Les deux se renvoient l'un à l'autre plutôt que
de se répéter.

---

## Technologies visées

C# / .NET 10 · WebView2 · Kestrel · SQLite (`Microsoft.Data.Sqlite`) · publication
self-contained single-file.

---

## Licence

À déterminer.
