// Presentation candidates are the only source of available advance actions.
export function advanceChoices(decision, visible) {
  const choices = [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])].filter(visible);
  return { stay: choices.find(choice => choice.boardVisual?.kind === "stay"),
    endTurn: choices.find(choice => choice.isEndTurn) };
}

export function bindAdvanceControls({ document, stayButton, endTurnButton, choices, busy, selecting, active, submit, tooltip }) {
  const buttons = { stay: stayButton, endTurn: endTurnButton };
  const hint = kind => kind === "endTurn" ? "Space" : choices().endTurn ? "S" : "S or Space";
  for (const [kind, button] of Object.entries(buttons)) {
    tooltip(button, () => `${button.textContent} (${hint(kind)})`);
    button.addEventListener("click", () => {
      const choice = choices()[kind];
      if (!busy() && choice) submit(choice.key);
    });
  }
  let boardSpace;
  const pointers = new Set();
  document.addEventListener("pointerdown", event => {
    pointers.add(event.pointerId);
    if (boardSpace) boardSpace.gesture = true;
  }, true);
  for (const type of ["pointerup", "pointercancel"]) document.addEventListener(type, event => pointers.delete(event.pointerId), true);
  document.defaultView?.addEventListener("blur", () => { boardSpace = null; pointers.clear(); });
  const blocked = event => {
    const target = event.target;
    return event.isComposing || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey ||
      busy() || !active() || selecting() || pointers.size > 0 || target?.isContentEditable ||
      target?.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"]), [role="menu"], [role="listbox"], [role="slider"], [role="textbox"], details[open]') ||
      document.querySelector('dialog[open], [role="dialog"]:not([hidden]), [role="menu"]:not([hidden]), [popover]:popover-open, #playtest-tools[open], .view-disclosure[open]');
  };
  // Capture before the viewport's Space navigation handler. Keep Space-drag available:
  // a plain board Space advances on release, while a pointer gesture cancels advancement.
  document.addEventListener("keydown", event => {
    if (boardSpace && event.key === " ") { event.preventDefault(); return; }
    if (event.defaultPrevented || event.repeat || blocked(event)) return;
    const key = event.key.toLowerCase();
    if (key !== "s" && key !== " ") return;
    const target = event.target;
    const boardKeyboard = target?.matches?.('#camera-viewport, #three-viewport, .three-focus');
    if (key === " " && !boardKeyboard && target?.closest?.('button, a, summary, [role="button"]')) return;
    const { stay, endTurn } = choices();
    const choice = key === "s" ? stay : endTurn ?? stay;
    if (!choice) return;
    event.preventDefault();
    if (key === " " && boardKeyboard) {
      boardSpace = { choice, target, gesture: false };
      return;
    }
    submit(choice.key);
  }, true);
  document.addEventListener("keyup", event => {
    if (event.key !== " " || !boardSpace) return;
    const pending = boardSpace; boardSpace = null;
    // The viewport must reset its camera modifier, but must not also select a Cell.
    event.preventDefault();
    if (pending.gesture || event.target !== pending.target || blocked(event)) return;
    const { stay, endTurn } = choices();
    if ((endTurn ?? stay) === pending.choice) submit(pending.choice.key);
  }, true);
  return () => {
    const available = choices();
    for (const [kind, button] of Object.entries(buttons)) {
      const choice = available[kind];
      button.hidden = !choice;
      button.disabled = busy() || !choice;
      button.textContent = kind === "stay" ? "Stay" : choice?.label ?? "End Turn";
      button.setAttribute("aria-keyshortcuts", kind === "endTurn" ? "Space" : available.endTurn ? "S" : "S Space");
    }
  };
}
