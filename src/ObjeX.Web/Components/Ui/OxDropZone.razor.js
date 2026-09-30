export function initDropZone(dropZone, inputFile) {
    let dragCounter = 0;
    const show = on => dropZone.classList.toggle("is-dragging", on);

    dropZone.addEventListener("dragenter", e => {
        e.preventDefault();
        dragCounter++;
        show(true);
    });

    dropZone.addEventListener("dragleave", () => {
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            show(false);
        }
    });

    dropZone.addEventListener("dragover", e => e.preventDefault());

    dropZone.addEventListener("drop", e => {
        e.preventDefault();
        dragCounter = 0;
        show(false);
        if (e.dataTransfer?.files?.length > 0) {
            inputFile.files = e.dataTransfer.files;
            inputFile.dispatchEvent(new Event("change", { bubbles: true }));
        }
    });

    document.addEventListener("dragover", e => e.preventDefault());
    document.addEventListener("drop", e => e.preventDefault());
}
