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

Écrire cette application avant les API a mis au jour six questions que la
spécification ne tranche pas. Elles sont à régler **avant** les phases 3 et 4.

## 1. Ouvrir une pièce jointe remplacerait l'application

Le cas le plus sérieux. Un lien vers `/data/rapport.pdf` pointe vers l'origine
locale ; or §51 pose que les URL de l'origine locale **restent dans la WebView**.
Cliquer sur une pièce jointe remplacerait donc l'application par le visualiseur PDF
intégré, sans retour possible — il n'y a ni bouton Précédent ni barre d'adresse
(§11).

§51 ne traite que des origines externes. Il manque une règle pour les fichiers
locaux. Trois pistes :

- servir `/data` avec `Content-Disposition: attachment`, ce qui déclenche un
  téléchargement plutôt qu'une navigation — mais empêche d'afficher une image
  dans la page;
- un paramètre de requête explicite, `?download=1`, laissant le choix à
  l'application;
- intercepter, dans la fenêtre, les navigations de premier niveau vers `/data`
  et les confier au système.

## 2. Supprimer une tâche coûte N+2 requêtes

§22 exclut toute suppression récursive implicite. Retirer une tâche portant cinq
pièces jointes demande donc : un listing, cinq suppressions, puis la suppression du
dossier — sept allers-retours pour une opération banale, et rien n'est atomique.
Une interruption au milieu laisse des fichiers orphelins.

§22 envisage une extension récursive explicite « si elle demeure simple et sûre ».
L'usage montre qu'elle est nécessaire, pas seulement souhaitable.

## 3. Que répond le listing d'un dossier inexistant ?

Une tâche sans pièce jointe n'a pas de dossier. `GET /data/attachments/7/` doit-il
retourner `404`, ou une liste vide ? §21 ne le dit pas.

L'exemple suppose `404` et traite ce cas comme « aucune pièce jointe ». Une liste
vide serait plus commode et éviterait de traiter une erreur comme un cas normal.

## 4. Rien n'indique qu'un fichier a été remplacé

§19 distingue `201` (créé) de `204` (remplacé) : l'information existe donc, mais
elle se perd si la couche d'accès ne la remonte pas. Téléverser deux fois le même
nom écrase silencieusement le premier fichier.

À trancher : est-ce à l'application de prévenir, ou faut-il un moyen de refuser
l'écrasement ?

## 5. Aucune transaction ne couvre fichiers et base

Supprimer une tâche touche deux mondes : des lignes SQLite et des fichiers. §32 ne
garantit l'atomicité qu'à l'intérieur d'une base. Une panne entre les deux laisse
un état incohérent.

C'est inhérent à l'architecture et probablement acceptable en V1, mais cela doit
être **écrit** : un développeur d'application doit savoir qu'il lui revient de
gérer ces orphelins.

## 6. Confirmations des points déjà identifiés

L'exercice confirme par l'usage deux trous déjà relevés :

- **Noms de fichiers.** L'exemple assainit côté client, mais rien n'empêche une
  autre application d'envoyer `PUT /data/CON`. Le filtrage doit être dans Proton.
- **Taille des requêtes.** Joindre une vidéo dépasserait la limite par défaut de
  Kestrel et produirait une erreur brute, hors du format uniforme de §24.
