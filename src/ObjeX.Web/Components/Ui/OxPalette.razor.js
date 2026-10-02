// Cmd+K or Ctrl+K toggles the palette from anywhere, also inside a text field; Firefox binds it to its own search, so
// the key is taken. Escape closes an open palette wherever the focus is. Inside the palette's field the arrows and Tab
// move the choice instead of the caret or the focus.

export function listen(owner) {
    const onKey = e => {
        if ((e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === "k") {
            e.preventDefault();
            owner.invokeMethodAsync("ToggleFromBrowser");
        } else if (e.key === "Escape" && document.querySelector(".ox-palette")) {
            e.preventDefault();
            owner.invokeMethodAsync("CloseFromBrowser");
        }
    };
    document.addEventListener("keydown", onKey);
    return { stop: () => document.removeEventListener("keydown", onKey) };
}

export function isMac() {
    return /mac|iphone|ipad/i.test(navigator.userAgentData?.platform ?? navigator.platform ?? "");
}

let returnTo = null;

// The field is new each time the palette opens, so its listeners go with it. A click anywhere in the palette leaves the
// focus in the field, so the keys keep working; the click itself still picks the result.
export function attach(input, owner) {
    returnTo = document.activeElement;
    input.closest(".ox-palette")?.addEventListener("mousedown", e => {
        if (e.target !== input) e.preventDefault();
    });
    input.addEventListener("keydown", e => {
        const step = e.key === "Tab" ? (e.shiftKey ? -1 : 1) : { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (step) {
            e.preventDefault();
            owner.invokeMethodAsync("MoveFromBrowser", step);
        } else if (e.key === "Enter") {
            e.preventDefault();
            owner.invokeMethodAsync("PickFromBrowser");
        }
    });
    input.focus();
}

// Closing hands the focus back to where it was, for example the row of the grid; after a pick that left the page it is gone.
export function restore() {
    if (returnTo?.isConnected) returnTo.focus();
    returnTo = null;
}

export function reveal(list) {
    list?.querySelector(".is-active")?.scrollIntoView({ block: "nearest" });
}
