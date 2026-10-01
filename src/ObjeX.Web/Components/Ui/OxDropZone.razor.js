// Collects dropped or picked files as { file, path } and hands them to a sink's add(); a dropped folder is read with all its subfolders.

let documentGuarded = false;

/** The whole page as drop target: files dropped anywhere on the zone go to the sink. */
export function initDropZone(zone, sink) {
    zone.oxSink = sink;
    if (zone.oxReady) return;
    zone.oxReady = true;
    trackDrag(zone, async e => {
        const entries = await collect(e.dataTransfer);
        if (entries.length > 0) await zone.oxSink.add(entries);
    });
    guardDocument();
}

/** The drop area of a dialog: dropped files, the file picker and the folder picker; tells the component once the files went to the sink. */
export function initFileDrop(zone, sink, component) {
    const filesInput = zone.querySelector("input[type=file]:not([webkitdirectory])");
    const folderInput = zone.querySelector("input[webkitdirectory]");
    const hand = async entries => {
        if (entries.length === 0) return;
        await sink.add(entries);
        await component.invokeMethodAsync("Picked");
    };
    for (const input of [filesInput, folderInput].filter(Boolean)) {
        input.addEventListener("change", async () => {
            const entries = [...input.files].map(file => ({ file, path: file.webkitRelativePath || file.name }));
            input.value = "";
            await hand(entries);
        });
    }
    zone.querySelector(".ox-file-drop-folder")?.addEventListener("click", () => folderInput.click());
    trackDrag(zone, async e => {
        e.stopPropagation();
        await hand(await collect(e.dataTransfer));
    });
    guardDocument();
}

function trackDrag(zone, onDrop) {
    let depth = 0;
    const show = on => zone.classList.toggle("is-dragging", on);
    zone.addEventListener("dragenter", e => {
        e.preventDefault();
        depth++;
        show(true);
    });
    zone.addEventListener("dragleave", () => {
        if (--depth <= 0) {
            depth = 0;
            show(false);
        }
    });
    zone.addEventListener("dragover", e => e.preventDefault());
    zone.addEventListener("drop", e => {
        e.preventDefault();
        depth = 0;
        show(false);
        onDrop(e);
    });
}

// A file dropped beside a zone would otherwise replace the page.
function guardDocument() {
    if (documentGuarded) return;
    documentGuarded = true;
    document.addEventListener("dragover", e => e.preventDefault());
    document.addEventListener("drop", e => e.preventDefault());
}

async function collect(dataTransfer) {
    // The browser hands out entries only while the drop event runs, so all of them are taken before the first await.
    const entries = [...(dataTransfer?.items ?? [])]
        .filter(item => item.kind === "file")
        .map(item => item.webkitGetAsEntry?.() ?? item.getAsFile());
    const out = [];
    for (const entry of entries) {
        if (entry instanceof File) out.push({ file: entry, path: entry.name });
        else if (entry) await walk(entry, out);
    }
    return out;
}

async function walk(entry, out) {
    try {
        if (entry.isFile) {
            out.push({ file: await new Promise((ok, fail) => entry.file(ok, fail)), path: entry.fullPath });
            return;
        }
        // A folder is read in batches; an empty batch ends it.
        const reader = entry.createReader();
        for (let batch; (batch = await new Promise((ok, fail) => reader.readEntries(ok, fail))).length > 0;)
            for (const child of batch) await walk(child, out);
    } catch {
        // An entry the browser cannot read, for example one removed since the drop, is left out.
    }
}
