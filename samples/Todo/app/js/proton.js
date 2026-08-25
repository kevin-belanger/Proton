// A thin wrapper over the Proton APIs.
//
// No SDK is required to write a Proton application: everything goes through
// `fetch` and relative URLs. This file only avoids repeating the same few lines,
// and shows what an official library would look like if one ever appeared.

const Proton = {

    /** Name and version this executable carries. */
    async app() {
        return await json('/api/app');
    },

    files: {

        /** Entries of a folder. Returns [] when the folder does not exist. */
        async list(path) {
            const response = await fetch('/files/' + path + '/');
            return response.ok ? (await response.json()).entries : [];
        },

        /** Creates or replaces a file. The body is the content itself. */
        async write(path, content) {
            await send('/files/' + path, { method: 'PUT', body: content });
        },

        async remove(path) {
            await send('/files/' + path, { method: 'DELETE' });
        },

        /** Deletes a folder and everything in it. */
        async removeFolder(path) {
            await send('/files/' + path + '/?recursive=1', { method: 'DELETE' });
        },

        /** Direct URL, usable in a link or an image. */
        url(path) {
            return '/files/' + path;
        }
    },

    /** A SQLite database living in data/db. */
    db(name) {
        return {
            /** Runs a query and returns one object per row. */
            async select(sql, parameters) {
                const { columns, rows } = await json(`/api/sqlite/${name}/query`, { sql, parameters });
                return rows.map(row => Object.fromEntries(columns.map((c, i) => [c, row[i]])));
            },

            /** Runs a statement. Returns { rowsAffected, lastInsertRowId }. */
            async run(sql, parameters) {
                return await json(`/api/sqlite/${name}/execute`, { sql, parameters });
            }
        };
    }
};

// --- Internals --------------------------------------------------------------

async function json(url, body) {
    const options = body
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
        : undefined;

    return await (await send(url, options)).json();
}

async function send(url, options) {
    const response = await fetch(url, options);

    // Proton reports errors in a uniform shape, with a stable code an application
    // can act on without parsing the human-readable message.
    if (!response.ok && response.status !== 404) {
        const body = await response.json().catch(() => null);
        throw new Error(body?.error?.message ?? response.statusText);
    }

    return response;
}
