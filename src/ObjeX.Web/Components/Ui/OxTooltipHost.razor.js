// One tooltip element for the page, fixed to the viewport, so no scrolling grid or card clips it.
// Any element with data-ox-tooltip is an anchor; the text is read when it shows, so a re-render updates it.
// data-ox-tooltip-placement picks the side, data-ox-tooltip-truncated shows it only when the text is cut off,
// and an anchor whose CSS sets --ox-tooltip: off stays quiet (the expanded sidebar, where the label is visible).

const openDelay = 450;
const warmWindow = 400;
const gap = 6;
const edge = 4;

let tip = null;
let installed = false;
let anchor = null;
let pending = null;
let timer = 0;
let watch = 0;
let hiddenAt = 0;
let clicked = null;

export function init(element) {
    tip = element;
    if (installed) return;
    installed = true;

    document.addEventListener("pointerover", e => {
        if (e.pointerType === "touch") return;
        const a = anchorOf(e.target);
        if (a === clicked) return;
        clicked = null;
        if (a === anchor || a === pending) return;
        hide();
        if (a) schedule(a);
    }, true);
    document.documentElement.addEventListener("pointerleave", () => { clicked = null; hide(); });
    document.addEventListener("focusin", e => {
        const a = anchorOf(e.target);
        if (a && e.target.matches(":focus-visible")) { hide(); show(a); }
    }, true);
    document.addEventListener("focusout", e => { if (anchor?.contains(e.target)) hide(); }, true);
    document.addEventListener("pointerdown", e => { clicked = anchorOf(e.target); hide(); }, true);
    document.addEventListener("keydown", e => { if (e.key === "Escape") hide(); }, true);
    window.addEventListener("scroll", () => hide(), { capture: true, passive: true });
    window.addEventListener("resize", () => hide());
}

// The layout that owned the element is gone; the next layout brings its own.
export function release(element) {
    if (tip !== element) return;
    hide();
    tip = null;
}

function anchorOf(node) {
    return node instanceof Element ? node.closest("[data-ox-tooltip]") : null;
}

function schedule(a) {
    pending = a;
    // Moving from one anchor to the next shows the next one at once, like a menu bar.
    const delay = performance.now() - hiddenAt < warmWindow ? 0 : openDelay;
    timer = setTimeout(() => { pending = null; show(a); }, delay);
}

function show(a) {
    const text = a.getAttribute("data-ox-tooltip");
    if (!tip || !text || !a.isConnected) return;
    if (getComputedStyle(a).getPropertyValue("--ox-tooltip").trim() === "off") return;
    const box = boxOf(a);
    if (a.hasAttribute("data-ox-tooltip-truncated") && !isCut(box)) return;

    anchor = a;
    tip.textContent = text;
    tip.hidden = false;
    place(box, a.getAttribute("data-ox-tooltip-placement") || "top");
    tip.classList.add("is-visible");

    // An icon button already announces the same words through aria-label; describing it again would repeat them.
    const target = focusTarget(a, box);
    if (target.getAttribute("aria-label") !== text) {
        target.setAttribute("aria-describedby", tip.id);
        anchor.oxDescribed = target;
    }
    // Blazor may remove the anchor while the tooltip shows; nothing else would hide it then.
    watch = setInterval(() => { if (!anchor?.isConnected) hide(); }, 250);
}

function hide() {
    clearTimeout(timer);
    pending = null;
    if (!anchor) return;
    clearInterval(watch);
    anchor.oxDescribed?.removeAttribute("aria-describedby");
    anchor.oxDescribed = null;
    anchor = null;
    hiddenAt = performance.now();
    if (!tip) return;
    tip.classList.remove("is-visible");
    tip.hidden = true;
}

// A wrapper with display: contents has no box of its own; its first child stands in for it.
function boxOf(a) {
    return a.getClientRects().length === 0 && a.firstElementChild ? a.firstElementChild : a;
}

function focusTarget(a, box) {
    return a.matches(":focus") ? a : box;
}

// The ellipsis may sit on the element itself or on the cell that holds it.
function isCut(box) {
    for (let el = box, i = 0; el && i < 3; el = el.parentElement, i++) {
        if (el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 1) return true;
    }
    return false;
}

function place(box, preferred) {
    const a = box.getBoundingClientRect();
    tip.style.left = "0px";
    tip.style.top = "0px";
    const t = tip.getBoundingClientRect();
    const vw = window.innerWidth;
    const vh = window.innerHeight;

    const at = {
        top: () => ({ top: a.top - gap - t.height, left: a.left + (a.width - t.width) / 2 }),
        bottom: () => ({ top: a.bottom + gap, left: a.left + (a.width - t.width) / 2 }),
        right: () => ({ top: a.top + (a.height - t.height) / 2, left: a.right + gap }),
        left: () => ({ top: a.top + (a.height - t.height) / 2, left: a.left - gap - t.width })
    };
    const flip = { top: "bottom", bottom: "top", right: "left", left: "right" };
    const fits = p => p.top >= edge && p.left >= edge && p.top + t.height <= vh - edge && p.left + t.width <= vw - edge;

    let side = at[preferred] ? preferred : "top";
    let pos = at[side]();
    if (!fits(pos) && fits(at[flip[side]]())) {
        side = flip[side];
        pos = at[side]();
    }
    tip.dataset.side = side;
    tip.style.left = `${Math.round(Math.min(Math.max(edge, pos.left), vw - t.width - edge))}px`;
    tip.style.top = `${Math.round(Math.min(Math.max(edge, pos.top), vh - t.height - edge))}px`;
}
