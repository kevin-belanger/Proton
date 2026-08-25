# Proton

**Documentation: [kevin-belanger.github.io/Proton](https://kevin-belanger.github.io/Proton/)**

A self-contained Windows engine that runs an HTML / CSS / JavaScript application as a
desktop application.

Proton starts a local server, serves the application from an `app` folder, and gives it
capabilities beyond the reach of an ordinary web page: reading and writing files, and
local SQLite databases.

A Proton application is distributed by copying a single file:

```text
MyApplication.exe      ← one file
```

The web application is embedded in the executable. On first start it creates the
folders it needs beside itself:

```text
MyApplication/
├── MyApplication.exe
└── data/
    ├── files/    its files
    └── db/       its SQLite databases
```

No installer, no server, no runtime to install separately.

---

## Getting started

Download `Proton.exe`, put it in an empty folder and run it. It creates `app` and
`data` there, then shows a starter page.

Then replace the contents of `app` with your own. Your pages are served from the root:

```text
app/index.html      →  /
app/css/style.css   →  /css/style.css
```

The application never has to know where it sits on disk, nor which port was chosen:
relative URLs are enough.

### The APIs

No library is required — everything goes through `fetch`.

```js
// The application's identity, as the executable carries it
const app = await (await fetch('/api/app')).json();

// Files
await fetch('/files/notes.txt', { method: 'PUT', body: 'hello' });
const text = await (await fetch('/files/notes.txt')).text();
const { entries } = await (await fetch('/files/folder/')).json();

// Folders — the trailing slash is what designates them
await fetch('/files/photos/', { method: 'PUT' });
await fetch('/files/photos/?recursive=1', { method: 'DELETE' });

// SQLite
await fetch('/api/sqlite/app.db/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
        sql: 'INSERT INTO notes(text) VALUES($t)',
        parameters: { $t: 'hello' }
    })
});
```

**`samples/Todo`** is a complete application that exercises all of these. Copy its
`app` and `config` folders next to `Proton.exe`, run it, then `Proton.exe /generate` to
turn it into a program of its own.

### Personalising the executable

Prepare `config/config.json` and `config/icon.ico`:

```json
{
  "name": "Inventory Manager",
  "executableName": "InventoryManager.exe",
  "windowTitle": "Inventory Manager — 2026 Edition",
  "version": "2.4.1",
  "company": "Kevin's Workshop",
  "window": { "width": 1280, "height": 800, "resizable": true }
}
```

then run:

```bash
Proton.exe /generate
```

You get `InventoryManager.exe`: the same engine, with its name, its icon, its Windows
metadata **and your application embedded in it**. `Proton.exe` is left untouched.

Distribute that file, and nothing else. `data` is created on first start.

To ship initial content — templates, a catalogue, a pre-filled database — add the
`data` argument:

```bash
Proton.exe /generate data
```

It embeds `data/` as well, laid down on first start if that folder does not already
exist.

---

## Status

The seven planned phases are implemented. The engine starts, serves its application,
exposes its APIs and can produce personalised executables.

| Phase | Contents |
| --- | --- |
| 1–2 | Window, WebView2, Kestrel, automatic port, initialisation |
| 3 | File and folder API, `/api/app` |
| 4 | SQLite API — queries, writes, transactions; databases isolated in `data/db` |
| 5 | Diagnostic log, uniform error handling |
| 6 | `/generate` mode — icon, metadata, embedded configuration |
| 7 | Self-contained publication, single file, compressed |

**128 automated tests.** The V1 acceptance criteria are summarised at the end of the
functional analysis.

What falls outside the scope is listed in §60, and the accepted simplifications in
[notes/02-perimetre-v1.md](notes/02-perimetre-v1.md).

---

## Building

```bash
dotnet publish src/Proton/Proton.csproj -c Release
```

The executable lands in **`C:\proton\dist\Proton.exe`**, and the intermediate
artefacts in `C:\proton\build`.

The outputs deliberately stay out of the repository: a self-contained publication
weighs some sixty megabytes, which clutters the working tree and becomes genuinely
awkward when the folder is synchronised by an online storage service. The output root
can be changed with the `PROTON_OUTPUT_ROOT` environment variable.

```bash
dotnet test
```

---

## Documents

| Document | Contents | Language |
| --- | --- | --- |
| [Documentation](https://kevin-belanger.github.io/Proton/) | **The user documentation** — for anyone discovering Proton. Source: [docs/](docs/) | English |
| [Functional analysis](Proton%20-%20Analyse%20fonctionnelle.md) | The specification: what Proton must do, and its acceptance criteria | French |
| [notes/](notes/) | Technical notes — the mechanisms established experimentally | French |
| [prototypes/](prototypes/) | The prototypes that settled those questions, with their measurements | French |
| [samples/](samples/) | Example applications | English |

The analysis describes the **what**. The notes in `notes/` describe the **how**, only
where it required verification. The two refer to each other rather than repeating
themselves.

**On language:** everything Proton says to its user is in English — this file, the
documentation, the samples, the `/generate` output, the dialogs and the diagnostic log.
Everything the project says about itself is in French: the analysis, the technical
notes and the code comments. The boundary runs at the level of the string, not the
file (§63.2).

The site in `docs/` is published by GitHub Pages from that folder of `main`
(§63.1). The engine's starter page links to it rather than reproducing it: presenting
Proton to someone evaluating it and welcoming someone who has just launched it are two
different jobs (§8.1).

---

## Technologies

C# / .NET 10 · WebView2 · Kestrel · SQLite (`Microsoft.Data.Sqlite`) · self-contained
single-file publication.

Diagnostics are written to `%LOCALAPPDATA%\Proton\logs\proton.log`.

---

## Licence

[MIT](LICENSE) — Copyright (c) 2026 Kevin Belanger.

Applications built with Proton are yours: you may distribute them however you like,
commercially included, without publishing your sources. Since the engine is embedded in
every executable produced, its attribution is written there automatically — in the
file's properties and on `/api/app`. There is nothing for you to do.
