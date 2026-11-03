// Presentation-only navigation. Existing game and Designer nodes stay mounted.
const byId = id => document.getElementById(id);
const menu = byId('game-menu');
const tools = byId('playtest-tools');
const openMenu = byId('open-game-menu');
const closeMenu = () => menu.close();
openMenu.addEventListener('click', () => { tools.open = false; menu.showModal(); });
byId('close-game-menu').addEventListener('click', closeMenu);
byId('resume-game').addEventListener('click', closeMenu);
menu.addEventListener('close', () => {
  if (!byId('playtest-main').hidden) (tools.open ? byId('close-game-tools') : openMenu).focus();
});
for (const link of menu.querySelectorAll('a')) link.addEventListener('click', closeMenu);
byId('menu-settings').addEventListener('click', () => {
  closeMenu(); tools.open = true; byId('close-game-tools').focus();
});
byId('close-game-tools').addEventListener('click', () => { tools.open = false; byId('tools-toggle').focus(); });
for (const button of document.querySelectorAll('[data-inspector-tab]')) {
  button.addEventListener('click', () => {
    const tab = button.dataset.inspectorTab;
    for (const peer of document.querySelectorAll('[data-inspector-tab]')) {
      const selected = peer.dataset.inspectorTab === tab;
      peer.setAttribute('aria-pressed', String(selected));
      byId(peer.getAttribute('aria-controls')).hidden = !selected;
    }
    // Let the existing choice renderer expose actions whose Unit card is hidden.
    globalThis.dispatchEvent(new CustomEvent('delve-inspector-change', { detail: { tab } }));
  });
}
globalThis.addEventListener('hashchange', () => {
  if (menu.open) closeMenu();
  tools.open = false;
});
