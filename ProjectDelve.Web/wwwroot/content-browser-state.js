// Inspection navigation only. Content semantics and validation come from the Engine.
export function contentLabel(value) {
  const text = String(value ?? "");
  if (/^(mov|rng|atk|def|hp|los)$/i.test(text)) return text.toUpperCase();
  return text.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[_-]+/g, " ")
    .replace(/^./, first => first.toUpperCase());
}

export function contentUsage(usage) {
  if (typeof usage === "string") return usage;
  if (usage && Object.hasOwn(usage, "maxUses"))
    return usage.maxUses == null ? "Unlimited" : `${usage.maxUses} / game`;
  return "Not specified";
}

export class ContentBrowserState {
  constructor() {
    this.catalog = null;
    this.query = "";
    this.category = "all";
    this.origin = "all";
    this.selectedKey = null;
    this.detailOpen = false;
    this.history = [];
  }
  load(catalog) {
    this.catalog = catalog;
    this.query = ""; this.category = "all"; this.origin = "all";
    this.history = []; this.detailOpen = false;
    this.selectedKey = this.entries[0]?.key ?? null;
  }
  get entries() { return this.catalog?.entries ?? []; }
  get selected() { return this.entries.find(entry => entry.key === this.selectedKey) ?? null; }
  matches(entry, { category = this.category, origin = this.origin } = {}) {
    const words = this.query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    const haystack = `${entry.name} ${entry.id}`.toLocaleLowerCase();
    return (category === "all" || entry.family === category) && (origin === "all" || entry.origin === origin)
      && words.every(word => haystack.includes(word));
  }
  get visible() { return this.entries.filter(entry => this.matches(entry)); }
  get categories() {
    const supplied = this.catalog?.categories ?? [];
    const known = supplied.map(category => typeof category === "string" ? { id: category, name: contentLabel(category) } : category);
    for (const family of new Set(this.entries.map(entry => entry.family)))
      if (!known.some(category => category.id === family)) known.push({ id: family, name: contentLabel(family) });
    return known.map(category => ({ ...category,
      count: this.entries.filter(entry => entry.family === category.id && this.matches(entry, { category: "all" })).length }));
  }
  get origins() { return [...new Set(this.entries.map(entry => entry.origin))]; }
  filter(values) {
    Object.assign(this, values);
    this.history = [];
    if (!this.visible.some(entry => entry.key === this.selectedKey)) this.selectedKey = this.visible[0]?.key ?? null;
    this.detailOpen = false;
  }
  open(key, reference = false) {
    const entry = this.entries.find(entry => entry.key === key);
    if (!entry) return false;
    if (reference && this.selectedKey && this.selectedKey !== key)
      this.history.push({ key: this.selectedKey, query: this.query, category: this.category, origin: this.origin });
    else if (!reference) this.history = [];
    if (!this.matches(entry)) { this.query = ""; this.category = "all"; this.origin = "all"; }
    this.selectedKey = key; this.detailOpen = true;
    return true;
  }
  back() {
    const previous = this.history.pop();
    if (previous) {
      this.query = previous.query; this.category = previous.category; this.origin = previous.origin;
      this.selectedKey = previous.key; this.detailOpen = true;
    } else this.detailOpen = false;
  }
  mechanic(id) { return this.entries.find(entry => entry.mechanic && entry.id === id); }
}
