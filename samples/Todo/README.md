# Exemple — Tâches

Une liste de tâches avec pièces jointes. Elle existe pour exercer **toutes** les
capacités de Proton V1 dans une application réaliste :

| Capacité | Usage dans l'exemple |
| --- | --- |
| `/api/app` (§24.1) | Titre et version affichés depuis la configuration embarquée |
| `/api/sqlite` (§27) | Les tâches — création du schéma, insertion, lecture, mise à jour, suppression |
| `/data` écriture (§17) | Téléversement d'une pièce jointe, corps de requête brut |
| `/data` lecture (§15) | Ouverture d'une pièce jointe |
| `/data` listing (§21) | Les pièces jointes sont **déduites** du contenu du dossier |
| `/data` suppression (§20) | Retrait d'une pièce jointe, et nettoyage à la suppression d'une tâche |

## Essayer

Copier le contenu de ce dossier dans le `app\` d'une application Proton :

```bash
xcopy /E /I /Y samples\Todo C:\proton\test\app
```

> **En l'état, l'application affiche un avertissement et s'arrête là.** Les API des
> phases 3 et 4 n'existent pas encore et répondent `501`. C'est voulu : elle a été
> écrite **avant** elles, pour définir leur contrat par l'usage plutôt que dans
> l'abstrait.

## Choix de conception

**Les pièces jointes ne sont pas enregistrées en base.** Elles vivent dans
`data/attachments/{id}/` et sont déduites du listing du dossier, qui fournit déjà
nom, taille et date (§21). Tenir une table en plus créerait deux états susceptibles
de diverger — un fichier supprimé à la main laisserait une ligne fantôme.

**Aucun SDK n'est requis** (§53). `js/proton.js` n'est qu'une commodité d'une
centaine de lignes ; tout y passe par `fetch` et des URL relatives. Il donne une
idée de ce que serait une bibliothèque officielle si elle voyait le jour.

---

# Ce que cet exercice a révélé

Écrire cette application avant les API a mis au jour sept questions que la
spécification ne tranchait pas. Toutes sont désormais réglées, et chacune a laissé
une trace dans l'analyse.

## 1. Ouvrir une pièce jointe remplacerait l'application — **tranché**

Le cas le plus sérieux. Un lien vers `/data/rapport.pdf` pointe vers l'origine
locale ; or §51 posait que les URL de l'origine locale **restent dans la WebView**.
Cliquer sur une pièce jointe aurait donc remplacé l'application par le visualiseur
PDF intégré, sans retour possible — il n'y a ni bouton Précédent ni barre d'adresse
(§11).

**Décision (§51.1) :** la fenêtre annule toute navigation de premier niveau vers
`/data` ou `/api` et confie la ressource au système.

Le filtrage porte sur la **nature de la requête**, non sur le type du fichier :
aucune liste d'extensions à tenir à jour, et `<img>`, `<iframe>`, `<video>` et
`fetch` continuent de fonctionner sans exception, n'étant pas des navigations.

Écarté : imposer `Content-Disposition: attachment` sur tout `/data`. Cela aurait
réglé le problème en interdisant au passage l'affichage d'un document dans un cadre.
Le paramètre facultatif `?download=1` (§15.1) reste disponible pour l'application qui
veut explicitement un téléchargement.

## 2. Supprimer une tâche coûtait N+2 requêtes — **tranché**

Retirer une tâche portant cinq pièces jointes demandait un listing, cinq
suppressions, puis la suppression du dossier : sept allers-retours pour une opération
banale, dont aucun n'était atomique. Une interruption au milieu laissait des fichiers
orphelins.

**Décision (§22.3) :** la suppression récursive d'un dossier existe, sur demande
explicite. `supprimerTache` tient désormais en une seule requête.

La récursion reste **opt-in** : sans le paramètre, un dossier non vide est refusé par
`409`. La destruction d'un contenu ne peut jamais résulter d'un oubli.

Écarté : la suppression de plusieurs fichiers en une requête (§22.5). Sur un serveur
local, une requête par fichier ne coûte rien, et une opération groupée soulèverait
des questions de résultat partiel qui ne se posent pas ici.

## 3. Que répond le listing d'un dossier inexistant ? — **tranché**

Une tâche sans pièce jointe n'a pas de dossier. `GET /data/attachments/7/` doit-il
retourner `404`, ou une liste vide ?

**Décision (§21) : `404`.** Un dossier absent et un dossier vide sont deux états
différents, et l'API ne les confond pas. C'est ce que l'exemple supposait déjà : il
traite ce `404` comme « aucune pièce jointe ».

§22.1 avait réglé la question voisine : la barre oblique finale distingue un fichier
d'un dossier sur toutes les méthodes.

## 4. Rien n’indique qu’un fichier a été remplacé — **tranché**

§19 distingue `201` (créé) de `204` (remplacé) : l'information existe donc, mais
elle se perd si la couche d'accès ne la remonte pas. Téléverser deux fois le même
nom écrase silencieusement le premier fichier.

**Décision : rien à ajouter à l'API.** §19 distingue déjà `201 Created` de
`204 No Content` : après un `PUT`, l'application sait si elle a créé ou remplacé.

Ce qui manquait était dans la couche d'accès, qui avalait ce code de retour.
`Proton.fichiers.ecrire` retourne désormais `{ cree }`, et l'exemple avertit quand
une pièce jointe en a écrasé une autre.

Écarté : un moyen de refuser l'écrasement, tel que `If-None-Match: *`. Ce serait
réintroduire une précondition par la petite porte, la complexité même écartée en §16.
Rien n'empêchera de l'ajouter le jour où le besoin se manifestera.

## 5. Aucune transaction ne couvre fichiers et base — **tranché**

Supprimer une tâche touche deux mondes : des lignes SQLite et des fichiers. §32 ne
garantit l'atomicité qu'à l'intérieur d'une base. Une panne entre les deux laisse
un état incohérent.

**Décision (§59.1) : la limite est assumée, avec une règle d'ordonnancement.**

Proton ne fournira aucune garantie transversale — une transaction distribuée entre
SQLite et le système de fichiers serait hors de proportion. En revanche, l'ordre des
opérations décide de quel côté l'échec retombe :

| Ordre | En cas d'interruption |
| --- | --- |
| Ligne d'abord, puis fichiers | Fichiers orphelins **invisibles** |
| Fichiers d'abord, puis ligne | La fiche subsiste, réparable |

`supprimerTache` applique déjà cette règle : le dossier de pièces jointes part avant
la ligne en base.

## 6. Noms de fichiers — **tranché**

L'exemple assainit les noms côté client, mais rien n'empêchait une autre application
d'envoyer `PUT /data/CON`.

**Décision (§17.1) : aucune liste de noms interdits.** Proton tente l'écriture ; si
elle échoue, il retourne `write_failed`. Disque plein, fichier verrouillé, nom refusé
par Windows relèvent du même traitement.

La mesure a montré que le risque était surestimé : sur Windows 11, `CON`, `AUX`,
`PRN` et `COM1` sont créés comme des fichiers ordinaires. Seul `NUL` échoue — et un
échec est exactement ce que §17.1 sait traiter.

Un cas résiduel est documenté plutôt que codé (§17.2) : un nom terminé par un point
ou une espace est normalisé par Windows. Il ne provoque ni erreur ni perte d'accès,
mais le nom retourné par le listing fait foi.


## 7. Taille des requêtes — **tranché**

Joindre une vidéo dépasserait la limite par défaut de Kestrel et produirait une
erreur brute, hors du format uniforme de §24.

**Décision (§58.1) : la limite est levée sur `/data`, maintenue sur `/api`.**

| Espace | Limite | Raison |
| --- | --- | --- |
| `/data` | aucune | Le contenu transite par flux vers le disque |
| `/api` | 32 Mo | Un corps JSON est désérialisé en mémoire |

Joindre une vidéo devient donc possible. Le disque plein reste la borne naturelle, et
§17.1 traite cet échec comme les autres.
