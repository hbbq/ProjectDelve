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
  const bit = key => {
    const value = String(config[key]);
    if (!["true", "false"].includes(value)) throw new Error(`Choose ${key}.`);
    return value === "true" ? "1" : "0";
  };
  const density = key => {
    if (!["None", "Low", "Medium", "High", "VeryHigh"].includes(config[key]))
      throw new Error(`Choose ${key}: None, Low, Medium, High or Very High.`);
    return config[key];
  };
  let enrichment = "", civilization = "", routes = "", population = "";
  if (config.enrichmentEnabled != null && config.enrichmentEnabled !== "") {
    enrichment = `-E${bit("enrichmentEnabled")}-I${integer("enrichmentIntensity", 1, 3)}-B${["forestEnrichment", "rockEnrichment", "waterEnrichment", "grasslandEnrichment"].map(bit).join("")}`;
  }
  if (config.civilizationEnabled != null && config.civilizationEnabled !== "")
    civilization = `-C${bit("civilizationEnabled")}-N${density("campsiteDensity")}-V${density("settlementDensity")}-P${integer("preparationIntensity", 1, 3)}`;
  if (config.routesEnabled != null && config.routesEnabled !== "") {
    if (!["ForestTrail", "MinorPath", "Road", "RockPassage"].includes(config.routeProfile)) throw new Error("Choose a route profile.");
    routes = `-Q${bit("routesEnabled")}-T${density("throughRoutes")}-J${config.routeProfile}-L${bit("connectSites")}-A${density("explorationLinks")}-U${bit("waterCrossings")}-Z${integer("routeIntensity", 1, 3)}`;
  }
  if (config.populationDensity != null && config.populationDensity !== "") {
    if (!["Off", "Low", "Medium", "High"].includes(config.populationDensity)) throw new Error("Choose a Population density.");
    population = `-M${config.populationDensity}-D${integer("populationDifficulty", 1, 5)}`;
  }
  const loot = config.lootPreset ?? "Empty";
  if (!["Empty", "Classic", "Fun", "Experimental", "FarOut", "All"].includes(loot)) throw new Error("Choose a Loot deck.");
  return `WLD1-${width}x${height}-X${x}-Y${y}-F${forest}-W${water}-R${rock}-K${config.scale}-H${config.waterForm}${enrichment}${civilization}${routes}${population}-O${loot}${includeSeed && seed ? `-S${seed.toUpperCase()}` : ""}`;
}

export function worldConfigurationFromCode(input, maxBoardSize = 120) {
  const match = input.trim().match(/^WLD1-([1-9]\d{0,2})x([1-9]\d{0,2})-X(-?\d+)-Y(-?\d+)-F(\d{1,3})-W(\d{1,3})-R(\d{1,3})-K(Fine|Balanced|Broad)-H(Auto|Lakes|River|Mixed)(?:-E([01])-I([123])-B([01]{4}))?(?:-C([01])-N(None|Low|Medium|High|VeryHigh)-V(None|Low|Medium|High|VeryHigh)-P([123]))?(?:-Q([01])-T(None|Low|Medium|High|VeryHigh)-J(ForestTrail|MinorPath|Road|RockPassage)-L([01])-A(None|Low|Medium|High|VeryHigh)-U([01])-Z([123]))?(?:-M(Off|Low|Medium|High)-D([1-5]))?(?:-O(Empty|Classic|Fun|Experimental|FarOut|All))?(?:-S([0-9a-fA-F]{16}))?$/);
  if (!match) throw new Error("Enter an ordered WLD1 recipe or complete code; a seed needs exactly 16 hexadecimal digits.");
  const result = Object.fromEntries(["width", "height", "offsetX", "offsetY", "forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm"].map((key, i) => [key, match[i + 1]]));
  result.lootPreset = match[26] ?? "Empty";
  result.seed = match[27] ?? "";
  if (match[10] != null) {
    result.enrichmentEnabled = String(match[10] === "1"); result.enrichmentIntensity = match[11];
    ["forestEnrichment", "rockEnrichment", "waterEnrichment", "grasslandEnrichment"].forEach((key, i) => { result[key] = String(match[12][i] === "1"); });
  }
  if (match[13] != null) {
    result.civilizationEnabled = String(match[13] === "1"); result.campsiteDensity = match[14];
    result.settlementDensity = match[15]; result.preparationIntensity = match[16];
  }
  if (match[17] != null) {
    result.routesEnabled = String(match[17] === "1"); result.throughRoutes = match[18]; result.routeProfile = match[19];
    result.connectSites = String(match[20] === "1"); result.explorationLinks = match[21];
    result.waterCrossings = String(match[22] === "1"); result.routeIntensity = match[23];
  }
  if (match[24] != null) { result.populationDensity = match[24]; result.populationDifficulty = match[25]; }
  worldConfigurationCode(result, true, maxBoardSize);
  result.seed = result.seed.toUpperCase();
  return result;
}

// Summaries describe authored settings only; counts and legality remain server-owned.
export function worldSettingSummaries(config) {
  const label = value => String(value ?? "").replace(/([a-z])([A-Z])/g, "$1 $2");
  const intensity = value => ({ 1: "Low", 2: "Medium", 3: "High" })[value] ?? "";
  return {
    world: `${config.width}×${config.height} · ${label(config.waterForm)} · ${label(config.scale)}`,
    civilization: config.civilizationEnabled === "false" ? "Disabled" : `${label(config.settlementDensity)} settlements · ${label(config.campsiteDensity)} campsites`,
    routes: config.routesEnabled === "false" ? "Disabled" : `${label(config.routeProfile)} · ${label(config.throughRoutes)} through · ${label(config.explorationLinks)} links · ${intensity(config.routeIntensity)} intensity`,
    population: `${config.populationDensity} · Difficulty ${config.populationDifficulty} · ${label(config.lootPreset)} Loot`,
    advanced: config.enrichmentEnabled === "false" ? "Disabled" : `${intensity(config.enrichmentIntensity)} · ${["forest", "rock", "water", "grassland"].filter(key => config[`${key}Enrichment`] === "true").join(", ")}`
  };
}

export function worldResultSummary(result) {
  // Always summarize the final result, even while inspecting an earlier stage.
  const frame = result.preview?.frames?.at(-1), sites = frame?.sites ?? [];
  const buildings = sites.flatMap(site => site.buildings ?? []);
  const failures = (frame?.planningFailures ?? []).filter(failure => !["Route", "RouteNetwork"].includes(failure.generator));
  const routeFailures = (frame?.planningFailures ?? []).filter(failure => ["Route", "RouteNetwork"].includes(failure.generator));
  const unresolved = (frame?.routeNetwork?.intents ?? []).filter(intent => intent.status === "Unresolved");
  const population = frame?.population?.failures ?? [];
  const fallbacks = sites.filter(site => site.settlementLayout?.fallbackReasons?.length);
  const omittedSites = (frame?.settlementDensity?.omitted ?? 0) + (frame?.campsiteDensity?.omitted ?? 0);
  const omittedRoutes = (frame?.throughRouteDensity?.omitted ?? 0) + (frame?.explorationLinkDensity?.omitted ?? 0);
  const constructionWarnings = Math.max(failures.length, omittedSites);
  const routeWarnings = Math.max(unresolved.length, omittedRoutes, routeFailures.length);
  const warnings = [];
  if (constructionWarnings) warnings.push(`${constructionWarnings} construction requests failed or omitted`);
  if (unresolved.length) warnings.push(`${unresolved.length} unresolved route connections`);
  if (omittedRoutes) warnings.push(`${omittedRoutes} route requests omitted`);
  if (routeFailures.length && !unresolved.length && !omittedRoutes) warnings.push(`${routeFailures.length} route planning failures`);
  if (population.length) warnings.push(`${population.length} population placement warnings`);
  if (fallbacks.length) warnings.push(`${fallbacks.length} sites used layout fallbacks`);
  const count = constructionWarnings + routeWarnings + population.length + fallbacks.length;
  return { text: `${sites.length} sites · ${buildings.length} buildings · ${buildings.reduce((sum, building) => sum + (building.rooms?.length ?? 0), 0)} rooms · ${count} warning${count === 1 ? "" : "s"}`, warnings };
}

// Sparse presentation replay only. Geometry, connectivity and content remain server-owned.
export function constructionRange(operations, id) {
  const start = operations.findIndex(operation => operation.id === id);
  if (start < 0) return null;
  const descendants = new Set([id]); let end = start + 1;
  for (let i = start + 1; i < operations.length; i++)
    if (descendants.has(operations[i].parentId)) { descendants.add(operations[i].id); end = i + 1; }
  return { start, end };
}

export function constructionPreview(preview, frameIndex, operationId, before = false, width) {
  const selected = preview.frames[frameIndex], operations = selected.construction ?? [];
  const range = constructionRange(operations, operationId);
  if (!range) return selected;
  const byId = new Map(operations.map(operation => [operation.id, operation]));
  let root = operations[range.start]; const seen = new Set();
  while (byId.has(root.parentId) && !seen.has(root.id)) { seen.add(root.id); root = byId.get(root.parentId); }
  const batch = selected.constructionBatches?.find(batch => range.start >= batch.firstOperation && range.start < batch.endOperation);
  const first = preview.frames.findIndex(frame => frame.construction?.some(operation => operation.id === root.id));
  const checkpoint = batch ? preview.frames.find(frame => frame.stepId === batch.checkpointStepId) : preview.frames[first - 1];
  if (!checkpoint) throw new Error("Construction inspection requires its preceding checkpoint.");
  const replayStart = batch?.firstOperation ?? checkpoint.construction?.length ?? 0;
  const terrain = checkpoint.terrain.split("");
  const palette = new Map(preview.terrainPalette.map((id, index) => [id, String.fromCharCode(65 + index)]));
  const edgeId = key => `${key.index}:${key.direction}`;
  const edges = new Map((checkpoint.edges ?? []).filter(edge => edge.kind !== "None").map(edge =>
    [edgeId({ index: edge.position.y * width + edge.position.x, direction: edge.direction }), edge]));
  for (const operation of operations.slice(replayStart, before ? range.start : range.end)) {
    for (const change of operation.terrainChanges ?? []) {
      if (!palette.has(change.after)) throw new Error(`Construction Terrain '${change.after}' is missing from the palette.`);
      terrain[change.index] = palette.get(change.after);
    }
    for (const change of operation.edgeChanges ?? []) {
      const key = edgeId(change.key);
      if (change.after === "None") edges.delete(key);
      else edges.set(key, { position: { x: change.key.index % width, y: Math.floor(change.key.index / width) }, direction: change.key.direction, kind: change.after });
    }
  }
  const changes = operations.slice(range.start, range.end);
  return { ...selected, terrain: terrain.join(""), edges: [...edges.values()],
    changedCells: [...new Set(changes.flatMap(operation => (operation.terrainChanges ?? []).map(change => change.index)))],
    changedEdges: changes.flatMap(operation => operation.edgeChanges ?? []) };
}
