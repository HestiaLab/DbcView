const shortcutKey = "__dbcPaletteShortcut";
const paletteSelector = "[data-testid='dbc-command-palette']";

export function installShortcut(dotNetReference) {
  disposeShortcut();

  const handler = event => {
    if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    if (isEditable(event.target)) {
      return;
    }

    const shell = document.querySelector("[data-testid='dbc-view-shell']");
    if (!shell || shell.dataset.documentOpen !== "true") {
      return;
    }

    const palette = document.querySelector(paletteSelector);
    if (palette && palette.open) {
      return;
    }

    event.preventDefault();
    dotNetReference.invokeMethodAsync("OpenPaletteFromShortcut").catch(() => { });
  };

  window[shortcutKey] = { handler, dotNetReference };
  document.addEventListener("keydown", handler, true);
}

export function disposeShortcut() {
  const installed = window[shortcutKey];
  if (!installed) {
    return;
  }

  document.removeEventListener("keydown", installed.handler, true);
  delete window[shortcutKey];
}

export function showModal(dialog, input) {
  if (!dialog) {
    return;
  }

  if (!dialog.open) {
    dialog.showModal();
  }

  if (input && typeof input.focus === "function") {
    input.focus({ preventScroll: true });
  }
}

export function closeModal(dialog) {
  if (dialog && dialog.open) {
    dialog.close();
  }
}

export function scrollOptionIntoView(dialog, id) {
  const option = document.getElementById(id);
  if (dialog && option && dialog.contains(option)) {
    option.scrollIntoView({ block: "nearest" });
  }
}

function isEditable(target) {
  if (!target || !(target instanceof Element)) {
    return false;
  }

  if (target.isContentEditable) {
    return true;
  }

  return target.closest(
    "input, textarea, select, [contenteditable=''], [contenteditable='true']") !== null;
}
