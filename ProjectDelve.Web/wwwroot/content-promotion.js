// Presentation only: Engine/service supplies closure, collisions and package changes.
export function mountContentPromotion(root, { source, entries, stamp, call, published, failed }) {
  const document = root.ownerDocument ?? globalThis.document;
  let chosen = [], packageId = '', preview = null, busy = false, openingStamp = null, openingSource = null, notice = '';
  const node = (tag, text) => { const n = document.createElement(tag); if (text != null) n.textContent = text; return n; };
  const button = (id, text, action) => { const n = node('button', text); n.id = id; n.type = 'button'; n.disabled = busy; n.addEventListener('click', action); return n; };
  const guard = () => { if (stamp() !== openingStamp) throw new Error('The source changed. Reopen promotion and prepare a new preview.'); };
  const run = async action => { try { busy = true; render(); await action(); } catch (error) { failed(error); } finally { busy = false; render(); } };
  function render() {
    root.replaceChildren(); root.hidden = !openingSource && !notice;
    if (notice) root.append(node('p', notice));
    if (!openingSource) return;
    root.append(node('h3', 'Promote to Canonical'), node('p', 'Publish selected content and required dependencies as one new JSON package. Existing scenarios and Library history stay unchanged.'));
    if (!preview) {
      const input = node('input'); input.id = 'content-promotion-package'; input.value = packageId;
      input.addEventListener('input', () => { packageId = input.value; });
      const label = node('label', 'New canonical package ID'); label.append(input); root.append(label);
      for (const entry of entries().filter(e => ['equipment', 'lootCards'].includes(e.family) && e.origin === openingSource.origin && (!openingSource.packageId || e.packageId === openingSource.packageId))) {
        const check = node('input'); check.type = 'checkbox'; check.checked = chosen.includes(entry.key); check.disabled = busy;
        check.addEventListener('change', () => { chosen = check.checked ? [...chosen, entry.key] : chosen.filter(k => k !== entry.key); });
        const label = node('label', `${entry.name} (${entry.id})`); label.append(check); root.append(label);
      }
      root.append(button('content-promotion-preview', 'Preview promotion', () => run(async () => {
        guard();
        preview = await call('/promotion/preview', { source: openingSource.source, destinationPackageId: packageId,
          selected: chosen.map(key => { const [family, ...id] = key.split(':'); return { family: family === 'equipment' ? 'Equipment' : 'LootCard', typeId: id.join(':') }; }) });
      })));
    } else {
      root.append(node('p', `Source: ${preview.request.source.kind} · revision ${preview.request.source.revision ?? 'scenario snapshot'} · ${preview.sourceDigest}`),
        node('p', `Canonical destination revision: ${preview.destinationRevision}`));
      for (const [name, rows] of [['Selected', preview.request.selected], ['Required closure', preview.closure], ['Already canonical', preview.reusedCanonical], ['Additional definitions to publish', preview.additional]])
        root.append(node('h4', name), node('p', rows.map(id => `${id.family}: ${id.typeId}`).join(', ') || 'None'));
      const changes = node('details'); changes.append(node('summary', `New canonical package: ${preview.package.id}`), node('pre', JSON.stringify(preview.package, null, 2))); root.append(changes);
      root.append(button('content-promotion-publish', 'Confirm and publish to Canonical', () => run(async () => {
        guard(); const result = await call('/promotion/publish', { preview, confirm: true });
        notice = `Published ${result.packageId} to Canonical. Existing scenario pins are unchanged.`;
        openingSource = null; chosen = []; preview = null;
        await published(result);
      })));
    }
    root.append(button('content-promotion-cancel', 'Cancel promotion', () => { openingSource = null; preview = null; render(); }));
  }
  render();
  return { begin(entry) {
    const selected = source(entry); if (!selected) return;
    openingSource = { source: selected, origin: entry.origin, packageId: entry.packageId }; openingStamp = stamp();
    chosen = [entry.key]; preview = null; notice = '';
    packageId = `promoted-${entry.id}-${globalThis.crypto?.randomUUID?.().slice(0, 8) ?? Date.now()}`;
    render(); root.scrollIntoView?.({ block: 'start' });
  } };
}
