# Proton — Analyse fonctionnelle

**Nom de code :** Proton
**Version du document :** 0.2
**Cible fonctionnelle :** Version 1
**Plateforme :** Windows
**Technologie privilégiée :** C# / .NET 10
**Objet :** Spécification fonctionnelle et base de travail pour l'implémentation par Claude Code

Ce document décrit **ce que Proton doit faire**. Les mécanismes retenus pour y parvenir,
lorsqu'ils ont demandé une vérification expérimentale, sont consignés séparément dans
`docs/`. Les prototypes qui les ont établis se trouvent dans `prototypes/`.

**Révisions :**

* 0.2 — questions de la §67 relatives à la personnalisation de l'exécutable tranchées
  par le prototype `config-pe` ; §39 et phase 7 précisées en conséquence.
* 0.1 — version initiale.

---

# 1. Présentation du projet

## 1.1 Vision

Proton est un moteur permettant de créer des applications de bureau Windows à partir de technologies Web classiques :

* HTML;
* CSS;
* JavaScript.

Le principe est de permettre à un développeur de construire l'interface et la logique principale d'une application comme s'il développait une application Web locale, tout en bénéficiant de certaines capacités normalement indisponibles à une page Web ordinaire, notamment :

* lecture et écriture de fichiers locaux;
* gestion de données persistantes;
* accès à des bases de données SQLite;
* possibilité future d'accéder à différentes fonctions natives de Windows.

Proton agit donc comme une couche d'exécution native située entre l'application Web et le système d'exploitation.

Le développeur d'une application Proton ne devrait normalement pas avoir à modifier ou recompiler le code source de Proton.

---

# 2. Objectif principal de la V1

La V1 doit permettre de distribuer une application sous une forme extrêmement simple.

Une application Proton typique doit pouvoir être distribuée ainsi :

```text
MonApplication/
│
├── MonApplication.exe
├── app/
│   ├── index.html
│   ├── css/
│   ├── js/
│   └── ...
│
└── data/
    └── ...
```

Le dossier `data` peut être absent lors de la distribution s'il ne contient encore aucune donnée.

Lorsqu'il est exécuté, `MonApplication.exe` doit :

1. déterminer son propre emplacement;
2. créer les dossiers requis s'ils n'existent pas;
3. démarrer un serveur HTTP local;
4. sélectionner automatiquement un port TCP disponible;
5. exposer le contenu du dossier `app`;
6. exposer les API de Proton;
7. créer une fenêtre Windows contenant WebView2;
8. charger l'application Web depuis le serveur local;
9. arrêter proprement le serveur lorsque l'application est fermée.

---

# 3. Principes fondamentaux

La conception de Proton doit respecter les principes suivants.

## 3.1 Simplicité de distribution

Une application Proton doit pouvoir être distribuée principalement par copie de fichiers.

Il ne doit pas être nécessaire d'installer :

* IIS;
* Apache;
* Node.js;
* PHP;
* Python;
* un serveur SQLite;
* le runtime .NET séparément;
* un service Windows.

Aucun programme d'installation ne doit être obligatoire.

---

## 3.2 Autonomie maximale

Proton doit être publié comme application .NET autonome.

Le runtime .NET et les dépendances propres à Proton doivent être intégrés autant que possible à la distribution.

L'objectif est qu'un utilisateur puisse copier l'application sur un ordinateur Windows récent et démarrer directement l'exécutable.

L'autonomie absolue ne doit toutefois pas conduire à des solutions déraisonnablement complexes ou volumineuses.

WebView2 constitue notamment un cas particulier. Proton devra utiliser une stratégie permettant de fonctionner sur la très grande majorité des ordinateurs Windows récents, tout en étudiant la possibilité d'embarquer ou d'extraire un runtime WebView2 lorsque cela est pertinent.

Les cas extrêmement anciens ou atypiques ne constituent pas une cible prioritaire de la V1.

---

## 3.3 Portabilité de l'application

Les chemins utilisés par Proton doivent être déterminés relativement à l'emplacement réel de l'exécutable et non au répertoire de travail courant du processus.

Une application complète doit donc pouvoir être déplacée d'un dossier à un autre sans modification.

---

# 4. Plateformes ciblées

## 4.1 V1

La V1 cible exclusivement Windows.

La cible principale est Windows x64 récent.

Windows 11 constitue la plateforme principale.

Le fonctionnement sur les versions encore raisonnablement compatibles de Windows 10 est souhaitable, sans qu'il soit nécessaire de supporter des environnements Windows obsolètes.

---

## 4.2 macOS

macOS n'est pas dans la portée de la V1.

Un port macOS pourra éventuellement être créé plus tard.

Il sera considéré comme une adaptation distincte du moteur Proton et non comme une exigence de compatibilité de la V1 Windows.

---

# 5. Technologies recommandées

L'implémentation privilégiée est :

```text
Langage              C#
Plateforme            .NET 10
Interface native      WinForms ou solution Windows légère équivalente
Navigateur embarqué   Microsoft Edge WebView2
Serveur HTTP          ASP.NET Core / Kestrel
SQLite                Microsoft.Data.Sqlite
Format de config      JSON
```

WinForms est recommandé pour la V1 en raison de sa simplicité : Proton n'a besoin que d'une fenêtre native contenant essentiellement une WebView.

L'architecture ne doit cependant pas coupler inutilement le moteur HTTP, la gestion des données et la fenêtre graphique.

---

# 6. Structure d'une application Proton

Trois dossiers possèdent une signification particulière.

```text
Executable.exe
│
├── app/
├── data/
└── config/
```

Ils doivent toujours être recherchés relativement au dossier contenant l'exécutable.

---

# 7. Dossier `app`

Le dossier :

```text
app/
```

contient l'application Web.

Il peut contenir n'importe quelle structure valide d'application Web statique :

```text
app/
├── index.html
├── css/
│   └── style.css
├── js/
│   └── app.js
├── images/
├── modules/
└── ...
```

Le contenu de `app` doit être exposé directement à la racine du serveur HTTP.

Ainsi :

```text
app/index.html
```

correspond à :

```text
/
```

et :

```text
app/css/style.css
```

correspond à :

```text
/css/style.css
```

L'application Web ne doit donc pas avoir besoin de connaître le chemin physique de son dossier.

---

# 8. Initialisation automatique

Lors du démarrage normal, Proton doit vérifier l'existence de :

```text
app/
data/
```

Si `app` n'existe pas, Proton doit :

1. créer le dossier;
2. créer automatiquement un fichier `index.html`;
3. y placer une application minimale de type Hello World.

Exemple conceptuel :

```html
<!doctype html>
<html>
<head>
    <meta charset="utf-8">
    <title>Proton</title>
</head>
<body>
    <h1>Hello World</h1>
</body>
</html>
```

Le contenu exact pourra être légèrement différent.

Si `data` n'existe pas, Proton doit simplement le créer.

Proton ne doit jamais écraser automatiquement un fichier utilisateur déjà existant.

Le dossier `config` ne doit pas être créé automatiquement lors d'un démarrage normal.

---

# 9. Serveur HTTP local

## 9.1 Serveur

La V1 doit utiliser Kestrel comme serveur HTTP embarqué.

Il doit être lancé directement dans le processus Proton.

Aucun processus serveur externe ne doit être nécessaire.

---

## 9.2 Port

Aucun port fixe ne doit être imposé.

Proton doit demander au système un port TCP disponible au démarrage.

Le port peut donc être différent à chaque exécution.

Conceptuellement :

```text
http://127.0.0.1:<port>/
```

Exemple :

```text
http://127.0.0.1:48723/
```

La fenêtre WebView doit recevoir automatiquement cette adresse.

L'application Web elle-même ne doit jamais avoir besoin de connaître le numéro de port à l'avance.

Elle doit utiliser des URL relatives :

```javascript
fetch('/data/settings.json')
```

ou :

```javascript
fetch('/api/sqlite/app.db/query', ...)
```

---

# 10. Restriction du serveur au poste local

Le serveur Proton ne doit pas devenir un serveur accessible depuis le réseau.

Il doit écouter exclusivement sur l'interface de boucle locale.

Par exemple :

```text
127.0.0.1
```

et non :

```text
0.0.0.0
```

Une autre machine du réseau ne doit pas pouvoir accéder à l'application Proton.

HTTPS n'est pas requis pour la V1 puisque les communications sont strictement locales au processus et à la machine.

---

# 11. Fenêtre principale

Au démarrage normal, Proton doit afficher une fenêtre Windows contenant une WebView2.

La WebView doit charger :

```text
http://127.0.0.1:<port>/
```

La fenêtre ne doit pas afficher les éléments d'un navigateur classique :

* barre d'adresse;
* boutons précédent/suivant;
* onglets;
* menus Edge.

L'utilisateur doit percevoir l'ensemble comme une application native.

---

# 12. Fermeture de l'application

Lorsque la fenêtre principale est fermée :

1. la WebView doit être libérée;
2. le serveur Kestrel doit être arrêté proprement;
3. les ressources SQLite en cours doivent être libérées;
4. le processus Proton doit se terminer.

Il ne doit rester aucun serveur Proton actif en arrière-plan.

---

# 13. API `data`

Le dossier :

```text
data/
```

constitue l'espace de stockage de fichiers accessible à l'application Web.

Il ne doit pas simplement être exposé comme répertoire statique en lecture seule.

Proton doit fournir une interface HTTP permettant :

* de lire;
* de créer;
* de remplacer;
* de supprimer;
* de lister

les fichiers de `data`.

La route principale est :

```text
/data/
```

---

# 14. Isolation du dossier `data`

Toutes les opérations réalisées par `/data` doivent obligatoirement rester à l'intérieur du dossier physique :

```text
<application>/data/
```

Une requête ne doit jamais pouvoir accéder à :

```text
../
```

ou à un chemin absolu externe.

Les protections doivent inclure notamment :

* normalisation des chemins;
* rejet des séquences de traversée;
* rejet des chemins absolus;
* vérification finale que le chemin résolu appartient bien à `data`;
* protection contre les liens symboliques ou points de réanalyse permettant de sortir du dossier.

Par exemple, une requête ressemblant à :

```text
/data/../../Windows/System32/config
```

doit être refusée.

---

# 15. Lecture d'un fichier

Exemple :

```http
GET /data/settings.json
```

Si le fichier existe, Proton retourne directement son contenu.

Exemple :

```http
HTTP/1.1 200 OK
Content-Type: application/json
ETag: "sha256-..."
```

Le type MIME doit être déterminé lorsque cela est raisonnablement possible.

Le contenu reste le contenu original du fichier.

Il n'est pas nécessaire d'envelopper le fichier dans une structure JSON simplement pour transmettre son hash.

---

# 16. Hash et contrôle de concurrence

Chaque lecture d'un fichier doit calculer une empreinte permettant d'identifier précisément la version du fichier.

La V1 utilisera :

```text
SHA-256
```

L'empreinte sera retournée sous forme d'un `ETag` HTTP fort.

Exemple conceptuel :

```http
ETag: "sha256-a847...91c"
```

Cela permettra à l'application Web de savoir exactement quelle version du fichier elle vient de lire.

---

# 17. Écriture conditionnelle d'un fichier

Une application peut remplacer ou créer un fichier avec :

```http
PUT /data/settings.json
```

Le corps HTTP contient directement le nouveau contenu.

Une application peut effectuer une écriture inconditionnelle :

```http
PUT /data/settings.json
```

ou transmettre la version qu'elle pense modifier :

```http
PUT /data/settings.json
If-Match: "sha256-a847...91c"
```

Lorsque `If-Match` est présent, Proton doit :

1. calculer le hash actuel du fichier;
2. comparer ce hash à celui fourni;
3. effectuer l'écriture uniquement si les deux correspondent.

Si les valeurs diffèrent, Proton ne doit pas toucher au fichier.

La réponse doit être :

```http
412 Precondition Failed
```

Cela constitue un mécanisme de concurrence optimiste.

---

# 18. Exemple de concurrence

Application A lit :

```text
settings.json
ETag = ABC
```

Application B modifie ensuite le même fichier.

Le fichier possède maintenant :

```text
ETag = XYZ
```

Application A tente :

```http
PUT /data/settings.json
If-Match: ABC
```

Proton détecte :

```text
ABC != XYZ
```

L'écriture est rejetée.

Ainsi, l'application A ne peut pas écraser silencieusement les modifications de B.

---

# 19. Création et remplacement d'un fichier

Pour :

```http
PUT /data/document.txt
```

si le fichier n'existe pas :

```http
201 Created
```

doit être retourné.

Si le fichier existe et est remplacé :

```http
204 No Content
```

peut être retourné.

Le nouvel `ETag` devrait être retourné dans les en-têtes de la réponse lorsqu'il est disponible.

---

# 20. Suppression d'un fichier

Exemple :

```http
DELETE /data/document.txt
```

Sans `If-Match`, la suppression est inconditionnelle.

Avec :

```http
DELETE /data/document.txt
If-Match: "sha256-..."
```

Proton doit vérifier la version avant la suppression.

Si le fichier a changé :

```http
412 Precondition Failed
```

doit être retourné.

Une suppression réussie peut retourner :

```http
204 No Content
```

---

# 21. Liste des fichiers

Une requête sur un dossier doit permettre d'obtenir son contenu.

Exemple :

```http
GET /data/
```

ou :

```http
GET /data/documents/
```

La réponse doit être JSON.

Exemple conceptuel :

```json
{
  "path": "documents",
  "entries": [
    {
      "name": "rapport.pdf",
      "type": "file",
      "size": 42817,
      "lastModified": "2026-08-24T14:32:00Z"
    },
    {
      "name": "archives",
      "type": "directory"
    }
  ]
}
```

Le calcul systématique du SHA-256 de tous les fichiers d'un répertoire n'est pas requis lors d'un simple listing.

Le hash doit être calculé lorsqu'un fichier est réellement lu ou lorsqu'une opération conditionnelle le nécessite.

---

# 22. Dossiers dans `data`

La V1 devrait permettre au minimum :

* de lister un dossier;
* de créer les dossiers parents nécessaires lors de la création d'un fichier;
* de supprimer un dossier vide.

Une extension permettant explicitement la création et la suppression récursive de dossiers peut être ajoutée si elle demeure simple et sûre.

Aucune suppression récursive implicite ne doit être effectuée sans requête explicite.

---

# 23. Codes HTTP

L'API doit employer les codes HTTP standards.

Exemples :

| Code  | Signification                                       |
| ----- | --------------------------------------------------- |
| `200` | Lecture ou opération réussie                        |
| `201` | Ressource créée                                     |
| `204` | Opération réussie sans contenu                      |
| `400` | Requête invalide                                    |
| `403` | Accès interdit                                      |
| `404` | Ressource introuvable                               |
| `409` | Conflit de ressource                                |
| `412` | Précondition `If-Match` non satisfaite              |
| `413` | Contenu trop volumineux si une limite est appliquée |
| `422` | Requête valide mais impossible à traiter            |
| `500` | Erreur interne                                      |
| `503` | Ressource temporairement indisponible               |

---

# 24. Format uniforme des erreurs

Lorsqu'une API retourne une erreur et que la réponse peut être JSON, Proton doit utiliser un format uniforme.

Exemple :

```json
{
  "error": {
    "code": "file_version_mismatch",
    "message": "The file has been modified since it was read."
  }
}
```

Des détails supplémentaires peuvent être ajoutés :

```json
{
  "error": {
    "code": "file_version_mismatch",
    "message": "The file has been modified since it was read.",
    "details": {
      "path": "settings.json"
    }
  }
}
```

Les codes internes doivent être stables afin qu'une application JavaScript puisse les interpréter sans analyser le texte humain du message.

---

# 25. API SQLite

Une autre responsabilité importante de Proton est de fournir une couche d'accès à SQLite.

L'application Web ne peut évidemment pas ouvrir directement un fichier SQLite local.

Proton doit donc agir comme intermédiaire.

Conceptuellement :

```text
JavaScript
    ↓ HTTP
Proton
    ↓
Microsoft.Data.Sqlite
    ↓
data/application.db
```

---

# 26. Emplacement des bases SQLite

Toutes les bases SQLite gérées par Proton doivent être situées dans `data`.

Exemple :

```text
data/
└── application.db
```

Une application ne doit pas pouvoir utiliser l'API SQLite pour ouvrir arbitrairement :

```text
C:\...
```

ou :

```text
..\...
```

Les mêmes règles de confinement que l'API de fichiers doivent s'appliquer.

---

# 27. Routes SQLite proposées

La convention suivante est recommandée :

```text
POST /api/sqlite/{database}/query
POST /api/sqlite/{database}/execute
POST /api/sqlite/{database}/transaction
```

Exemple :

```text
POST /api/sqlite/application.db/query
```

L'architecture interne doit toutefois isoler la logique SQLite de la logique HTTP afin que ces routes puissent évoluer sans modifier le cœur du moteur.

---

# 28. Requêtes SQLite

Une requête de lecture peut être envoyée ainsi :

```http
POST /api/sqlite/application.db/query
Content-Type: application/json
```

```json
{
  "sql": "SELECT id, name FROM users WHERE active = $active",
  "parameters": {
    "$active": 1
  }
}
```

Les paramètres SQL doivent être supportés.

L'application Web ne devrait pas être obligée de concaténer les données directement dans les chaînes SQL.

---

# 29. Réponse d'une requête SQLite

Exemple conceptuel :

```json
{
  "columns": [
    "id",
    "name"
  ],
  "rows": [
    [1, "Alice"],
    [2, "Bob"]
  ]
}
```

L'utilisation d'un tableau `columns` séparé des lignes évite les ambiguïtés liées aux noms de colonnes en double.

Les valeurs SQLite doivent être sérialisées de façon prévisible en JSON.

Les valeurs `NULL` deviennent :

```json
null
```

Les BLOB doivent être représentés de façon non ambiguë, par exemple en Base64.

---

# 30. Exécution SQL

Pour des commandes telles que :

```sql
INSERT
UPDATE
DELETE
CREATE TABLE
ALTER TABLE
```

l'application peut utiliser :

```text
POST /api/sqlite/application.db/execute
```

Exemple :

```json
{
  "sql": "INSERT INTO users(name) VALUES($name)",
  "parameters": {
    "$name": "Alice"
  }
}
```

Réponse conceptuelle :

```json
{
  "rowsAffected": 1,
  "lastInsertRowId": 42
}
```

---

# 31. Création d'une base SQLite

Une base inexistante peut être créée automatiquement lorsqu'une commande d'écriture ou de création de structure la nécessite.

Par exemple :

```text
POST /api/sqlite/application.db/execute
```

avec :

```sql
CREATE TABLE users (...)
```

peut automatiquement créer :

```text
data/application.db
```

si le fichier n'existe pas.

Une simple requête de lecture sur une base inexistante devrait plutôt retourner `404` afin de ne pas créer accidentellement une base vide.

---

# 32. Transactions SQLite

Proton doit permettre l'exécution atomique de plusieurs commandes.

Exemple :

```http
POST /api/sqlite/application.db/transaction
```

```json
{
  "commands": [
    {
      "sql": "UPDATE accounts SET balance = balance - $amount WHERE id = $id",
      "parameters": {
        "$amount": 100,
        "$id": 1
      }
    },
    {
      "sql": "UPDATE accounts SET balance = balance + $amount WHERE id = $id",
      "parameters": {
        "$amount": 100,
        "$id": 2
      }
    }
  ]
}
```

Toutes les commandes doivent être exécutées dans une seule transaction SQLite.

Si une commande échoue :

```text
ROLLBACK
```

doit être effectué.

Aucune commande de la transaction ne doit demeurer appliquée partiellement.

---

# 33. Concurrence SQLite

L'implémentation doit tenir compte du fait que plusieurs requêtes HTTP peuvent arriver simultanément.

Les connexions SQLite ne doivent pas être partagées de manière non sécuritaire entre plusieurs opérations concurrentes.

Une connexion appropriée doit être créée ou empruntée pour chaque opération.

L'utilisation du mode WAL peut être envisagée afin d'améliorer la coexistence entre lectures et écritures.

Les situations `busy` ou `locked` doivent être gérées proprement et ne doivent pas provoquer l'arrêt de Proton.

---

# 34. Sécurité de l'API SQLite

Même si Proton accepte volontairement du SQL provenant de son application Web, l'API ne doit pas permettre au SQL de contourner l'isolation du dossier `data`.

Les fonctionnalités SQLite permettant d'ouvrir ou d'écrire arbitrairement d'autres fichiers doivent être évaluées et, lorsque nécessaire, bloquées.

Cela concerne notamment les mécanismes pouvant permettre :

* l'attachement arbitraire d'une base située ailleurs;
* le chargement d'extensions natives;
* l'écriture d'une base vers un chemin extérieur à `data`.

Le principe à respecter est :

> Une application Proton V1 peut gérer librement ses propres fichiers et bases de données dans `data`, mais pas le reste du système de fichiers par l'intermédiaire de l'API Proton.

Les API donnant volontairement accès à davantage de ressources système feront partie d'une version ultérieure.

---

# 35. Mode de personnalisation `/config`

Proton doit posséder deux modes d'exécution principaux.

Mode normal :

```text
Proton.exe
```

Mode de personnalisation :

```text
Proton.exe /config
```

L'alias :

```text
Proton.exe --config
```

peut également être accepté, mais `/config` constitue la syntaxe principale prévue.

---

# 36. Rôle du dossier `config`

Le dossier :

```text
config/
```

n'est pas un dossier utilisé par l'application finale.

Il sert uniquement à fabriquer une version personnalisée de l'exécutable Proton.

Exemple :

```text
Proton.exe

config/
├── config.json
└── icon.ico
```

Le développeur :

1. prépare `config.json`;
2. place `icon.ico`;
3. lance `Proton.exe /config`;
4. obtient un nouvel exécutable personnalisé.

---

# 37. Principe du générateur

L'exécutable exécuté avec `/config` ne doit pas se modifier lui-même.

Il doit :

1. lire la configuration;
2. créer une copie de son propre moteur;
3. personnaliser cette copie;
4. produire un nouvel `.exe`.

Par exemple :

```text
Proton.exe
      ↓ /config
GestionInventaire.exe
```

`Proton.exe` demeure intact.

---

# 38. Exécutable enfant

L'exécutable généré doit contenir exactement le même moteur Proton.

La personnalisation ne doit pas supprimer les capacités internes de Proton.

Par conséquent :

```text
GestionInventaire.exe /config
```

doit lui-même être capable de générer un autre exécutable personnalisé.

Il n'existe donc pas réellement deux moteurs différents, « générateur » et « application ».

Chaque exécutable Proton possède le moteur complet et peut fonctionner :

* normalement;
* ou en mode `/config`.

---

# 39. Configuration embarquée

L'exécutable généré doit conserver ses paramètres de personnalisation sans dépendre du dossier `config`.

Après génération, le dossier :

```text
config/
```

doit pouvoir être supprimé complètement.

L'application personnalisée doit continuer à connaître :

* son nom;
* son titre;
* son icône;
* ses autres paramètres embarqués éventuels.

La configuration doit donc être intégrée dans l'exécutable généré d'une manière appropriée.

La méthode a été arrêtée : la configuration est annexée **en fin de fichier**, après le
bundle .NET, et non stockée comme ressource PE. Le format et les raisons de ce choix
figurent dans `docs/01-personnalisation-executable.md`.

---

# 40. `config.json`

La V1 peut utiliser une structure telle que :

```json
{
  "name": "Gestion Inventaire",
  "executableName": "GestionInventaire.exe",
  "windowTitle": "Gestion Inventaire",
  "window": {
    "width": 1280,
    "height": 800,
    "resizable": true
  }
}
```

Les propriétés minimales réellement requises sont :

```text
name
executableName
```

`windowTitle` peut être optionnel et prendre `name` comme valeur par défaut.

Les paramètres de taille de fenêtre peuvent également être optionnels.

---

# 41. Icône

Le fichier d'icône doit utiliser un nom prédéterminé :

```text
config/icon.ico
```

Il doit s'agir d'un fichier ICO Windows valide.

Le générateur doit intégrer cette icône dans le nouvel exécutable de manière à ce qu'elle soit visible notamment :

* dans l'Explorateur Windows;
* dans la barre des tâches;
* dans la fenêtre;
* dans les propriétés pertinentes du fichier.

---

# 42. Métadonnées de l'exécutable

Lorsque cela est techniquement raisonnable, le générateur devrait également adapter les métadonnées Windows pertinentes :

* Product Name;
* File Description;
* nom d'application;
* éventuellement version;
* éventuellement éditeur.

Ces propriétés ne doivent toutefois pas empêcher la réalisation de la V1 minimale.

---

# 43. Génération atomique

Le mode `/config` ne doit pas risquer de produire un exécutable partiellement écrit.

Une stratégie recommandée est :

```text
1. copier vers un fichier temporaire;
2. effectuer les modifications;
3. valider le résultat;
4. déplacer/remplacer atomiquement le fichier final.
```

L'exécutable source ne doit jamais être modifié.

---

# 44. Régénération

Le mode `/config` doit être réexécutable.

Si la configuration change :

```text
config/config.json
config/icon.ico
```

le développeur doit pouvoir relancer :

```text
Proton.exe /config
```

pour générer à nouveau l'application.

Le comportement de remplacement du fichier cible doit être prévisible et documenté.

La V1 peut remplacer automatiquement un exécutable généré précédemment si celui-ci n'est pas en cours d'utilisation.

---

# 45. Signature Authenticode

La signature Authenticode d'applications générées ne fait pas partie des fonctionnalités obligatoires de la V1.

Une signature éventuelle doit toujours être appliquée après toute personnalisation de l'exécutable.

Le futur mécanisme de signature ne doit jamais nécessiter de stocker une clé privée dans :

```text
config.json
```

ou dans le dépôt Git.

Une version ultérieure pourra permettre d'utiliser :

* un certificat présent dans le magasin de certificats Windows;
* un certificat fourni par l'éditeur;
* un service de signature;
* `signtool` ou un mécanisme équivalent.

Si un exécutable source signé est modifié pour produire un enfant, l'enfant doit être considéré comme non signé jusqu'à ce qu'une nouvelle signature valide lui soit appliquée.

---

# 46. Distribution finale d'une application

Le dossier `config` est un outil de développement.

Il ne fait pas partie de l'application distribuée.

Exemple de dossier de développement :

```text
Projet/
├── Proton.exe
├── app/
├── data/
└── config/
```

Après personnalisation :

```text
Projet/
├── GestionInventaire.exe
├── app/
├── data/
└── config/
```

Distribution finale :

```text
GestionInventaire/
├── GestionInventaire.exe
├── app/
└── data/
```

Le dossier `data` peut être omis s'il est vide puisqu'il sera créé automatiquement.

---

# 47. Séparation des responsabilités internes

Même si Proton peut être relativement petit, son code doit être modulaire.

Les responsabilités suivantes doivent être séparées :

```text
Bootstrap / démarrage
Serveur Kestrel
Service de fichiers
Service SQLite
Fenêtre native
WebView2
Configuration embarquée
Générateur / personnalisation d'exécutable
Gestion des erreurs
```

La logique métier de l'API ne doit pas être placée directement dans le code de la fenêtre.

La fenêtre ne doit pas connaître les détails de SQLite.

Le moteur SQLite ne doit pas connaître les détails de WebView2.

---

# 48. Architecture conceptuelle

```text
┌─────────────────────────────────────────┐
│              Proton.exe                 │
│                                         │
│  ┌───────────────────────────────────┐  │
│  │       Fenêtre Windows             │  │
│  │                                   │  │
│  │          WebView2                 │  │
│  └────────────────┬──────────────────┘  │
│                   │ HTTP                │
│  ┌────────────────▼──────────────────┐  │
│  │             Kestrel               │  │
│  │                                   │  │
│  │  /              → app/            │  │
│  │  /data/...      → File API        │  │
│  │  /api/sqlite/...→ SQLite API      │  │
│  └───────────────┬───────────────────┘  │
│                  │                      │
│          ┌───────▼────────┐             │
│          │     data/      │             │
│          │                │             │
│          │ fichiers       │             │
│          │ SQLite         │             │
│          └────────────────┘             │
└─────────────────────────────────────────┘
```

---

# 49. Priorité des routes

Les routes internes Proton doivent avoir priorité sur les fichiers statiques de `app`.

Les espaces suivants sont réservés :

```text
/data
/api
```

Par conséquent, un fichier :

```text
app/data/test.html
```

ne doit pas prendre le contrôle de :

```text
/data/test.html
```

Cette route appartient à l'API Proton.

---

# 50. Origine commune

Une caractéristique importante de l'architecture est que l'application Web et les API Proton sont servies par la même origine.

Exemple :

```text
http://127.0.0.1:48723/
http://127.0.0.1:48723/data/settings.json
http://127.0.0.1:48723/api/sqlite/app.db/query
```

Cela permet au JavaScript d'utiliser simplement :

```javascript
fetch('/data/settings.json')
```

sans connaître le port et sans configuration CORS particulière.

---

# 51. Navigation WebView

La WebView principale est destinée à l'application locale.

Une navigation vers une origine externe ne devrait pas remplacer silencieusement l'application Proton.

Par défaut, une URL externe telle que :

```text
https://example.com
```

devrait idéalement être ouverte dans le navigateur Windows par défaut ou être explicitement interceptée.

Les URL appartenant à l'origine locale Proton demeurent dans la WebView.

---

# 52. Protection contre l'accès depuis d'autres pages Web

Même si le serveur n'écoute que sur `127.0.0.1`, les API Proton doivent être conçues en tenant compte du fait qu'un navigateur externe présent sur la même machine pourrait tenter de communiquer avec un serveur local.

Proton ne doit pas activer un CORS permissif tel que :

```text
Access-Control-Allow-Origin: *
```

Les API sont prévues pour être utilisées par l'application servie par Proton elle-même.

Les requêtes provenant d'origines étrangères doivent être rejetées lorsque la politique HTTP applicable permet cette vérification.

---

# 53. Application Web indépendante du moteur

Le code situé dans `app` doit demeurer aussi standard que possible.

Une application Proton devrait pouvoir être développée avec des outils Web ordinaires.

Par exemple :

```javascript
const response = await fetch('/data/settings.json');
```

ou :

```javascript
await fetch('/api/sqlite/app.db/query', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json'
    },
    body: JSON.stringify({
        sql: 'SELECT * FROM users'
    })
});
```

Aucun SDK JavaScript Proton obligatoire n'est requis pour la V1.

Une bibliothèque JavaScript facilitant ces appels pourra toutefois être ajoutée ultérieurement.

---

# 54. Gestion des erreurs au démarrage

Une erreur critique ne doit pas simplement provoquer la disparition du processus.

Proton doit afficher une erreur compréhensible si, par exemple :

* le serveur ne peut pas démarrer;
* `app` ne peut pas être créé;
* le dossier `data` n'est pas accessible;
* WebView2 ne peut pas être initialisé;
* une configuration embarquée est invalide;
* le mode `/config` échoue.

Les détails techniques utiles au diagnostic doivent être conservés ou affichables.

---

# 55. Échec de WebView2

Si aucun environnement WebView2 utilisable n'est disponible et qu'aucune solution embarquée n'est disponible, Proton doit afficher un message clair plutôt que de planter.

Le mécanisme exact de distribution de WebView2 devra être déterminé pendant l'implémentation en privilégiant :

1. l'autonomie;
2. la compatibilité avec la majorité des Windows modernes;
3. la simplicité de distribution;
4. une taille raisonnable.

---

# 56. Journaux

La V1 n'a pas besoin d'un système complexe de journalisation.

Le moteur doit néanmoins pouvoir produire suffisamment d'information pour diagnostiquer :

* le démarrage;
* le port choisi;
* les erreurs HTTP graves;
* les erreurs SQLite;
* les erreurs WebView2;
* les erreurs de génération `/config`.

Les journaux ne doivent pas encombrer le dossier de l'application en fonctionnement normal.

Une destination située dans le profil utilisateur ou un mode de diagnostic est préférable.

---

# 57. Performance

Proton vise principalement des applications locales de petite ou moyenne taille.

La V1 n'a pas pour vocation de remplacer un serveur de bases de données multi-utilisateur.

Les opérations HTTP locales doivent néanmoins être suffisamment rapides pour qu'une application utilisant `fetch()` ait une sensation comparable à une application native.

Le hash SHA-256 ne doit être calculé que lorsqu'il est utile.

Il ne doit pas être recalculé massivement pour tout un arbre de fichiers lors d'un simple listing.

---

# 58. Taille des fichiers

L'API de fichiers doit traiter les données sous forme de flux lorsque cela est approprié.

Un fichier volumineux ne doit pas nécessairement être chargé intégralement plusieurs fois en mémoire.

De même, le calcul SHA-256 devrait pouvoir être effectué par flux.

---

# 59. Concurrence sur les fichiers

Le système `ETag` / `If-Match` protège les applications contre les modifications concurrentes involontaires.

L'écriture finale devrait également être effectuée de manière aussi atomique que possible.

Une stratégie adaptée consiste à :

```text
écrire temporairement
→ fermer le flux
→ remplacer le fichier cible
```

Cela réduit le risque qu'un arrêt au milieu de l'écriture laisse un fichier partiellement écrit.

---

# 60. Fonctionnalités explicitement hors V1

La V1 ne doit pas devenir un projet trop large.

Les fonctionnalités suivantes sont volontairement reportées :

* accès générique au registre Windows;
* exécution arbitraire de commandes système;
* accès natif complet au système de fichiers;
* gestion des imprimantes;
* accès aux périphériques;
* notifications Windows avancées;
* presse-papiers natif par API Proton;
* gestion native de fenêtres multiples;
* services Windows;
* intégration système poussée;
* mise à jour automatique;
* signature Authenticode intégrée;
* macOS;
* Linux;
* magasin de plugins;
* système de permissions complexe.

---

# 61. Vision V2

Une V2 pourra enrichir Proton avec des API natives.

Conceptuellement :

```text
/api/system/...
/api/filesystem/...
/api/clipboard/...
/api/process/...
/api/printer/...
```

Ces fonctions ne doivent pas être implémentées prématurément.

L'architecture V1 doit simplement permettre d'ajouter de nouveaux modules HTTP sans réécrire le cœur du moteur.

---

# 62. Cycle de vie pour un développeur Proton

## Étape 1 — Obtenir Proton

Le développeur télécharge :

```text
Proton.exe
```

depuis GitHub.

---

## Étape 2 — Premier lancement

Il exécute :

```text
Proton.exe
```

Proton crée :

```text
app/
data/
```

et un exemple Hello World.

---

## Étape 3 — Développer

Le développeur remplace le contenu de :

```text
app/
```

par son application HTML/CSS/JavaScript.

Il peut utiliser :

```text
/data
```

pour les fichiers persistants et :

```text
/api/sqlite
```

pour les bases SQLite.

---

## Étape 4 — Personnaliser

Le développeur crée :

```text
config/config.json
config/icon.ico
```

puis exécute :

```text
Proton.exe /config
```

---

## Étape 5 — Générer

Proton produit :

```text
MonApplication.exe
```

avec le nom, les métadonnées et l'icône appropriés.

---

## Étape 6 — Distribuer

Le développeur distribue :

```text
MonApplication.exe
app/
data/
```

Le dossier `config` n'est pas distribué.

---

# 63. Publication du projet Proton

Le projet Proton lui-même doit être publié sur GitHub.

Le dépôt doit contenir au minimum :

```text
README.md
LICENSE ou indication de licence à déterminer
src/
tests/
samples/
```

Les Releases GitHub doivent pouvoir fournir :

* le code source correspondant;
* l'exécutable Proton générique compilé;
* éventuellement une application d'exemple;
* les notes de version.

La compilation initiale sera réalisée sur Windows.

---

# 64. Structure de code recommandée

Une organisation possible est :

```text
src/
└── Proton/
    ├── Bootstrap/
    ├── Configuration/
    ├── Hosting/
    ├── WebView/
    ├── FileApi/
    ├── SqliteApi/
    ├── Personalization/
    ├── Security/
    └── Infrastructure/

tests/
├── Proton.Tests/
└── Proton.IntegrationTests/

samples/
└── HelloWorld/
```

Cette structure est indicative.

Le principe essentiel est la séparation des responsabilités.

---

# 65. Critères d'acceptation V1

La V1 peut être considérée fonctionnelle lorsque les scénarios suivants réussissent.

## CA-01 — Exécutable seul

Étant donné uniquement :

```text
Proton.exe
```

un double-clic doit créer :

```text
app/
data/
```

puis afficher une fenêtre contenant le Hello World.

---

## CA-02 — Application personnalisée

Si `app/index.html` contient :

```html
<h1>Test Proton</h1>
```

la fenêtre doit afficher ce contenu.

---

## CA-03 — Port automatique

Si plusieurs ports courants sont déjà utilisés, Proton doit quand même sélectionner un port disponible et démarrer.

Aucun port fixe ne doit être nécessaire.

---

## CA-04 — Isolation réseau

Le serveur doit être accessible localement depuis :

```text
127.0.0.1
```

mais pas depuis une autre machine du réseau.

---

## CA-05 — Lecture de fichier

Après création de :

```text
data/test.txt
```

un :

```http
GET /data/test.txt
```

doit retourner son contenu et un `ETag`.

---

## CA-06 — Écriture

Un :

```http
PUT /data/test.txt
```

doit pouvoir créer ou remplacer le fichier.

---

## CA-07 — Écriture conditionnelle réussie

Un `PUT` avec le bon `If-Match` doit réussir.

---

## CA-08 — Conflit

Un `PUT` avec un ancien `ETag` doit retourner :

```text
412
```

et ne doit modifier aucune donnée.

---

## CA-09 — Suppression conditionnelle

Le même comportement doit s'appliquer à `DELETE`.

---

## CA-10 — Traversée interdite

Une tentative d'accéder à :

```text
../
```

doit être rejetée.

---

## CA-11 — SQLite

L'application doit pouvoir :

1. créer une base;
2. créer une table;
3. insérer une ligne;
4. lire cette ligne;
5. modifier cette ligne;
6. supprimer cette ligne.

Le tout uniquement par HTTP depuis JavaScript.

---

## CA-12 — Transaction SQLite

Si la deuxième commande d'une transaction échoue, la première doit être annulée.

---

## CA-13 — Fermeture

Lorsque la fenêtre est fermée, le port HTTP doit être libéré et le processus doit disparaître.

---

## CA-14 — Génération

Avec :

```text
config/config.json
config/icon.ico
```

et :

```text
Proton.exe /config
```

un nouvel exécutable personnalisé doit être créé sans modifier `Proton.exe`.

---

## CA-15 — Personnalisation

Le nouvel exécutable doit utiliser :

* le nom configuré;
* le titre configuré;
* l'icône configurée.

---

## CA-16 — Indépendance du dossier config

Après génération, le dossier :

```text
config/
```

peut être supprimé.

Le nouvel exécutable doit continuer de fonctionner exactement de la même manière.

---

## CA-17 — Génération récursive

Un exécutable personnalisé doit lui-même accepter :

```text
/config
```

et être capable de créer une autre copie personnalisée.

---

## CA-18 — Aucune installation .NET

Sur une machine cible compatible ne possédant pas de runtime .NET installé séparément, l'application publiée en mode self-contained doit pouvoir démarrer.

---

# 66. Priorités d'implémentation pour Claude Code

Le développement devrait être effectué progressivement.

## Phase 1 — Shell minimal

Créer :

* projet C#;
* fenêtre Windows;
* WebView2;
* Kestrel;
* port automatique;
* chargement de `app/index.html`.

Objectif :

```text
Proton.exe
→ Kestrel
→ WebView
→ Hello World
```

---

## Phase 2 — Initialisation

Ajouter :

* détection du dossier de l'exécutable;
* création de `app`;
* création de `data`;
* génération du Hello World.

---

## Phase 3 — API fichiers

Implémenter :

```text
GET
PUT
DELETE
```

avec :

* confinement dans `data`;
* listing des dossiers;
* SHA-256;
* `ETag`;
* `If-Match`;
* codes HTTP;
* format d'erreur uniforme.

---

## Phase 4 — SQLite

Ajouter :

```text
/query
/execute
/transaction
```

avec :

* paramètres SQL;
* sérialisation JSON;
* transactions;
* gestion des erreurs;
* confinement des bases dans `data`.

---

## Phase 5 — Robustesse

Ajouter :

* gestion propre des erreurs;
* arrêt du serveur;
* sécurité des chemins;
* gestion des accès concurrents;
* tests d'intégration.

---

## Phase 6 — `/config`

Implémenter :

* lecture du JSON;
* validation de `icon.ico`;
* copie de l'exécutable;
* intégration de la configuration;
* modification de l'icône;
* métadonnées Windows;
* génération atomique.

Le procédé est déjà établi et éprouvé : voir `docs/01-personnalisation-executable.md`
et le code de référence dans `prototypes/config-pe/`. Restent à traiter les métadonnées
Windows (§42) et les icônes multi-résolutions.

---

## Phase 7 — Publication

Configurer :

* build Release;
* publication self-contained;
* publication single-file — confirmée compatible avec la personnalisation `/config`;
* compression du bundle activée, qui réduit l'exécutable d'environ moitié;
* package GitHub;
* documentation minimale.

---

# 67. Décisions qui ne doivent pas bloquer le développement initial

Certains détails pourront être déterminés en cours d'implémentation.

## 67.1 Questions tranchées

Les points suivants ont été résolus par le prototype `config-pe`. Le détail figure dans
`docs/01-personnalisation-executable.md`.

* **Mécanisme de modification des ressources PE** — les API Win32 de mise à jour de
  ressources ne peuvent être appliquées qu'au PE isolé du bundle, jamais à l'exécutable
  publié, sous peine de détruire ce dernier.
* **Bibliothèque de manipulation PE** — aucune. Toute bibliothèque bâtie sur ces mêmes
  API Win32 présenterait le même défaut.
* **Format interne de la configuration intégrée** — trailer annexé en fin de fichier.
* **Publication single-file** — confirmée viable malgré la personnalisation, ce qui
  préserve la forme de distribution décrite en §2.

## 67.2 Questions encore ouvertes

* stratégie exacte pour embarquer ou fournir WebView2;
* emplacement exact des journaux;
* taille maximale éventuelle des requêtes;
* support win-arm64;
* signature Authenticode.

Ces questions ne doivent pas empêcher la réalisation du prototype fonctionnel.

---

# 68. Définition synthétique de Proton

Proton V1 peut être résumé ainsi :

> Proton est un moteur Windows autonome permettant d'exécuter une application HTML/CSS/JavaScript dans une fenêtre WebView2. Il démarre automatiquement un serveur Kestrel local sur un port disponible, sert l'application depuis le dossier `app`, fournit une API REST sécurisée pour gérer les fichiers du dossier `data`, fournit une couche HTTP permettant d'utiliser des bases SQLite locales et permet, via un mode `/config`, de générer une copie personnalisée et redistribuable de l'exécutable avec son propre nom et sa propre icône.

La priorité de Proton est de conserver une expérience extrêmement simple :

```text
un exécutable
+
un dossier app
+
un dossier data
```

tout en constituant une base extensible sur laquelle des capacités natives Windows supplémentaires pourront être ajoutées ultérieurement.
