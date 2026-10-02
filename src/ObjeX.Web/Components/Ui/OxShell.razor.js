// The sidebar's edge: drag to resize, drag past the threshold to collapse or out of the rail to expand, double-click to reset.
// A plain click does nothing, so a double-click never toggles on its way; the round button on the edge toggles.
// During a drag only the aside's own inline width changes (Blazor renders no style there), so every frame stays in the
// browser; the circuit hears about it once, on release.

const stops = new Map();
const clickSlop = 3;

export function init(side, edge, owner, minWidth, maxWidth) {
    const handle = edge.querySelector(".ox-shell-handle");
    const rail = () => parseFloat(getComputedStyle(side).getPropertyValue("--ox-size-sidebar-rail")) || 64;
    // Below the middle between rail and minimum the sidebar shows as the rail, and is dropped as one.
    const threshold = () => (rail() + minWidth) / 2;

    let drag = null;

    const onDown = e => {
        if (e.button !== 0) return;
        handle.setPointerCapture(e.pointerId);
        drag = { x: e.clientX, width: side.getBoundingClientRect().width, moved: false, final: null };
    };

    const onMove = e => {
        if (!drag) return;
        const dx = e.clientX - drag.x;
        if (!drag.moved && Math.abs(dx) < clickSlop) return;
        if (!drag.moved) {
            drag.moved = true;
            side.style.transition = "none";
            document.body.style.cursor = "col-resize";
            document.body.style.userSelect = "none";
            edge.dataset.dragging = "";
        }
        const wanted = drag.width + dx;
        drag.final = wanted < threshold() ? rail() : Math.round(Math.min(maxWidth, Math.max(minWidth, wanted)));
        side.style.width = `${drag.final}px`;
    };

    const onUp = async e => {
        if (!drag) return;
        const { moved, final } = drag;
        drag = null;
        handle.releasePointerCapture?.(e.pointerId);
        if (!moved) return;
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
        delete edge.dataset.dragging;
        const collapsed = final === rail();
        // The inline width holds the dropped size until Blazor has rendered the same one from the stored preference.
        try {
            await owner.invokeMethodAsync("ResizedFromBrowser", collapsed, collapsed ? 0 : final);
        } finally {
            side.style.width = "";
            side.style.transition = "";
            window.dispatchEvent(new Event("resize"));
        }
    };

    const onDouble = () => owner.invokeMethodAsync("ResetFromBrowser");

    // Charts measure themselves on window resize only; the sidebar's animation changes their width without one.
    const onTransitionEnd = e => {
        if (e.target === side && e.propertyName === "width") window.dispatchEvent(new Event("resize"));
    };

    handle.addEventListener("pointerdown", onDown);
    handle.addEventListener("pointermove", onMove);
    handle.addEventListener("pointerup", onUp);
    handle.addEventListener("pointercancel", onUp);
    handle.addEventListener("dblclick", onDouble);
    side.addEventListener("transitionend", onTransitionEnd);

    stops.set(edge, () => {
        handle.removeEventListener("pointerdown", onDown);
        handle.removeEventListener("pointermove", onMove);
        handle.removeEventListener("pointerup", onUp);
        handle.removeEventListener("pointercancel", onUp);
        handle.removeEventListener("dblclick", onDouble);
        side.removeEventListener("transitionend", onTransitionEnd);
    });
}

export function stop(edge) {
    stops.get(edge)?.();
    stops.delete(edge);
}
