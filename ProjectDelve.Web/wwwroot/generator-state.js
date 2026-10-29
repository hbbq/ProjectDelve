// Setup/authoring contract only. The C# generator owns generation and validation.
export function configurationCode(config, includeSeed = true) {
  const integer = (key, min, max) => {
    const value = Number(config[key]);
    if (!Number.isInteger(value) || value < min || value > max) throw new Error(`${key} must be a whole number from ${min} to ${max}.`);
    return value;
  };
  const width = integer("width", 14, 50), height = integer("height", 14, 50), rooms = integer("rooms", 2, 24);
  if (rooms > Math.floor((width - 2) / 6) * Math.floor((height - 2) / 6)) throw new Error("The rooms do not fit. Increase dimensions or reduce rooms; each region needs 6 × 6 Cells.");
  const connectivity = integer("connectivity", 0, 3), doors = integer("doors", 0, 3), density = integer("density", 1, 3), difficulty = integer("difficulty", 1, 5);
  const excavation = integer("excavation", 1, 3);
  const objective = config.objective === "eliminate" ? "OEE" : `OR${config.required === "4" ? "A" : config.required}`;
  if (!["OEE", "ORA", "OR1", "OR2", "OR3"].includes(objective)) throw new Error("Choose the required Hero count.");
  const loot = config.loot ?? "Classic";
  if (!["Classic", "Fun", "Experimental", "FarOut"].includes(loot)) throw new Error("Choose a Loot preset.");
  const prefix = `DNG1-HVM-${width}x${height}-R${rooms}-E${excavation}-C${connectivity}-D${doors}-N${density}-L${difficulty}-${objective}-F${config.fog === "0" ? 0 : 1}${loot === "Classic" ? "" : `-Q${loot}`}`;
  const seed = (config.seed ?? "").trim();
  if (includeSeed && seed && !/^[0-9a-f]{16}$/i.test(seed)) throw new Error("Seed must contain exactly 16 hexadecimal digits, or be blank.");
  return prefix + (includeSeed && seed ? `-S${seed.toUpperCase()}` : "");
}
export function configurationFromCode(input) {
  const match = input.trim().match(/^DNG1-HVM-([1-9][0-9]?)x([1-9][0-9]?)-R([1-9][0-9]?)-E([1-3])-C([0-3])-D([0-3])-N([1-3])-L([1-5])-(OEE|ORA|OR[1-4])-F([01])(?:-Q(Classic|Fun|Experimental|FarOut))?(?:-S([0-9a-fA-F]{16}))?$/);
  if (!match || !input.trim().startsWith("DNG1-HVM-")) throw new Error("Enter an ordered DNG1-HVM prefix or complete code. A seed needs exactly 16 hexadecimal digits.");
  const result = Object.fromEntries(["width", "height", "rooms", "excavation", "connectivity", "doors", "density", "difficulty"].map((key, i) => [key, match[i + 1]]));
  const objective = match[9].toUpperCase();
  Object.assign(result, { objective: objective === "OEE" ? "eliminate" : "reach", required: objective === "OR4" || objective === "ORA" ? "A" : objective === "OEE" ? "2" : objective.slice(2), fog: match[10], loot: match[11] ?? "Classic", seed: match[12]?.toUpperCase() ?? "" });
  configurationCode(result); return result;
}

// A result is immutable until explicitly generated again. Edits invalidate requests, not previews.
export class GeneratorSession {
  sequence = 0;
  result = null;
  signature = null;
  changed() { this.sequence++; }
  begin(signature) { return { sequence: ++this.sequence, signature }; }
  accept(ticket, result) {
    if (ticket.sequence !== this.sequence) return false;
    this.result = structuredClone(result); this.signature = ticket.signature; return true;
  }
  outdated(signature) { return !!this.result && this.signature !== signature; }
}
