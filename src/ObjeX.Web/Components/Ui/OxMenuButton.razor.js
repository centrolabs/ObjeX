let onScroll = null;

// Puts the panel under the button, right edges aligned; above it when the viewport has no room below.
export function place(anchor, panel, owner) {
    const gap = 4;
    const a = anchor.getBoundingClientRect();
    const p = panel.getBoundingClientRect();
    const top = a.bottom + gap + p.height > window.innerHeight ? Math.max(gap, a.top - gap - p.height) : a.bottom + gap;
    const left = Math.min(Math.max(gap, a.right - p.width), window.innerWidth - p.width - gap);
    panel.style.top = `${top}px`;
    panel.style.left = `${left}px`;
    panel.style.visibility = "visible";

    const items = () => [...panel.querySelectorAll('[role="menuitem"]:not(:disabled)')];
    items()[0]?.focus();
    panel.addEventListener("keydown", e => {
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
        e.preventDefault();
        const list = items();
        const next = list.indexOf(document.activeElement) + (e.key === "ArrowDown" ? 1 : -1);
        list[(next + list.length) % list.length]?.focus();
    });

    stopListening();
    onScroll = () => { stopListening(); owner.invokeMethodAsync("CloseFromScroll"); };
    window.addEventListener("scroll", onScroll, { capture: true, passive: true });
    window.addEventListener("resize", onScroll);
}

export function release(anchor) {
    stopListening();
    anchor?.querySelector("button")?.focus();
}

function stopListening() {
    if (!onScroll) return;
    window.removeEventListener("scroll", onScroll, { capture: true });
    window.removeEventListener("resize", onScroll);
    onScroll = null;
}
