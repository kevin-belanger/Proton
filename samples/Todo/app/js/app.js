// Proton Todo — a small application that exercises every Proton capability:
// the embedded configuration, a SQLite database, and real files on disk.

const db = Proton.db('todo.db');

// Attachments of a task live in their own folder, and are listed from it rather
// than tracked in a table. Two sources of truth would eventually disagree.
const folderOf = id => `attachments/${id}`;

const list = document.querySelector('#tasks');
const empty = document.querySelector('#empty');
const taskTemplate = document.querySelector('#task-template');
const chipTemplate = document.querySelector('#attachment-template');

let filter = 'all';

// --- Start ------------------------------------------------------------------

start();

async function start() {
    // A packaged application carries its own name; the plain engine reports its
    // own, in which case the title written in the HTML is the better one.
    const app = await Proton.app();

    if (app.name !== app.engine.name) {
        document.title = app.name;
        document.querySelector('#title').textContent = app.name;
    }

    await db.run(`CREATE TABLE IF NOT EXISTS tasks (
        id      INTEGER PRIMARY KEY AUTOINCREMENT,
        note    TEXT    NOT NULL,
        created TEXT    NOT NULL,
        done    INTEGER NOT NULL DEFAULT 0
    )`);

    document.querySelector('#new-task').addEventListener('submit', add);

    document.querySelectorAll('#filters button').forEach(button =>
        button.addEventListener('click', () => {
            filter = button.dataset.filter;
            document.querySelectorAll('#filters button')
                .forEach(b => b.classList.toggle('selected', b === button));
            render();
        }));

    await render();
}

// --- Rendering ---------------------------------------------------------------

async function render() {
    const tasks = await db.select('SELECT * FROM tasks ORDER BY done, id DESC');

    const counts = { all: tasks.length, active: 0, done: 0 };
    tasks.forEach(t => t.done ? counts.done++ : counts.active++);

    document.querySelectorAll('#filters button').forEach(b =>
        b.querySelector('.count').textContent = counts[b.dataset.filter]);

    const shown = tasks.filter(t =>
        filter === 'all' || (filter === 'done') === Boolean(t.done));

    // Which tasks have an attachment folder at all. Asking once here means never
    // asking for a folder that does not exist.
    const withFiles = new Set((await Proton.files.list('attachments')).map(entry => entry.name));

    list.replaceChildren(...await Promise.all(shown.map(task => card(task, withFiles))));
    empty.hidden = shown.length > 0;
}

async function card(task, withFiles) {
    const node = taskTemplate.content.cloneNode(true);
    const item = node.querySelector('.task');

    item.classList.toggle('done', Boolean(task.done));
    item.querySelector('.note').textContent = task.note;
    item.querySelector('.date').textContent = formatDate(task.created);

    item.querySelector('.check').onclick = () => toggle(task);
    item.querySelector('.remove').onclick = () => removeTask(task);
    item.querySelector('.attach input').onchange = event => attach(task, event.target.files);

    if (withFiles.has(String(task.id))) {
        const files = await Proton.files.list(folderOf(task.id));
        item.querySelector('.attachments')
            .append(...files.filter(f => f.type === 'file').map(f => chip(task, f)));
    }

    // Files can also be dropped straight onto the card.
    item.ondragover = event => { event.preventDefault(); item.classList.add('dropping'); };
    item.ondragleave = () => item.classList.remove('dropping');
    item.ondrop = event => {
        event.preventDefault();
        item.classList.remove('dropping');
        attach(task, event.dataTransfer.files);
    };

    return node;
}

function chip(task, file) {
    const node = chipTemplate.content.cloneNode(true);
    const link = node.querySelector('a');

    link.textContent = file.name;
    link.href = Proton.files.url(`${folderOf(task.id)}/${file.name}`);
    node.querySelector('.size').textContent = formatSize(file.size);
    node.querySelector('.detach').onclick = () => detach(task, file.name);

    return node;
}

// --- Actions -----------------------------------------------------------------

async function add(event) {
    event.preventDefault();

    const input = document.querySelector('#note');
    if (!input.value.trim()) return;

    await db.run('INSERT INTO tasks (note, created) VALUES ($note, $created)', {
        $note: input.value.trim(),
        $created: new Date().toISOString()
    });

    input.value = '';
    await render();
}

async function toggle(task) {
    await db.run('UPDATE tasks SET done = $done WHERE id = $id', {
        $done: task.done ? 0 : 1,
        $id: task.id
    });
    await render();
}

async function removeTask(task) {
    // Files first, then the row. The other order would leave attachments that
    // nothing points to any more.
    await Proton.files.removeFolder(folderOf(task.id));
    await db.run('DELETE FROM tasks WHERE id = $id', { $id: task.id });
    await render();
}

async function attach(task, files) {
    for (const file of files) {
        // The request body is the file itself — Proton writes the bytes as they
        // come, with no intermediate encoding.
        await Proton.files.write(`${folderOf(task.id)}/${safeName(file.name)}`, file);
    }
    await render();
}

async function detach(task, name) {
    await Proton.files.remove(`${folderOf(task.id)}/${name}`);
    await render();
}

// --- Formatting ---------------------------------------------------------------

function formatDate(iso) {
    return new Date(iso).toLocaleString(undefined,
        { dateStyle: 'medium', timeStyle: 'short' });
}

function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1048576).toFixed(1)} MB`;
}

/** Windows rejects a few characters in file names. */
function safeName(name) {
    return name.replace(/[\\/:*?"<>|]/g, '_');
}
