// Application de démonstration : une liste de tâches avec pièces jointes.
//
// Elle exerce délibérément toutes les capacités de Proton V1 :
//   — /api/app    identité de l'application
//   — /api/sqlite les tâches
//   — /files      les pièces jointes, y compris le listing d'un dossier

const base = Proton.base('todo.db');

/** Les pièces jointes d'une tâche vivent dans data/attachments/{id}/. */
const dossierPiecesJointes = id => `attachments/${id}`;

// --- Démarrage ------------------------------------------------------------------

document.addEventListener('DOMContentLoaded', async () => {
    const services = await Proton.servicesDisponibles();

    if (!services.fichiers || !services.sqlite) {
        annoncerServicesManquants(services);
        return;
    }

    await afficherIdentite();
    await creerSchema();
    await rafraichir();

    document.getElementById('formulaire').addEventListener('submit', ajouterTache);
});

async function afficherIdentite() {
    try {
        const app = await Proton.app();
        document.getElementById('titre').textContent = app.name;
        document.title = app.name;
        if (app.version) {
            document.getElementById('version').textContent = 'version ' + app.version;
        }
    } catch {
        // L'application reste utilisable même sans identité configurée.
    }
}

async function creerSchema() {
    await base.execute(`
        CREATE TABLE IF NOT EXISTS taches (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            note       TEXT    NOT NULL,
            creee_le   TEXT    NOT NULL,
            terminee   INTEGER NOT NULL DEFAULT 0
        )
    `);
}

// --- Lecture --------------------------------------------------------------------

async function rafraichir() {
    const taches = await base.select(
        'SELECT id, note, creee_le, terminee FROM taches ORDER BY terminee, id DESC');

    const liste = document.getElementById('taches');
    liste.replaceChildren();

    if (taches.length === 0) {
        liste.innerHTML = '<li class="vide">Aucune tâche pour le moment.</li>';
        return;
    }

    for (const tache of taches) {
        liste.appendChild(await construireLigne(tache));
    }
}

async function construireLigne(tache) {
    const ligne = document.createElement('li');
    ligne.className = tache.terminee ? 'tache terminee' : 'tache';

    const coche = document.createElement('input');
    coche.type = 'checkbox';
    coche.checked = Boolean(tache.terminee);
    coche.addEventListener('change', () => basculer(tache.id, coche.checked));

    const corps = document.createElement('div');
    corps.className = 'corps';
    corps.innerHTML =
        `<span class="note"></span>` +
        `<span class="meta">n° ${tache.id} · ${formaterDate(tache.creee_le)}</span>`;
    corps.querySelector('.note').textContent = tache.note;

    corps.appendChild(await construirePiecesJointes(tache.id));

    const joindre = document.createElement('input');
    joindre.type = 'file';
    joindre.multiple = true;
    joindre.id = `fichier-${tache.id}`;
    joindre.className = 'fichier';
    joindre.addEventListener('change', () => televerser(tache.id, joindre.files));

    const etiquette = document.createElement('label');
    etiquette.className = 'bouton discret';
    etiquette.htmlFor = joindre.id;
    etiquette.textContent = 'Joindre';

    const supprimer = document.createElement('button');
    supprimer.className = 'bouton discret';
    supprimer.textContent = 'Supprimer';
    supprimer.addEventListener('click', () => supprimerTache(tache.id));

    const actions = document.createElement('div');
    actions.className = 'actions';
    actions.append(etiquette, joindre, supprimer);

    ligne.append(coche, corps, actions);
    return ligne;
}

/**
 * Les pièces jointes ne sont pas enregistrées en base : elles sont déduites du
 * contenu du dossier. Le listing de §21 fournit déjà nom, taille et date, ce qui
 * évite de tenir deux états qui pourraient diverger.
 */
async function construirePiecesJointes(id) {
    const conteneur = document.createElement('div');
    conteneur.className = 'pieces';

    let fichiers = [];
    try {
        fichiers = await Proton.fichiers.lister(dossierPiecesJointes(id));
    } catch (e) {
        // Un dossier absent signifie simplement « aucune pièce jointe ».
        if (e.status !== 404) throw e;
    }

    for (const fichier of fichiers.filter(f => f.type === 'file')) {
        const lien = document.createElement('a');
        lien.href = Proton.fichiers.url(`${dossierPiecesJointes(id)}/${fichier.name}`);
        lien.textContent = fichier.name;
        lien.title = formaterTaille(fichier.size);
        lien.target = '_blank';

        const retirer = document.createElement('button');
        retirer.className = 'retirer';
        retirer.textContent = '×';
        retirer.title = 'Retirer cette pièce jointe';
        retirer.addEventListener('click', () => retirerPieceJointe(id, fichier.name));

        const puce = document.createElement('span');
        puce.className = 'piece';
        puce.append(lien, retirer);
        conteneur.appendChild(puce);
    }

    return conteneur;
}

// --- Écriture -------------------------------------------------------------------

async function ajouterTache(evenement) {
    evenement.preventDefault();

    const champ = document.getElementById('note');
    const note = champ.value.trim();
    if (!note) return;

    await base.execute(
        'INSERT INTO taches (note, creee_le) VALUES ($note, $date)',
        { $note: note, $date: new Date().toISOString() });

    champ.value = '';
    await rafraichir();
}

async function basculer(id, terminee) {
    await base.execute(
        'UPDATE taches SET terminee = $terminee WHERE id = $id',
        { $terminee: terminee ? 1 : 0, $id: id });
    await rafraichir();
}

async function televerser(id, fichiers) {
    const remplaces = [];

    for (const fichier of fichiers) {
        // Le corps de la requête est le fichier lui-même : Proton écrit les octets
        // tels quels, sans encodage intermédiaire (§17).
        const nom = assainir(fichier.name);
        const { cree } = await Proton.fichiers.ecrire(
            `${dossierPiecesJointes(id)}/${nom}`, fichier);

        if (!cree) remplaces.push(nom);
    }

    // Un fichier de même nom est écrasé sans que l'API ne s'y oppose (§16).
    // Le code de retour est le seul moyen d'en avertir l'utilisateur.
    if (remplaces.length > 0) {
        alert('Pièce jointe remplacée : ' + remplaces.join(', '));
    }

    await rafraichir();
}

async function retirerPieceJointe(id, nom) {
    await Proton.fichiers.supprimer(`${dossierPiecesJointes(id)}/${nom}`);
    await rafraichir();
}

async function supprimerTache(id) {
    // Le dossier de pièces jointes part d'un coup, contenu compris (§22.3). La
    // récursion est demandée explicitement : sans ce paramètre, un dossier non vide
    // serait refusé.
    await Proton.fichiers.supprimerDossier(dossierPiecesJointes(id), { recursif: true });

    await base.execute('DELETE FROM taches WHERE id = $id', { $id: id });
    await rafraichir();
}

// --- Présentation ---------------------------------------------------------------

function annoncerServicesManquants(services) {
    const manquants = [];
    if (!services.fichiers) manquants.push('l’API de fichiers');
    if (!services.sqlite) manquants.push('l’API SQLite');

    document.getElementById('contenu').innerHTML =
        `<div class="avertissement">
            <strong>Cette application n’est pas encore utilisable.</strong>
            <p>Elle a besoin de ${manquants.join(' et ')}, que cette version de
            Proton ne fournit pas encore.</p>
            <p class="meta">Elle a été écrite avant ces API, afin d’en définir
            le contrat par l’usage.</p>
        </div>`;
}

/**
 * Windows interdit certains caractères dans les noms de fichiers, et Proton
 * refusera de sortir du dossier `data` (§14). Mieux vaut nettoyer ici que de
 * découvrir l'erreur au téléversement.
 */
function assainir(nom) {
    return nom.replace(/[\\/:*?"<>|]/g, '_').replace(/^\.+/, '_');
}

function formaterDate(iso) {
    const date = new Date(iso);
    return isNaN(date) ? iso : date.toLocaleString();
}

function formaterTaille(octets) {
    if (octets === undefined) return '';
    if (octets < 1024) return `${octets} o`;
    if (octets < 1024 * 1024) return `${(octets / 1024).toFixed(1)} Ko`;
    return `${(octets / 1024 / 1024).toFixed(1)} Mo`;
}
