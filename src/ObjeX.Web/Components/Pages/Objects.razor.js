// Sends files straight to PUT /api/upload, a few at a time, and reports to the page at most four times a second.
// The page decides keys and URLs; the files stay here, under the ids the page knows them by.

const parallel = 3;
const reportMs = 250;
const retryMs = 1000;
// Keeps every call to the page well below the circuit's 32 KB message limit, counted in UTF-8 bytes as sent.
const batchBytes = 16000;
const utf8 = new TextEncoder();

export function createUploader(page, tokenHeader, token) {
    const files = new Map();
    const waiting = [];
    const running = new Map();
    let reports = new Map();
    let timer = 0;
    let nextId = 1;
    let nextDrop = 1;
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
        const pending = [...reports.values()];
        reports = new Map();
        for (const batch of batches(pending)) {
            try {
                await page.invokeMethodAsync("OnUploadEvents", batch);
            } catch {
                // The circuit is away, for example while it reconnects: keep what did not arrive, unless a newer report replaced it.
                for (const event of pending.slice(pending.indexOf(batch[0])))
                    if (!reports.has(event.id)) reports.set(event.id, event);
                if (!disposed) timer ||= setTimeout(flush, retryMs);
                return;
            }
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
        /** From OxDropZone and OxFileDrop: keeps the files and tells the page about them, one drop in one or more batches. */
        async add(entries) {
            const drop = nextDrop++;
            const added = entries.map(({ file, path }) => {
                const id = nextId++;
                files.set(id, file);
                return { id, path, size: file.size, type: file.type };
            });
            for (const batch of batches(added))
                await page.invokeMethodAsync("OnFilesAdded", drop, batch);
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

function* batches(items) {
    let batch = [];
    let bytes = 0;
    for (const item of items) {
        const size = utf8.encode(JSON.stringify(item)).length + 1;
        if (batch.length > 0 && bytes + size > batchBytes) {
            yield batch;
            batch = [];
            bytes = 0;
        }
        batch.push(item);
        bytes += size;
    }
    if (batch.length > 0) yield batch;
}

function serverError(xhr) {
    try {
        return JSON.parse(xhr.responseText).error ?? null;
    } catch {
        return null;
    }
}

// Files on the clipboard (Cmd+V of a screenshot or of files copied in the Finder) go to the folder shown, like a drop.
// Text fields keep their own paste, and an open dialog takes none.
export function watchPaste(sink) {
    const onPaste = e => {
        if (e.target instanceof Element && e.target.closest("input, textarea, [contenteditable]")) return;
        if (document.querySelector(".rz-dialog")) return;
        const files = [...(e.clipboardData?.files ?? [])];
        if (files.length === 0) return;
        e.preventDefault();
        sink.add(files.map(file => ({ file, path: pastedName(file) })));
    };
    document.addEventListener("paste", onPaste);
    return { stop: () => document.removeEventListener("paste", onPaste) };
}

// A screenshot arrives as image.png every time; a timestamp keeps the next one from overwriting it.
function pastedName(file) {
    if (!/^image\.\w+$/i.test(file.name)) return file.name;
    const d = new Date();
    const two = n => String(n).padStart(2, "0");
    return `Pasted ${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())} ${two(d.getHours())}.${two(d.getMinutes())}.${two(d.getSeconds())}`
        + file.name.slice(file.name.lastIndexOf("."));
}
