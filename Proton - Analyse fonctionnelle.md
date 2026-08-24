# Proton — Analyse fonctionnelle

**Nom de code :** Proton
**Version du document :** 1.2
**Cible fonctionnelle :** Version 1
**Plateforme :** Windows
**Technologie privilégiée :** C# / .NET 10
**Objet :** Spécification fonctionnelle et base de travail pour l'implémentation par Claude Code

Ce document décrit **ce que Proton doit faire**. Les mécanismes retenus pour y parvenir,
lorsqu'ils ont demandé une vérification expérimentale, sont consignés séparément dans
`docs/`. Les prototypes qui les ont établis se trouvent dans `prototypes/`.

**Révisions :**

* 1.2 — §41 complété : la fenêtre doit recevoir l'icône de l'exécutable, Windows Forms ne la reprenant pas seule.
* 1.1 — §51.2 implémenté : une pièce jointe est téléchargée puis ouverte avec l'application associée, au lieu d'être confiée au navigateur.
* 1.0 — les sept phases sont implémentées ; état des critères d'acceptation relevé en §65.1.
* 0.10 — journal de diagnostic précisé (§56.1) et filets contre les erreurs non gérées (§56.2).
* 0.9 — §34 tranché : ATTACH interdit par une limite du moteur SQLite plutôt que par analyse du SQL.
* 0.8 — cadre de conception explicité (§3.4) : application locale, pas service exposé ; les liens ne sont plus résolus dans le confinement (§14.1).
* 0.7 — dernières questions de l'exemple Todo réglées : ordonnancement des opérations mixtes (§59.1) et limites de taille par espace (§58.1).
* 0.6 — comportement d'ouverture d'une pièce jointe arrêté (§51.2) ; traitement générique des échecs d'écriture (§17.1) et normalisation des noms documentée (§17.2) ; listing d'un dossier inexistant en 404 (§21).
* 0.5 — gestion des dossiers spécifiée (§22) : barre oblique finale comme discriminant, création idempotente, suppression récursive explicite et ses précautions.
* 0.4 — navigation vers les espaces réservés interdite (§51.1) et téléchargement explicite (§15.1), à la suite de l'exemple `samples/Todo`.
* 0.3 — périmètre V1 resserré : ajout de l'API d'application (§24.1) ; report du
  contrôle de concurrence sur les fichiers (§16 à §20) et de la restriction d'origine
  (§52) ; §57 à §60, CA-05 à CA-09 et la phase 3 ajustés en conséquence.
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

## 3.4 Une application locale, pas un service exposé

Proton sert à construire des applications de bureau. Le serveur HTTP n'est qu'un
moyen d'atteindre l'application depuis la WebView : il n'écoute que la machine
elle-même, ne sert qu'un seul utilisateur, et cet utilisateur a délibérément installé
et lancé le programme.

Ce cadre gouverne toutes les décisions de conception. **Les mécanismes propres aux
serveurs exposés au public n'ont pas leur place ici** : pas de modèle adversaire, pas
d'authentification, pas de quotas, pas de durcissement contre un attaquant distant.

La distinction est celle-ci :

| Protéger de | Pertinent ? |
| --- | --- |
| Une erreur de l'application — chemin mal formé, opération accidentelle | **Oui** — c'est la raison d'être du confinement |
| Un programme hostile déjà installé sur la machine | **Non** — il dispose déjà d'un accès direct au disque |
| Un attaquant distant | **Non** — le serveur n'écoute pas le réseau (§10) |

Le confinement de `data`, la restriction à l'interface locale et les codes d'erreur
existent pour rendre le comportement **prévisible**, pas pour résister à une
agression. Toute protection dont le coût — en complexité, en performance, ou en
usages légitimes empêchés — dépasse ce qu'elle évite réellement doit être écartée.

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
3. y placer une page d'accueil minimale.

La même page est engendrée lorsque `app` existe déjà mais ne contient pas
d'`index.html` : un dossier vide laisserait autrement l'utilisateur devant une erreur
`404` au premier démarrage.

Cette page doit tenir en un seul fichier, sans ressource externe, et rester jetable :
elle est un point de départ que le développeur remplace par sa propre application,
non un gabarit à désinstaller.

Elle doit néanmoins faire plus qu'afficher un texte fixe. En interrogeant `/api/app`
(§24.1) pour afficher le nom et la version de l'application, et en dressant l'état des
services de Proton, elle montre d'emblée ce qu'une page Web ordinaire ne peut pas
faire — et sert de premier diagnostic si quelque chose ne répond pas.

Une application de démonstration complète n'a pas sa place ici : elle vit dans
`samples` (§64) et n'a pas à voyager dans chaque exécutable produit.

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

Les protections doivent inclure :

* normalisation des chemins;
* rejet des séquences de traversée;
* rejet des chemins absolus, des lettres de lecteur et des séparateurs Windows;
* rejet des caractères que le système de fichiers n'accepte pas;
* vérification finale que le chemin résolu appartient bien à `data`.

Par exemple, une requête ressemblant à :

```text
/data/../../Windows/System32/config
```

doit être refusée.

## 14.1 Les liens ne sont pas résolus

Un lien symbolique ou une jonction placés dans `data` sont **suivis normalement**.

L'API ne permet d'en créer aucun. Pour qu'un lien existe dans `data`, il faut que
l'utilisateur ou un autre programme l'y ait placé — et l'un comme l'autre disposent
déjà d'un accès direct au disque. Les refuser ne protégerait donc de rien.

Ils correspondent en revanche à un usage délibéré : rediriger un sous-dossier
volumineux vers un autre disque. Le refus casserait cet usage sans contrepartie, et
imposerait une résolution de lien sur chaque composant de chaque requête.

Le confinement de `/data` est une **clôture d'API**, non une barrière contre un
adversaire. Il empêche une application de sortir de `data` par accident ou par un
chemin mal formé. Il ne prétend pas contenir un exécutable hostile, qui n'aurait de
toute façon pas besoin de passer par Proton (§34).

La suppression récursive constitue le seul cas particulier, traité en §22.4 : elle
détruit, et ne doit donc jamais descendre à travers un lien.

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
ETag: "6a1f-1a2b3c4d"
Last-Modified: Mon, 24 Aug 2026 14:32:00 GMT
```

Le type MIME doit être déterminé lorsque cela est raisonnablement possible.

Le contenu reste le contenu original du fichier.

Il n'est pas nécessaire d'envelopper le fichier dans une structure JSON simplement pour transmettre ses métadonnées.

L'`ETag` retourné est un **validateur de cache**, dérivé de la taille et de la date de
modification du fichier. Il permet à la WebView de réémettre une requête conditionnelle
`If-None-Match` et de recevoir `304 Not Modified` lorsque le fichier n'a pas changé.

Il ne constitue pas un mécanisme de contrôle de concurrence : voir §16.

## 15.1 Forcer un téléchargement

Une application peut demander qu'un fichier soit téléchargé plutôt qu'affiché :

```http
GET /data/rapport.pdf?download=1
```

Proton ajoute alors :

```http
Content-Disposition: attachment; filename="rapport.pdf"
```

Ce paramètre est **facultatif** et à la main de l'application. Proton ne doit pas
imposer `Content-Disposition` par défaut : cela interdirait des usages légitimes tels
que l'affichage d'un document dans un cadre.

La protection contre la disparition de l'application relève d'un autre mécanisme,
décrit en §51.1, qui ne dépend d'aucun type de fichier.

---

# 16. Concurrence sur les fichiers — reportée après la V1

La V1 **ne fournit pas** de mécanisme de contrôle de concurrence sur les fichiers de
`data`.

Une application Proton est une application de bureau locale, exécutée en une seule
instance, dont les écritures proviennent d'une seule page. Le risque que deux écrivains
se disputent le même fichier est marginal, et le coût d'un mécanisme correct ne se
justifie pas au stade du MVP.

Concrètement :

* aucune empreinte SHA-256 n'est calculée à la lecture;
* l'en-tête `If-Match` n'est pas interprété;
* le code `412 Precondition Failed` n'est pas émis par l'API de fichiers;
* une écriture remplace inconditionnellement le fichier visé.

L'`ETag` faible décrit en §15 subsiste, mais comme validateur de cache uniquement.

L'atomicité de l'écriture elle-même demeure exigée : voir §59.

> **Décision datée du 2026-08-24.** Ce report est un choix assumé, non un oubli.
> Les conditions de sa révision et la façon de réintroduire le mécanisme sans
> réécriture figurent dans `docs/02-perimetre-v1.md`.

---

# 17. Écriture d'un fichier

Une application remplace ou crée un fichier avec :

```http
PUT /data/settings.json
```

Le corps HTTP contient directement le nouveau contenu.

L'écriture est inconditionnelle. Aucun en-tête de précondition n'est requis, et un
`If-Match` éventuellement transmis est ignoré par la V1.

## 17.1 Lorsque l'écriture échoue

Proton ne tient aucune liste de noms interdits et ne cherche pas à anticiper les
refus du système de fichiers. Il tente l'écriture ; si elle échoue, il retourne une
erreur au format uniforme (§24) :

```json
{
  "error": {
    "code": "write_failed",
    "message": "The file could not be written."
  }
}
```

Disque plein, fichier verrouillé par un autre programme, droits insuffisants, nom
refusé par Windows : tous ces cas relèvent du même traitement. Chercher à les
distinguer par avance produirait un code fragile, dépendant de la version de Windows,
pour un bénéfice nul — l'application ne peut de toute façon que signaler l'échec à
l'utilisateur.

Le message d'erreur ne doit pas contenir de chemin physique : l'application Web
raisonne en chemins relatifs à `data` (§7).

## 17.2 Noms normalisés par Windows

Windows normalise silencieusement certains noms de fichiers : un nom terminé par un
point ou une espace perd ce caractère. `rapport.` et `rapport ` désignent donc tous
deux le fichier `rapport`, et une écriture successive sur ces deux noms n'en laisse
qu'un seul.

Ce comportement est **accepté tel quel**. Il ne provoque ni erreur ni perte d'accès :
une application peut toujours relire ce qu'elle vient d'écrire.

La règle qui en découle est simple : **le nom retourné par le listing (§21) fait
foi**, et non celui que l'application a soumis.

> Les noms hérités de MS-DOS — `CON`, `AUX`, `PRN`, `COM1` — ne posent pas de
> difficulté : Windows les accepte aujourd'hui comme des noms de fichiers ordinaires.
> `NUL` fait exception et provoque un échec, traité comme n'importe quel autre échec
> d'écriture par §17.1.

---

# 18. Comportement en cas d'écritures simultanées

En l'absence de contrôle de concurrence (§16), deux écritures simultanées sur le même
fichier se résolvent par « le dernier écrivain gagne ».

Application A écrit :

```text
settings.json ← version A
```

Application B écrit ensuite :

```text
settings.json ← version B
```

Le fichier contient la version B. La version A est perdue, sans erreur signalée.

En revanche, grâce à l'écriture atomique exigée en §59, le fichier contient toujours
**l'une ou l'autre** des deux versions complètes, jamais un mélange des deux.

C'est le comportement attendu de la V1. Il doit être documenté comme tel à l'intention
des développeurs d'applications Proton.

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

Le nouvel `ETag` faible (§15) devrait être retourné dans les en-têtes de la réponse
lorsqu'il est disponible.

---

# 20. Suppression d'un fichier

Exemple :

```http
DELETE /data/document.txt
```

La suppression est inconditionnelle. Comme pour l'écriture (§17), un `If-Match`
éventuellement transmis est ignoré par la V1.

Une suppression réussie peut retourner :

```http
204 No Content
```

La suppression d'un fichier inexistant retourne :

```http
404 Not Found
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

Le listing ne retourne que des métadonnées déjà connues du système de fichiers. Aucune
empreinte du contenu n'est calculée, ni lors d'un listing, ni lors d'une lecture (§16).

Le listing d'un dossier inexistant retourne :

```http
404 Not Found
```

et non une liste vide. Un dossier absent et un dossier vide sont deux états
différents, et l'API ne doit pas les confondre : une application qui les distingue
doit pouvoir le faire.

---

# 22. Dossiers dans `data`

Une application doit pouvoir créer et supprimer des dossiers aussi simplement que des
fichiers.

## 22.1 La barre oblique finale désigne un dossier

C'est elle qui distingue les deux natures de ressource, sur toutes les méthodes :

| Requête | Effet |
| --- | --- |
| `GET /data/notes` | Lit le fichier `notes` |
| `GET /data/notes/` | Liste le dossier `notes` (§21) |
| `PUT /data/notes` | Crée ou remplace le fichier `notes` |
| `PUT /data/notes/` | Crée le dossier `notes` |
| `DELETE /data/notes` | Supprime le fichier `notes` |
| `DELETE /data/notes/` | Supprime le dossier `notes` |

Lorsqu'un chemin sans barre oblique finale désigne en réalité un dossier existant,
une lecture retourne :

```http
301 Moved Permanently
Location: /data/notes/
```

Cette convention lève l'ambiguïté sans introduire de méthode HTTP ni de route
particulière.

## 22.2 Création

```http
PUT /data/rapports/2026/
```

Les dossiers parents manquants sont créés au passage. La réponse est `201 Created`,
ou `204 No Content` si le dossier existait déjà — la création est donc idempotente.

Les dossiers parents nécessaires à l'écriture d'un fichier continuent d'être créés
implicitement : `PUT /data/rapports/2026/mars.pdf` fonctionne sans création préalable.

## 22.3 Suppression

Par défaut, seul un dossier vide peut être supprimé :

```http
DELETE /data/rapports/
```

Si le dossier n'est pas vide, Proton refuse :

```http
409 Conflict
```

```json
{
  "error": {
    "code": "directory_not_empty",
    "message": "The directory is not empty."
  }
}
```

La suppression d'un dossier et de tout son contenu doit être **demandée
explicitement** :

```http
DELETE /data/rapports/?recursive=1
```

Aucune récursion implicite n'est jamais effectuée. Une application qui omet le
paramètre ne peut pas détruire de données par accident.

## 22.4 Précautions impératives

La suppression récursive est l'opération la plus destructrice de l'API. Trois règles
la bornent.

**Le dossier `data` lui-même ne peut pas être supprimé.** `DELETE /data/?recursive=1`
doit être refusé. Une application peut vider son espace de stockage entrée par
entrée, mais pas faire disparaître sa racine.

**Les liens sont supprimés, jamais parcourus.** Un lien symbolique ou une jonction
placés dans `data` sont retirés en tant que liens ; leur cible n'est pas touchée.

C'est le seul endroit où les liens reçoivent un traitement particulier — ailleurs, ils
sont suivis normalement (§14.1). La raison n'est pas la défiance mais l'irréversible :
un utilisateur qui a redirigé `data/photos` vers un autre disque ne s'attend pas à ce
que la suppression d'un dossier emporte ses photos. Supprimer le lien seul est ce
qu'il a demandé ; descendre dedans serait une destruction qu'il n'a pas visée.

**La descente doit être écrite explicitement.** La suppression récursive fournie par
la bibliothèque standard de .NET ne convient pas : mise à l'épreuve sur un dossier
contenant une jonction, elle supprime les fichiers, supprime le lien, puis échoue sur
`UnauthorizedAccessException` en laissant le dossier en place. Le résultat est un
état partiel accompagné d'une erreur — comportement reproduit trois fois sur trois.
Proton doit donc parcourir l'arborescence lui-même, en vérifiant chaque entrée.

> La cible du lien, elle, survit bien à cette opération : le confinement n'est pas en
> cause, seule la fiabilité de la suppression l'est.

## 22.5 Hors périmètre V1

La suppression de plusieurs fichiers en une seule requête n'est pas retenue. Sur un
serveur local, le coût d'une requête par fichier est négligeable, et une opération
groupée soulèverait des questions de résultat partiel qui ne se posent pas ici.

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
| `412` | Réservé — non émis par la V1, voir §16              |
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

# 24.1 API d'application

L'exécutable personnalisé connaît son nom, son titre et sa version (§39, §40). Sans une
route dédiée, l'application Web n'aurait aucun moyen de lire ces valeurs et devrait les
dupliquer dans son propre code, avec la dérive qui s'ensuit.

Proton doit donc exposer sa configuration embarquée en lecture seule :

```http
GET /api/app
```

Réponse :

```json
{
  "name": "Gestion Inventaire",
  "windowTitle": "Gestion Inventaire — Édition 2026",
  "version": "2.4.1",
  "company": "Atelier Kevin",
  "engine": {
    "name": "Proton",
    "version": "1.0.0"
  }
}
```

Lorsqu'aucune configuration n'est embarquée — cas du moteur générique lancé tel quel —
la route répond malgré tout, avec des valeurs par défaut :

```json
{
  "name": "Proton",
  "windowTitle": "Proton",
  "version": null,
  "company": null,
  "engine": {
    "name": "Proton",
    "version": "1.0.0"
  }
}
```

Cette route permet notamment à une application d'afficher son propre numéro de version
sans le coder en dur, et de détecter qu'elle s'exécute bien dans Proton plutôt que dans
un navigateur ordinaire.

## Ce que cette route ne doit pas exposer

* aucun chemin physique du système de fichiers — l'application Web n'a pas à connaître
  son emplacement sur le disque (§7);
* aucun numéro de port — les URL relatives suffisent (§9.2);
* aucune information sur la machine hôte ou l'utilisateur.

La route est en lecture seule. La configuration est figée dans l'exécutable au moment
de sa génération et ne peut pas être modifiée à l'exécution.

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

Les deux derniers emplacements demandent une action de la fenêtre : Windows Forms
n'utilise pas l'icône de l'exécutable de lui-même, et affiche celle du framework tant
qu'aucune autre ne lui est assignée.

Elle doit lui être fournie par sa propriété `Icon`, et non par un message envoyé à son
handle : Windows Forms applique la sienne après la création du handle, et écraserait
le message.

La fenêtre reçoit l'icône de taille standard. Windows la réduit pour la barre de
titre ; les variantes de petite taille que porterait le fichier ICO ne sont donc pas
employées telles quelles — ce qui reste sans conséquence visible dans la plupart des
cas.

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

## 51.1 Les espaces réservés ne sont jamais des destinations

Une exception s'applique aux espaces réservés `/data` et `/api` (§49).

Ces espaces servent des **données**, non des pages. Une navigation de premier niveau
vers l'un d'eux afficherait le contenu du fichier **à la place** de l'application :

```text
<a href="/data/rapport.pdf">Rapport</a>
```

Un clic sur ce lien remplacerait l'application par le lecteur PDF intégré. La fenêtre
n'ayant ni bouton Précédent ni barre d'adresse (§11), l'utilisateur n'aurait aucun
moyen d'en revenir : il devrait fermer et relancer l'application.

Proton doit donc **annuler** toute navigation de premier niveau vers un espace
réservé et confier la ressource au système.

Cette règle ne s'applique qu'à la navigation du document principal. Les images, les
feuilles de style, les médias, les cadres et les requêtes `fetch` ne sont pas des
navigations et demeurent parfaitement libres :

```html
<img src="/data/photo.jpg">          <!-- fonctionne -->
<iframe src="/data/rapport.pdf">     <!-- fonctionne -->
fetch('/data/settings.json')         <!-- fonctionne -->
```

Le filtrage porte sur la nature de la requête, non sur le type du fichier : aucune
liste d'extensions n'est à tenir à jour.

> Cette règle a été mise au jour en écrivant l'exemple `samples/Todo`, dont les
> pièces jointes exposaient exactement ce défaut.

## 51.2 Que devient la ressource interceptée

Empêcher la disparition de l'application est acquis. Reste ce qui arrive ensuite au
fichier sur lequel l'utilisateur a cliqué.

**Le comportement retenu est celui d'une application de bureau ordinaire :** le
fichier est téléchargé, puis ouvert avec l'application que le système lui associe.
C'est ce que fait un client de messagerie lorsqu'on ouvre une pièce jointe. La
fenêtre Proton, elle, reste affichée.

Le comportement provisoire — confier l'URL au navigateur par défaut — fonctionne mais
ne convient pas :

* il ouvre un navigateur complet pour consulter une pièce jointe;
* le fichier s'affiche dans le navigateur, et non dans l'application que
  l'utilisateur a associée à ce type de document;
* l'URL retenue contient un **port éphémère** (§9.2). L'onglet cesse de fonctionner
  dès que l'application se ferme, et l'entrée laissée dans l'historique du navigateur
  est morte au prochain démarrage.

Le procédé retenu : la fenêtre récupère la ressource, l'enregistre dans le dossier
des téléchargements de l'utilisateur, puis la confie au système. Le garde-fou de
§51.1 demeure la seule règle de navigation.

Deux voies avaient été envisagées :

1. **Par le serveur.** Répondre `Content-Disposition: attachment` lorsque l'en-tête
   `Sec-Fetch-Dest` vaut `document`, c'est-à-dire pour les seules navigations de
   premier niveau. La WebView transforme alors la navigation en téléchargement et
   affiche sa barre native. Élégant, mais rend le garde-fou de §51.1 inopérant,
   puisque celui-ci annule la navigation avant même que le serveur ne réponde.
2. **Par la fenêtre** — retenu. Le garde-fou reste la seule règle de navigation, au
   prix d'une responsabilité de téléchargement confiée à la fenêtre.

Un fichier de même nom déjà présent n'est jamais écrasé : un suffixe est ajouté,
l'utilisateur ayant peut-être encore ouvert le précédent.

Dans tous les cas, il ne doit jamais se retrouver devant un navigateur, ni devant une
adresse contenant un numéro de port.

---

# 52. Origine des requêtes — restriction reportée après la V1

La V1 **n'applique pas** de restriction d'origine sur ses API.

Le serveur n'écoute que sur `127.0.0.1` : il est inaccessible depuis le réseau (§10).
Le périmètre visé par la V1 est le développement local et les applications
personnelles, pour lesquelles cette isolation est jugée suffisante.

## Risque accepté

Une page Web ouverte dans un navigateur **de la même machine** peut atteindre le
serveur Proton si elle en devine le port. La politique CORS empêche cette page de
*lire* les réponses, mais pas de *provoquer des effets* : une requête simple, sans
contrôle préalable, parvient au serveur et s'exécute.

Ce risque est accepté pour la V1. Il suppose qu'un utilisateur visite une page
malveillante pendant qu'une application Proton s'exécute, et que cette page découvre
le port retenu — lequel change à chaque démarrage (§9.2).

## Contrainte de conception

Ce choix doit rester **réversible sans réécriture**. La vérification d'origine doit
donc exister comme point de passage identifié dans la chaîne de traitement HTTP, même
si la V1 le laisse tout passer.

Restreindre l'accès dans une version ultérieure doit être affaire de politique, pas de
refonte.

> **Décision datée du 2026-08-24.** Les mesures envisagées et les conditions de
> révision figurent dans `docs/02-perimetre-v1.md`. Ce choix doit être réexaminé
> avant toute distribution large d'applications Proton.

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

## 56.1 Emplacement et forme

Le journal est écrit dans le profil de l'utilisateur :

```text
%LOCALAPPDATA%\Proton\logs\proton.log
```

Le dossier de l'application reste donc intact — il se distribue par copie et ne doit
rien accumuler (§2).

Seuls les événements notables y figurent : démarrage, adresse retenue, arrêt, et les
erreurs graves. **Jamais une ligne par requête**, qui ferait grossir le fichier sans
rien apprendre.

Le fichier est encodé en UTF-8 **avec marque d'ordre des octets**. Sans elle, les
outils de Windows le lisent dans la page de codes ANSI et affichent « dÃ©marrÃ© » : un
journal illisible ne rend aucun service.

Au-delà d'un mégaoctet, une génération précédente est conservée sous `proton.log.1`.
Deux fichiers suffisent : l'intérêt d'un journal de diagnostic est de couvrir la
dernière session, non l'historique complet.

## 56.2 Aucune erreur ne doit disparaître en silence

Trois filets se répondent, chacun pour un chemin d'échappement différent :

| Origine | Traitement |
| --- | --- |
| Exception dans le traitement d'une requête | Réponse `500` au format uniforme (§24), et consignation |
| Exception sur le thread de l'interface | Boîte de dialogue et consignation, plutôt que la fenêtre d'erreur de Windows Forms |
| Exception non gérée avant l'arrêt du processus | Consignation — le journal est alors le seul témoin qui subsiste |

Le premier point mérite d'être souligné : sans lui, une exception imprévue produirait
la page d'erreur HTML du serveur. Une application JavaScript recevrait alors quelque
chose qu'elle ne sait pas interpréter, là où elle attend un code stable.

---

# 57. Performance

Proton vise principalement des applications locales de petite ou moyenne taille.

La V1 n'a pas pour vocation de remplacer un serveur de bases de données multi-utilisateur.

Les opérations HTTP locales doivent néanmoins être suffisamment rapides pour qu'une application utilisant `fetch()` ait une sensation comparable à une application native.

La V1 ne calcule aucune empreinte SHA-256 sur les fichiers de `data` (§16), ce qui
supprime de fait le principal risque de coût inutile sur cette API.

L'`ETag` faible de §15 se déduit de métadonnées déjà connues du système de fichiers :
son calcul est négligeable et ne nécessite pas de lire le contenu.

---

# 58. Taille des fichiers

L'API de fichiers doit traiter les données sous forme de flux lorsque cela est approprié.

Un fichier volumineux ne doit pas nécessairement être chargé intégralement plusieurs fois en mémoire.

En lecture comme en écriture, le contenu devrait transiter par flux plutôt que par un
tampon complet en mémoire.

## 58.1 Limite de taille des requêtes

Kestrel plafonne par défaut le corps d'une requête à 30 000 000 octets. Cette limite
protège un serveur exposé au public ; elle n'a pas lieu d'être ici, où le serveur
n'écoute que la machine elle-même. Joindre une vidéo à une fiche est un usage
parfaitement légitime.

La limite est donc levée **là où le contenu transite par flux**, et maintenue là où
il doit être chargé en mémoire :

| Espace | Limite | Raison |
| --- | --- | --- |
| `/data` | aucune | Le contenu est écrit directement sur le disque, sans s'accumuler en mémoire |
| `/api` | 32 Mo | Un corps JSON est désérialisé en mémoire avant d'être exécuté |

Une requête `/api` dépassant la limite doit répondre `413` au format uniforme de §24,
et non l'erreur brute du serveur.

Aucune limite n'est nécessaire sur `/data` : le disque plein constitue la borne
naturelle, et §17.1 traite déjà cet échec.

---

# 59. Atomicité de l'écriture

La V1 ne protège pas contre les écritures concurrentes (§16), mais elle doit garantir
qu'aucun fichier ne se retrouve à moitié écrit.

C'est la garantie qui subsiste, et elle suffit au périmètre visé : une application
relisant un fichier obtient toujours un contenu cohérent, même si une écriture a été
interrompue.

L'écriture finale doit donc être effectuée de manière aussi atomique que possible.

Une stratégie adaptée consiste à :

```text
écrire temporairement
→ fermer le flux
→ remplacer le fichier cible
```

Cela réduit le risque qu'un arrêt au milieu de l'écriture laisse un fichier partiellement écrit.

## 59.1 Aucune transaction ne couvre à la fois les fichiers et les bases

Une opération applicative touche souvent les deux mondes : supprimer une fiche, c'est
retirer une ligne d'une base **et** des fichiers de `data`. Les transactions de §32 ne
garantissent l'atomicité qu'à l'intérieur d'une base.

Proton ne fournit donc **aucune garantie transversale**, et n'en fournira pas : une
transaction distribuée entre SQLite et le système de fichiers serait hors de
proportion avec le périmètre de la V1.

Il revient à l'application de composer ses opérations. La règle utile ne consiste pas
à empêcher l'échec, mais à choisir **de quel côté il retombe** :

| Ordre retenu | En cas d'interruption |
| --- | --- |
| Supprimer la ligne, puis les fichiers | Fichiers orphelins **invisibles** : plus rien ne signale leur existence |
| Supprimer les fichiers, puis la ligne | La fiche subsiste, l'utilisateur recommence — **réparable** |

Symétriquement, à la création : enregistrer la ligne d'abord, écrire les fichiers
ensuite. Une fiche sans pièce jointe se corrige ; une pièce jointe sans fiche est
introuvable.

Le principe : **orienter les échecs vers un excès de données plutôt que vers des
données invisibles.** Ce qui reste visible peut être réparé.

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
* système de permissions complexe;
* contrôle de concurrence sur les fichiers — `ETag` fort et `If-Match`, voir §16;
* restriction d'origine sur les API HTTP, voir §52.

Les deux derniers points sont des simplifications décidées le 2026-08-24 en cours de
conception, et non des fonctionnalités jamais envisagées. Leurs conditions de révision
figurent dans `docs/02-perimetre-v1.md`.

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

doit retourner son contenu, un `ETag` faible et un `Last-Modified`.

Une seconde requête portant `If-None-Match` avec cet `ETag` doit retourner `304`.

---

## CA-06 — Écriture

Un :

```http
PUT /data/test.txt
```

doit pouvoir créer ou remplacer le fichier.

---

## CA-07 — Configuration exposée

Sur un exécutable généré avec :

```json
{ "name": "Test Proton" }
```

la route :

```http
GET /api/app
```

doit retourner ce nom.

Sur le moteur générique, la même route doit répondre avec les valeurs par défaut plutôt
qu'échouer.

---

## CA-08 — Écriture atomique

Un fichier ne doit jamais être observable dans un état partiellement écrit.

Une lecture effectuée pendant le remplacement d'un fichier doit retourner soit
l'ancien contenu complet, soit le nouveau, jamais un contenu tronqué.

---

## CA-09 — Suppression

Un :

```http
DELETE /data/test.txt
```

doit supprimer le fichier et retourner `204`.

La suppression d'un fichier inexistant doit retourner `404`.

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

## 65.1 État des critères

Relevé au terme des sept phases.

| Critère | État | Comment il a été vérifié |
| --- | --- | --- |
| CA-01 · Exécutable seul | **Vérifié** | Dossier vide, `app` et `data` créés, WebView active confirmée par ses processus enfants |
| CA-02 · Application personnalisée | **Vérifié** | L'exemple `Todo` s'affiche et fonctionne |
| CA-03 · Port automatique | **Vérifié** | Test automatisé : deux instances obtiennent des ports distincts |
| CA-04 · Isolation réseau | **Vérifié** | Injoignable sur les cinq interfaces routables de la machine |
| CA-05 · Lecture de fichier | **Vérifié** | Test automatisé, `ETag` et `304` compris |
| CA-06 · Écriture | **Vérifié** | Test automatisé |
| CA-07 · Configuration exposée | **Vérifié** | Test automatisé sur `/api/app` |
| CA-08 · Écriture atomique | **Vérifié** | Écriture par temporaire puis remplacement |
| CA-09 · Suppression | **Vérifié** | Test automatisé, `404` sur fichier absent compris |
| CA-10 · Traversée interdite | **Vérifié** | 34 tests couvrant les formes connues |
| CA-11 · SQLite | **Vérifié** | Cycle complet par HTTP, puis dans l'exemple `Todo` |
| CA-12 · Transaction | **Vérifié** | Test automatisé : la première commande est annulée |
| CA-13 · Fermeture | **Vérifié** | Processus terminé, port réutilisable aussitôt |
| CA-14 · Génération | **Vérifié** | SHA-256 du moteur inchangé après chaque passe |
| CA-15 · Personnalisation | **Vérifié** | Nom, icône et métadonnées relevés sur l'exécutable produit |
| CA-16 · Indépendance de `config` | **Vérifié** | Dossier supprimé, l'application garde son identité |
| CA-17 · Génération récursive | **Vérifié** | Chaîne de trois générations, sans accumulation |
| CA-18 · Aucune installation .NET | **Présumé** | La publication self-contained l'assure, mais aucune machine dépourvue de runtime .NET n'était disponible pour l'établir |

CA-18 est le seul qui n'ait pas été constaté directement : il demande une machine
vierge. Tous les autres l'ont été, soit par un test automatisé, soit sur un
exécutable réellement lancé.

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
* `ETag` faible et `Last-Modified`, pour la validation de cache;
* écriture atomique;
* `?download=1` (§15.1);
* création et suppression de dossiers, récursion explicite comprise (§22);
* codes HTTP;
* format d'erreur uniforme.

Ajouter également la route `/api/app` (§24.1), qui appartient à la même couche HTTP
et ne dépend que de la configuration embarquée.

L'exemple `samples/Todo` a été écrit avant cette phase afin d'éprouver le contrat de
ces routes par l'usage. Son `README` recense les questions qu'il a mises au jour et
qui restent à trancher — notamment le listing d'un dossier inexistant et la
suppression récursive.

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
