const guardProperty = "__dbcTableKeyboardGuard";

const handledKeys = new Set([
    "ArrowUp",
    "ArrowDown",
    "ArrowLeft",
    "ArrowRight",
    "Home",
    "End",
    "PageUp",
    "PageDown",
]);

export function installKeyboardGuard(container) {
    if (!container || container[guardProperty]) {
        return;
    }

    container[guardProperty] = true;
    container.addEventListener(
        "keydown",
        event => {
            if (handledKeys.has(event.key)) {
                event.preventDefault();
            }
        },
        true);
}

export function scrollToRow(container, index, itemSize) {
    if (!container) {
        return;
    }

    const row = container.querySelector(`[data-row-index="${index}"]`);
    if (row) {
        row.scrollIntoView({ block: "nearest" });
        return;
    }

    const viewport = container.clientHeight || 0;
    const target = Math.max(
        0,
        index * itemSize - Math.max(0, viewport - itemSize) / 2);
    container.scrollTop = target;
}

export function focusRow(container, index, timeoutMs = 1000) {
    if (!container) {
        return Promise.resolve(false);
    }

    let observer = null;
    let timer = null;
    let settled = false;
    const cleanup = () => {
        if (observer) {
            observer.disconnect();
        }

        if (timer) {
            clearTimeout(timer);
        }
    };

    const tryFocus = () => {
        const row = container.querySelector(`[data-row-index="${index}"]`);
        const target = row ? row.querySelector("[data-row-focus]") : null;
        if (!target) {
            return false;
        }

        target.focus({ preventScroll: true });
        return true;
    };

    return new Promise(resolve => {
        if (tryFocus()) {
            resolve(true);
            return;
        }

        observer = new MutationObserver(() => {
            if (settled) {
                return;
            }

            if (tryFocus()) {
                settled = true;
                cleanup();
                resolve(true);
            }
        });
        observer.observe(container, { childList: true, subtree: true });

        timer = setTimeout(() => {
            if (settled) {
                return;
            }

            settled = true;
            cleanup();
            resolve(tryFocus());
        }, timeoutMs);
    });
}

export function downloadText(fileName, text) {
    const bytes = new TextEncoder().encode(text);
    const blob = new Blob([bytes], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

export function selectTextRange(textarea, start, length) {
    if (!textarea) {
        return;
    }

    const value = textarea.value || "";
    const from = Math.max(0, Math.min(start, value.length));
    const to = Math.max(from, Math.min(start + length, value.length));
    textarea.focus({ preventScroll: true });
    textarea.setSelectionRange(from, to);

    const line = value.slice(0, from).split("\n").length;
    const lineHeight = Number.parseFloat(getComputedStyle(textarea).lineHeight) || 18;
    textarea.scrollTop = Math.max(
        0,
        (line - 1) * lineHeight - Math.max(0, textarea.clientHeight) / 3);
}

export function watchScrollbarGutter(table, body) {
    if (!table || !body) {
        return;
    }

    const sync = () => {
        const gutter = Math.max(0, body.offsetWidth - body.clientWidth);
        table.style.setProperty("--dbc-scrollbar-gutter", `${gutter}px`);
    };

    sync();
    if (body.__dbcScrollbarObserver) {
        return;
    }

    const observer = new ResizeObserver(sync);
    observer.observe(body);
    body.__dbcScrollbarObserver = observer;
}
