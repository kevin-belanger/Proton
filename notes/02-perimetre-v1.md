# Note technique 02 — Périmètre de la V1 : simplifications assumées

**Statut :** décisions arrêtées
**Date :** 2026-08-24
**Couvre :** analyse fonctionnelle §16 à §20, §24.1, §52, §59, §60

Cette note consigne trois décisions de périmètre prises avant le début du
développement. Deux retirent des exigences de l'analyse initiale, une en ajoute.

Elle existe pour qu'aucune de ces absences ne soit un jour prise pour un oubli, et
pour que leur révision éventuelle ne demande pas de refonte.

---

## 1. Ce qui a été décidé

| Sujet | V1 | Motif |
| --- | --- | --- |
| Concurrence sur les fichiers | **Retirée** | Application locale mono-instance ; le mécanisme spécifié n'était pas correct |
| Restriction d'origine des API | **Retirée** | Serveur limité à `127.0.0.1` ; périmètre local assumé |
| API d'application `/api/app` | **Ajoutée** | Sans elle, une application ne peut pas lire sa propre configuration |

---

## 2. Concurrence sur les fichiers

### Ce qui est retiré

L'analyse initiale prévoyait un `ETag` fort en SHA-256 et une écriture conditionnelle
par `If-Match`, répondant `412` en cas de conflit.

La V1 ne calcule aucune empreinte de contenu, n'interprète pas `If-Match` et n'émet
pas de `412` sur l'API de fichiers.

### Pourquoi

Deux raisons, dont une seule suffirait.

**Le périmètre ne le justifie pas.** Une application Proton s'exécute localement, en
une instance, et ses écritures proviennent d'une seule page. Deux écrivains simultanés
sur le même fichier sont un cas de figure marginal.

**Le mécanisme spécifié ne tenait pas sa promesse.** La séquence décrite — calculer
l'empreinte, la comparer, puis écrire — n'est pas atomique. Deux clients partis du
même `ETag` peuvent tous deux franchir la comparaison avant que l'un ou l'autre
n'écrive :

```text
Client A                     Client B
GET → ETag ABC               GET → ETag ABC
PUT If-Match: ABC            PUT If-Match: ABC
  empreinte lue = ABC ✓
                               empreinte lue = ABC ✓
  écrit « version A »
                               écrit « version B »
```

Les deux écritures réussissent et celle de A est perdue — exactement ce que le
mécanisme prétendait empêcher. Le rendre correct exigeait un verrou par chemin
maintenu pendant tout le triplet.

Entre une garantie absente et une garantie qui ment, l'absence est préférable : elle
ne trompe pas le développeur d'applications.

### Ce qui subsiste

- **L'écriture atomique** (§59) : écrire un temporaire, puis remplacer. Une lecture
  obtient toujours une version complète, jamais un fichier tronqué. C'est la garantie
  qui compte réellement au quotidien.
- **Un `ETag` faible** (§15), dérivé de la taille et de la date de modification. Il ne
  coûte aucune lecture de contenu et sert la validation de cache HTTP
  (`If-None-Match` → `304`). Ce n'est pas un garde-fou de concurrence.

### Conditions de révision

À reconsidérer si l'un de ces cas se présente :

- plusieurs instances d'une même application Proton peuvent s'exécuter simultanément;
- une application ouvre plusieurs fenêtres ou onglets écrivant dans `data`;
- un processus externe modifie les fichiers de `data` pendant l'exécution;
- une application manipule des documents dont la perte silencieuse serait grave.

### Comment le réintroduire sans refonte

Le service de fichiers doit **acheminer toute écriture et toute suppression par un
point unique**, plutôt que d'ouvrir des flux depuis les gestionnaires de routes.

À cette condition, réintroduire le mécanisme consiste à ajouter, à cet endroit, un
verrou par chemin normalisé et une comparaison de précondition. Les routes ne changent
pas.

---

## 3. Restriction d'origine des API

### Ce qui est retiré

L'analyse initiale demandait de rejeter les requêtes d'origine étrangère. La V1
n'applique aucune vérification d'origine.

### Le risque accepté, énoncé précisément

Le serveur n'écoute que sur `127.0.0.1` et reste donc inaccessible depuis le réseau.
Il demeure joignable par **toute page Web ouverte dans un navigateur de la même
machine**, à condition d'en deviner le port.

La politique CORS d'un navigateur empêche une page tierce de *lire* les réponses. Elle
n'empêche pas l'*effet de bord* : une requête simple part sans contrôle préalable et
s'exécute. Ceci, depuis n'importe quel onglet, atteint le serveur :

```js
fetch('http://127.0.0.1:48723/api/sqlite/app.db/execute', {
  method: 'POST',
  headers: { 'Content-Type': 'text/plain' },   // évite le contrôle préalable
  body: '{"sql":"DROP TABLE users"}'
})
```

La réponse est bloquée par le navigateur ; la table est déjà supprimée.

Le scénario suppose que l'utilisateur visite une page malveillante pendant qu'une
application Proton s'exécute, et que cette page trouve le port retenu — lequel change
à chaque démarrage. La probabilité est faible mais non nulle : quelques milliers de
tentatives suffisent à balayer la plage des ports éphémères.

Ce risque est **accepté pour la V1**, dont le périmètre est le développement local et
les applications personnelles.

### Conditions de révision

À traiter **avant** :

- toute distribution large d'applications Proton à des utilisateurs finaux;
- toute application manipulant des données sensibles ou personnelles;
- l'ouverture d'API natives supplémentaires (§61) — le risque croît avec la surface.

### Comment le réintroduire sans refonte

C'est la contrainte de conception énoncée en §52 : la vérification d'origine doit
exister comme **point de passage identifié** dans la chaîne de traitement HTTP, même
si la V1 le laisse tout passer.

Trois mesures se combineront à cet endroit le moment venu :

1. **Exiger un type de contenu qui force le contrôle préalable** sur les routes à
   effet — `application/json` strictement, refus sinon. Cela seul neutralise l'exemple
   ci-dessus.
2. **Rejeter tout en-tête `Origin` étranger**, et se fier à `Sec-Fetch-Site` lorsqu'il
   est présent.
3. **Un secret d'instance**, engendré à chaque démarrage et injecté dans la page par la
   WebView, exigé sur les routes à effet. C'est la mesure la plus robuste : une page
   tierce ne peut pas le connaître, même en devinant le port.

Aucune ne modifie les routes ni le contrat des API.

---

## 4. API d'application

### Ce qui est ajouté

Une route `GET /api/app` exposant en lecture seule la configuration embarquée :
nom, titre, version, éditeur, plus l'identité du moteur.

### Pourquoi

Sans elle, le mode `/config` produit une incohérence : le développeur définit un nom et
une version, Proton les embarque fidèlement dans l'exécutable, et son JavaScript n'y a
aucun accès. Il devrait les redéclarer dans son HTML, avec la divergence qui s'installe
à la première mise à jour oubliée.

La route donne également à une application le moyen de savoir qu'elle s'exécute dans
Proton, plutôt que dans un navigateur ordinaire ouvert sur les mêmes fichiers.

### Ce qu'elle ne doit pas exposer

Aucun chemin physique, aucun numéro de port, aucune information sur la machine ou
l'utilisateur. §7 et §9.2 posent que l'application Web n'a besoin de connaître ni son
emplacement sur le disque, ni le port du serveur.

Cette réserve n'est pas seulement une question de fuite d'information : c'est ce qui
garantit qu'une application Proton reste déplaçable et indépendante de son
environnement d'exécution.

---

## 5. Effet sur les critères d'acceptation

CA-07, CA-08 et CA-09 portaient sur l'écriture conditionnelle. Ils ont été remplacés,
à numérotation constante, par des critères correspondant au périmètre retenu :

| CA | Avant | Après |
| --- | --- | --- |
| CA-07 | Écriture conditionnelle réussie | Configuration exposée par `/api/app` |
| CA-08 | Conflit `412` | Écriture atomique — jamais de fichier tronqué |
| CA-09 | Suppression conditionnelle | Suppression, et `404` sur fichier inexistant |

CA-05 exige désormais un `ETag` faible et vérifie la réponse `304` sur
`If-None-Match`.

La V1 compte toujours 18 critères d'acceptation.

---

## 6. Note de méthode

Les sections retirées de l'analyse ont été **conservées à leur numéro**, avec leur
contenu remplacé par l'énoncé du report. Renuméroter aurait invalidé les renvois
internes du document ainsi que ceux des notes techniques.

C'est la règle retenue pour la suite : le périmètre change, la numérotation ne bouge
pas.
