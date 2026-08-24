// Petite couche d'accès aux API de Proton.
//
// Aucun SDK n'est requis pour écrire une application Proton (§53) : tout passe par
// `fetch` et des URL relatives. Ce fichier ne fait qu'éviter de répéter la même
// dizaine de lignes, et donne une idée de ce à quoi ressemblerait une bibliothèque
// officielle si elle voyait le jour.

const Proton = {

    /** Configuration embarquée dans l'exécutable (§24.1). */
    async app() {
        return await lire('/api/app');
    },

    fichiers: {

        /** Contenu d'un dossier de `data` (§21). */
        async lister(chemin = '') {
            const resultat = await lire('/data/' + normaliser(chemin));
            return resultat.entries ?? [];
        },

        /** Contenu brut d'un fichier (§15). */
        async lireTexte(chemin) {
            const reponse = await fetch('/data/' + normaliser(chemin));
            if (!reponse.ok) throw await erreur(reponse);
            return await reponse.text();
        },

        /**
         * Crée ou remplace un fichier (§17). Le corps est le contenu lui-même :
         * une chaîne, un Blob ou un File issu d'un <input type="file">.
         */
        async ecrire(chemin, contenu) {
            const reponse = await fetch('/data/' + normaliser(chemin), {
                method: 'PUT',
                body: contenu
            });
            if (!reponse.ok) throw await erreur(reponse);
        },

        /** Supprime un fichier (§20). */
        async supprimer(chemin) {
            const reponse = await fetch('/data/' + normaliser(chemin), { method: 'DELETE' });
            if (!reponse.ok && reponse.status !== 404) throw await erreur(reponse);
        },

        /** Crée un dossier, parents compris. La barre oblique finale le désigne (§22.2). */
        async creerDossier(chemin) {
            const reponse = await fetch('/data/' + normaliser(chemin) + '/', { method: 'PUT' });
            if (!reponse.ok) throw await erreur(reponse);
        },

        /**
         * Supprime un dossier (§22.3).
         *
         * Sans `recursif`, un dossier non vide est refusé par `409` : la destruction
         * du contenu ne peut jamais résulter d'un oubli.
         */
        async supprimerDossier(chemin, { recursif = false } = {}) {
            const url = '/data/' + normaliser(chemin) + '/' + (recursif ? '?recursive=1' : '');
            const reponse = await fetch(url, { method: 'DELETE' });
            if (!reponse.ok && reponse.status !== 404) throw await erreur(reponse);
        },

        /** URL directe d'un fichier, utilisable dans un <a href> ou un <img src>. */
        url(chemin) {
            return '/data/' + normaliser(chemin);
        }
    },

    /** Accès à une base SQLite de `data` (§27). */
    base(nom) {
        return {
            /** Lecture. Retourne { columns, rows } (§29). */
            async query(sql, parameters = {}) {
                return await poster(`/api/sqlite/${nom}/query`, { sql, parameters });
            },

            /** Lecture retournant des objets plutôt que des tableaux positionnels. */
            async select(sql, parameters = {}) {
                const { columns, rows } = await this.query(sql, parameters);
                return rows.map(ligne =>
                    Object.fromEntries(columns.map((colonne, i) => [colonne, ligne[i]])));
            },

            /** Écriture. Retourne { rowsAffected, lastInsertRowId } (§30). */
            async execute(sql, parameters = {}) {
                return await poster(`/api/sqlite/${nom}/execute`, { sql, parameters });
            },

            /** Plusieurs commandes dans une seule transaction (§32). */
            async transaction(commands) {
                return await poster(`/api/sqlite/${nom}/transaction`, { commands });
            }
        };
    },

    /**
     * Indique si les API nécessaires sont disponibles.
     * Tant que les phases 3 et 4 ne sont pas livrées, elles répondent 501.
     */
    async servicesDisponibles() {
        const [fichiers, sqlite] = await Promise.all([
            disponible('/data/'),
            disponible('/api/sqlite')
        ]);
        return { fichiers, sqlite };
    }
};

// --- Fonctions internes ---------------------------------------------------------

async function lire(url) {
    const reponse = await fetch(url);
    if (!reponse.ok) throw await erreur(reponse);
    return await reponse.json();
}

async function poster(url, corps) {
    const reponse = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corps)
    });
    if (!reponse.ok) throw await erreur(reponse);
    return await reponse.json();
}

async function disponible(url) {
    try {
        const reponse = await fetch(url);
        return reponse.status !== 501;
    } catch {
        return false;
    }
}

/**
 * Les erreurs de Proton suivent un format uniforme, avec un code interne stable
 * qu'une application peut interpréter sans analyser le message humain (§24).
 */
async function erreur(reponse) {
    let code = 'http_' + reponse.status;
    let message = reponse.statusText;
    try {
        const corps = await reponse.json();
        if (corps?.error) {
            code = corps.error.code ?? code;
            message = corps.error.message ?? message;
        }
    } catch {
        // Réponse non JSON : on conserve le statut HTTP comme code.
    }
    const e = new Error(message);
    e.code = code;
    e.status = reponse.status;
    return e;
}

/** Retire une éventuelle barre oblique de tête : les chemins sont relatifs à `data`. */
function normaliser(chemin) {
    return String(chemin).replace(/^\/+/, '');
}
