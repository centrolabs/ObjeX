// Cmd+K or Ctrl+K toggles the palette from anywhere, also inside a text field; Firefox binds it to its own search, so
// the key is taken. Inside the palette's field the arrows move the choice instead of the caret.

export function listen(owner) {
    const onKey = e => {
        if ((e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === "k") {
            e.preventDefault();
            owner.invokeMethodAsync("ToggleFromBrowser");
        }
    };
    document.addEventListener("keydown", onKey);
    return { stop: () => document.removeEventListener("keydown", onKey) };
}

export function isMac() {
    return /mac|iphone|ipad/i.test(navigator.userAgentData?.platform ?? navigator.platform ?? "");
}

// The field is new each time the palette opens, so its listener goes with it.
export function attach(input, owner) {
    input.addEventListener("keydown", e => {
        const step = { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (step) {
            e.preventDefault();
            owner.invokeMethodAsync("MoveFromBrowser", step);
        } else if (e.key === "Enter") {
            e.preventDefault();
            owner.invokeMethodAsync("PickFromBrowser");
        } else if (e.key === "Escape") {
            e.preventDefault();
            owner.invokeMethodAsync("CloseFromBrowser");
        }
    });
    input.focus();
}

export function reveal(list) {
    list?.querySelector(".is-active")?.scrollIntoView({ block: "nearest" });
}
