// Recipe authoring only; the server owns terrain generation and content validation.
export function worldConfigurationCode(config, includeSeed = true, maxBoardSize = 120) {
  const integer = (key, min, max) => {
    const raw = String(config[key] ?? "").trim(), value = Number(raw);
    if (!/^-?\d+$/.test(raw) || !Number.isInteger(value) || value < min || value > max)
      throw new Error(`${key} must be a whole number from ${min} to ${max}.`);
    return value;
  };
  const width = integer("width", 1, maxBoardSize), height = integer("height", 1, maxBoardSize);
  const x = integer("offsetX", -2147483648, 2147483647), y = integer("offsetY", -2147483648, 2147483647);
  if (x + width - 1 > 2147483647 || y + height - 1 > 2147483647)
    throw new Error("The entire window must fit within signed 32-bit world coordinates.");
  const forest = integer("forestCoverage", 0, 100), water = integer("waterCoverage", 0, 100), rock = integer("rockCoverage", 0, 100);
  if (forest + water + rock > 100) throw new Error("Forest, water and rock coverage must total at most 100%.");
  if (!["Fine", "Balanced", "Broad"].includes(config.scale)) throw new Error("Choose a terrain scale.");
  if (!["Auto", "Lakes", "River", "Mixed"].includes(config.waterForm)) throw new Error("Choose a water form.");
  const seed = String(config.seed ?? "").trim();
  if (includeSeed && seed && !/^[0-9a-f]{16}$/i.test(seed)) throw new Error("Seed must contain exactly 16 hexadecimal digits, or be blank.");
  return `WLD1-${width}x${height}-X${x}-Y${y}-F${forest}-W${water}-R${rock}-K${config.scale}-H${config.waterForm}${includeSeed && seed ? `-S${seed.toUpperCase()}` : ""}`;
}

export function worldConfigurationFromCode(input, maxBoardSize = 120) {
  const match = input.trim().match(/^WLD1-([1-9]\d{0,2})x([1-9]\d{0,2})-X(-?\d+)-Y(-?\d+)-F(\d{1,3})-W(\d{1,3})-R(\d{1,3})-K(Fine|Balanced|Broad)-H(Auto|Lakes|River|Mixed)(?:-S([0-9a-fA-F]{16}))?$/);
  if (!match) throw new Error("Enter an ordered WLD1 recipe or complete code; a seed needs exactly 16 hexadecimal digits.");
  const result = Object.fromEntries(["width", "height", "offsetX", "offsetY", "forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm", "seed"].map((key, i) => [key, match[i + 1] ?? ""]));
  worldConfigurationCode(result, true, maxBoardSize);
  result.seed = result.seed.toUpperCase();
  return result;
}
