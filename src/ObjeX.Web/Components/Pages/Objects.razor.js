export function initDropZone(dropZone, inputFile) {
    let dragCounter = 0;
    const overlay = document.createElement("div");
    overlay.style.cssText = "position:absolute;inset:0;z-index:10;background:var(--rz-primary);opacity:0.06;border:2px dashed var(--rz-primary);border-radius:var(--rz-border-radius);pointer-events:none;display:none";
    dropZone.appendChild(overlay);

    dropZone.addEventListener("dragenter", e => {
        e.preventDefault();
        dragCounter++;
        overlay.style.display = "block";
    });

    dropZone.addEventListener("dragleave", () => {
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            overlay.style.display = "none";
        }
    });

    dropZone.addEventListener("dragover", e => e.preventDefault());

    dropZone.addEventListener("drop", e => {
        e.preventDefault();
        dragCounter = 0;
        overlay.style.display = "none";
        if (e.dataTransfer?.files?.length > 0) {
            inputFile.files = e.dataTransfer.files;
            inputFile.dispatchEvent(new Event("change", { bubbles: true }));
        }
    });

    document.addEventListener("dragover", e => e.preventDefault());
    document.addEventListener("drop", e => e.preventDefault());
}
