// Presentation only. Callers supply content from projections or existing disclosures.
let tooltip, owner, timer;
const hoverDelay = 180;

export function tooltipPosition(anchor, size, viewport) {
  const gap = 8;
  const left = Math.max(gap, Math.min(anchor.left, viewport.width - size.width - gap));
  const below = anchor.bottom + gap;
  const top = below + size.height <= viewport.height - gap ? below : anchor.top - size.height - gap;
  return { left, top: Math.max(gap, Math.min(top, viewport.height - size.height - gap)) };
}

function dismissTooltip() {
  clearTimeout(timer);
  if (owner) {
    const ids = (owner.getAttribute("aria-describedby") ?? "").split(/\s+/).filter(id => id && id !== "shared-tooltip");
    if (ids.length) owner.setAttribute("aria-describedby", ids.join(" "));
    else owner.removeAttribute("aria-describedby");
  }
  owner = null;
  if (tooltip) tooltip.hidden = true;
}

export function bindTooltip(target, content) {
  // This attribute also makes the projected content available without native title UI.
  target.dataset.tooltip = content();
  let hovered = false, focused = false;
  const reveal = () => {
    dismissTooltip();
    timer = setTimeout(() => {
      if (!target.isConnected || (!hovered && !focused)) return;
      const value = content();
      if (!value) return;
      owner = target;
      tooltip.textContent = value;
      tooltip.hidden = false;
      const position = tooltipPosition(target.getBoundingClientRect(), tooltip.getBoundingClientRect(),
        { width: document.documentElement.clientWidth, height: document.documentElement.clientHeight });
      tooltip.style.left = `${position.left}px`;
      tooltip.style.top = `${position.top}px`;
      const ids = new Set((target.getAttribute("aria-describedby") ?? "").split(/\s+/).filter(Boolean));
      ids.add(tooltip.id);
      target.setAttribute("aria-describedby", [...ids].join(" "));
    }, hoverDelay);
  };
  target.addEventListener("pointerenter", event => { if (event.pointerType !== "touch") { hovered = true; reveal(); } });
  target.addEventListener("pointerleave", () => { hovered = false; if (!focused) dismissTooltip(); });
  target.addEventListener("focus", () => { focused = true; reveal(); });
  target.addEventListener("blur", () => { focused = false; if (!hovered) dismissTooltip(); });
  // Never cancel a control's native click, including details/summary on touch.
}

export function initializeTooltips() {
  tooltip = document.createElement("div");
  tooltip.id = "shared-tooltip"; tooltip.className = "shared-tooltip";
  tooltip.setAttribute("role", "tooltip"); tooltip.hidden = true;
  document.body.append(tooltip);
  for (const target of document.querySelectorAll("[data-tooltip-source]"))
    bindTooltip(target, () => {
      const source = document.getElementById(target.dataset.tooltipSource);
      return source?.children?.length
        ? [...source.children].map(child => child.textContent).join("\n") : source?.textContent ?? "";
    });
  document.addEventListener("keydown", event => { if (event.key === "Escape") dismissTooltip(); });
  document.addEventListener("pointerdown", dismissTooltip, true);
  document.addEventListener("scroll", dismissTooltip, true);
  document.addEventListener("toggle", dismissTooltip, true);
  globalThis.addEventListener?.("resize", dismissTooltip);
  // Cards and board targets can be replaced while a tooltip is visible.
  if (globalThis.MutationObserver) new MutationObserver(() => {
    if (owner && !owner.isConnected) dismissTooltip();
  }).observe(document.body, { childList: true, subtree: true });
}
