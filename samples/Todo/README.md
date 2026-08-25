# Proton Todo

A task list with file attachments. Small enough to read in one sitting, complete
enough to exercise everything Proton offers.

```text
samples/Todo/
├── app/                the application itself
│   ├── index.html      markup and two <template> elements
│   ├── css/style.css   all of the visual work
│   └── js/
│       ├── proton.js   a thin wrapper over the Proton APIs
│       └── app.js      the application logic
└── config/             turns it into a standalone executable
    ├── config.json
    └── icon.ico
```

## Try it

Copy `app` and `config` next to `Proton.exe`, then run it. The application starts
straight away.

To turn it into a program of its own:

```bash
Proton.exe /generate
```

You get **`ProtonTodo.exe`** — your application, its icon and its name, in a single
file. Copy it anywhere and run it; it creates what it needs on first start.

## What it does

- add, complete and delete tasks
- attach files by button, or by dropping them onto a card
- open an attachment — it downloads and opens in the associated program
- remove an attachment
- filter by all, active or completed, with live counts

## How it is built

**The markup lives in `index.html`, not in the JavaScript.** Two `<template>`
elements hold a task card and an attachment chip; the script clones one and fills it
in. That keeps `app.js` down to what the application actually *does*.

**Everything visual is in `style.css`,** including the icons — the tick, the
paperclip, the wastebasket and the cross are drawn with borders and pseudo-elements
rather than loaded as images. Nothing is fetched from the network.

**Attachments are not recorded in the database.** They live in
`data/files/attachments/{id}/` and are read back from the folder listing, which
already reports name, size and date. Two sources of truth would eventually disagree —
a file deleted by hand would leave a phantom row.

**Almost no validation.** This is sample code: it assumes the APIs answer, because
they do. The one guard that earns its place strips the characters Windows rejects in
file names.

## Points worth noticing

**Deleting a task removes its files first, then the row.** The other order would
leave attachments that nothing points to any more — invisible, and impossible to
clean up. When an operation spans the database and the disk, choose the order that
makes an interruption recoverable.

**The folder listing is fetched once per render,** not once per task. Asking which
attachment folders exist before asking what is inside them means never requesting a
folder that isn't there — and no stray 404s in the console.

**The title comes from the executable.** `GET /api/app` returns the name embedded by
`/generate`, so the packaged application calls itself *Proton Todo* while the plain
engine keeps the title written in the HTML.

## The APIs it uses

| Call | What for |
| --- | --- |
| `GET /api/app` | the application's own name and version |
| `POST /api/sqlite/todo.db/query` | reading tasks |
| `POST /api/sqlite/todo.db/execute` | creating the table, adding, updating, deleting |
| `GET /files/attachments/{id}/` | listing attachments |
| `PUT /files/attachments/{id}/{name}` | uploading a file |
| `DELETE /files/attachments/{id}/{name}` | removing one |
| `DELETE /files/attachments/{id}/?recursive=1` | removing a task's folder in one call |

No library is required for any of this — `js/proton.js` is eighty lines of
convenience over `fetch`, and you are free to ignore it.
