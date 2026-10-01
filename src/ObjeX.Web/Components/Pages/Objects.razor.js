// Sends files straight to PUT /api/upload, a few at a time, and reports to the page at most four times a second.
// The page decides keys and URLs; the files stay here, under the ids the page knows them by.

const parallel = 3;
const reportMs = 250;
// Keeps each list of new files well below the circuit's 32 KB message limit.
const batchChars = 16000;

export function createUploader(page, tokenHeader, token) {
    const files = new Map();
    const waiting = [];
    const running = new Map();
    let reports = new Map();
    let timer = 0;
    let nextId = 1;
    let disposed = false;

    function report(event) {
        if (disposed) return;
        // Progress only replaces progress: the last word of a file always reaches the page.
        if (event.kind === "progress" && (reports.get(event.id)?.kind ?? "progress") !== "progress") return;
        reports.set(event.id, event);
        timer ||= setTimeout(flush, reportMs);
    }

    async function flush() {
        timer = 0;
        const batch = [...reports.values()];
        reports = new Map();
        try {
            await page.invokeMethodAsync("OnUploadEvents", batch);
        } catch {
            // The circuit is gone; the uploads still finish on the server.
        }
    }

    function pump() {
        while (running.size < parallel && waiting.length > 0) send(waiting.shift());
    }

    function send({ id, url, type }) {
        const file = files.get(id);
        if (!file) return report({ id, kind: "failed", status: 0, error: "The file is no longer available. Choose it again." });

        const xhr = new XMLHttpRequest();
        running.set(id, xhr);
        const end = event => {
            running.delete(id);
            report(event);
            pump();
        };
        xhr.upload.onprogress = e => report({ id, kind: "progress", loaded: e.loaded });
        xhr.onload = () => end(xhr.status >= 200 && xhr.status < 300
            ? { id, kind: "done" }
            : { id, kind: "failed", status: xhr.status, error: serverError(xhr) });
        xhr.onerror = () => end({ id, kind: "failed", status: 0 });
        xhr.onabort = () => end({ id, kind: "cancelled" });
        xhr.open("PUT", url);
        xhr.setRequestHeader(tokenHeader, token);
        xhr.setRequestHeader("Content-Type", type);
        xhr.send(file);
        report({ id, kind: "progress", loaded: 0 });
    }

    return {
        /** From OxDropZone and OxFileDrop: keeps the files and tells the page about them in batches. */
        async add(entries) {
            let batch = [];
            let chars = 0;
            for (const { file, path } of entries) {
                const id = nextId++;
                files.set(id, file);
                batch.push({ id, path, size: file.size, type: file.type });
                chars += path.length + file.type.length + 64;
                if (chars > batchChars) {
                    await page.invokeMethodAsync("OnFilesAdded", batch);
                    batch = [];
                    chars = 0;
                }
            }
            if (batch.length > 0) await page.invokeMethodAsync("OnFilesAdded", batch);
        },
        /** Queues files the page accepted: [{ id, url, type }]. */
        start(items) {
            waiting.push(...items);
            pump();
        },
        cancel(id) {
            const i = waiting.findIndex(w => w.id === id);
            if (i >= 0) {
                waiting.splice(i, 1);
                report({ id, kind: "cancelled" });
            }
            running.get(id)?.abort();
        },
        cancelAll() {
            for (const { id } of waiting.splice(0)) report({ id, kind: "cancelled" });
            for (const xhr of [...running.values()]) xhr.abort();
        },
        forget(ids) {
            for (const id of ids) files.delete(id);
        },
        dispose() {
            disposed = true;
            clearTimeout(timer);
            waiting.length = 0;
            for (const xhr of [...running.values()]) xhr.abort();
            files.clear();
        },
    };
}

function serverError(xhr) {
    try {
        return JSON.parse(xhr.responseText).error ?? null;
    } catch {
        return null;
    }
}
