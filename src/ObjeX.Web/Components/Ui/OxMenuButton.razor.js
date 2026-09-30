// One entry per open menu, keyed by its anchor, so closing or disposing one menu never touches another.
const open = new Map();

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

    stop(anchor);
    const close = () => { stop(anchor); owner.invokeMethodAsync("CloseFromBrowser"); };
    // Tab or a click elsewhere moves the focus out of the panel; the menu must not stay open behind it.
    const onFocusOut = e => { if (!panel.contains(e.relatedTarget)) close(); };
    panel.addEventListener("focusout", onFocusOut);
    window.addEventListener("scroll", close, { capture: true, passive: true });
    window.addEventListener("resize", close);
    open.set(anchor, () => {
        panel.removeEventListener("focusout", onFocusOut);
        window.removeEventListener("scroll", close, { capture: true });
        window.removeEventListener("resize", close);
    });
}

// Closing through the menu itself hands the focus back to its button.
export function release(anchor) {
    stop(anchor);
    anchor?.querySelector("button")?.focus();
}

export function stop(anchor) {
    open.get(anchor)?.();
    open.delete(anchor);
}
