/**
 * The small pieces of dialog chrome the settings and help modals share.
 *
 * Both dialogs are overlays mounted into a container rather than `<dialog>` elements, because the client
 * has to keep working in the same static bundle on browsers that predate the element and because the
 * end-of-match card already establishes the pattern (a `.modal` overlay with a `.modal-card` inside).
 * The parts that are identical in both — the overlay element, the two ways to dismiss it, and putting
 * the focus back where it was — live here so the two modals only own their *content*.
 */

/** The class name of a modal overlay; `.modal` is also what the stylesheet dresses. */
export const OverlayClassName = "modal modal--dialog";

/**
 * The element that had focus when a dialog opened, so it can be focused again when the dialog closes.
 *
 * Duck-typed rather than `instanceof HTMLElement`: the check has to work for a document from another
 * realm (an iframe, a test double) and must not depend on a browser global being defined at all.
 */
export function focusableActiveElement(doc: Document): HTMLElement | null {
  const active: unknown = doc.activeElement;
  if (active == null || typeof active !== "object") return null;
  return typeof (active as { focus?: unknown }).focus === "function" ? (active as HTMLElement) : null;
}

/**
 * Builds the modal overlay for one dialog: fixed, centred, `aria-modal`, tagged with a stable
 * `data-dialog` name so the CSS and a test can tell the two dialogs apart.
 */
export function createOverlay(doc: Document, dialogName: string, label: string): HTMLElement {
  const overlay = doc.createElement("div");
  overlay.className = OverlayClassName;
  overlay.setAttribute("role", "dialog");
  overlay.setAttribute("aria-modal", "true");
  overlay.setAttribute("aria-label", label);
  overlay.dataset["dialog"] = dialogName;
  return overlay;
}

/**
 * Wires the two ways a mouse or keyboard user dismisses a dialog: a click on the overlay itself (not on
 * the card inside it) and Escape from anywhere inside the card. Returns nothing; the handler is
 * attached for the lifetime of the overlay.
 */
export function dismissOnBackdropAndEscape(
  overlay: HTMLElement,
  card: HTMLElement,
  dismiss: () => void,
): void {
  overlay.addEventListener("click", (event) => {
    if (event.target === overlay) dismiss();
  });

  card.addEventListener("keydown", (event) => {
    if ((event as KeyboardEvent).key !== "Escape") return;
    event.preventDefault();
    dismiss();
  });
}
