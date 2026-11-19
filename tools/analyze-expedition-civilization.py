"""Compare full before/after ExpeditionJourneyDiagnostic exports (Pillow only).

python tools/analyze-expedition-civilization.py BEFORE AFTER OUTPUT [--baseline CORPUS]
All figures use exact accepted SiteInfo spans. Signals are regional samples, not
interpolated cell fields. The path is ordinary Land with openable Doors and all
initial Monster footprints blocked; it is a geographic witness, not a gameplay run.
"""
import argparse
from collections import Counter
import csv
from functools import lru_cache
import json
from math import ceil, dist, sqrt
from pathlib import Path
from random import Random
from statistics import mean, median

from PIL import Image, ImageDraw

SIZE = 60
COLORS = {"grass": "#74855a", "dirt": "#9f8467", "tree": "#365736", "fruit-tree": "#396d42",
          "solid-rock": "#515765", "gravel": "#a6a5a3", "stone-floor": "#d9d5cd",
          "stone-floor-with-table": "#965b39", "campfire": "#ee9825", "water": "#326c90",
          "wet-grass": "#537f79", "rocky-water": "#557b92", "stepping-stones": "#8db0b5", "lava": "#a7452f",
          "gravel-with-well": "#377a85", "rubble": "#74696a", "grass-with-marker": "#4b6d70",
          "gravel-with-statue": "#548a93", "stone-floor-with-obelisk": "#305a74"}
KINDS = {"Campsite": "#ffbd45", "Settlement": "#ff8caa", "MountainDungeon": "#c5a3ff",
         "Ruin": "#f58b50", "Landmark": "#63d9bb", "FortifiedOutpost": "#91e573"}
PROFILES = {"Cottage": "#e3a883", "OpenCamp": "#ffd38c", "ShelteredCamp": "#f4ab54",
            "PrimitiveHut": "#e98b49", "VillageCenter": "#ffd0dd",
            "RuinedCottage": "#f58b50", "CollapsedBuilding": "#fabb72", "RuinedHamlet": "#ff633a",
            "RoadsideMarker": "#7aeac7", "IsolatedLandmark": "#63d9bb", "FortifiedOutpost": "#91e573"}
REMAINS = {"RuinedCottage", "CollapsedBuilding", "RuinedHamlet", "RoadsideMarker", "IsolatedLandmark"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load(root):
    return {int(p.parent.name.split("-")[1]): read(p) for p in root.glob("seed-*/journey.json")}


def cells(area):
    result = {(x, s["y"]) for s in area["spans"] for x in range(s["x"], s["x"] + s["length"])}
    assert len(result) == area["area"]
    return result


def nearest(points):
    return [min(dist(p, q) for j, q in enumerate(points) if i != j) for i, p in enumerate(points)] if len(points) > 1 else []


@lru_cache(maxsize=None)
def uniform_nn(n):
    if n < 2:
        return None
    rng = Random(42315 + n)
    return mean(mean(nearest([(rng.uniform(0, SIZE), rng.uniform(0, SIZE)) for _ in range(n)])) for _ in range(300))


def runs(values):
    result, start = [], None
    for i, value in enumerate(values + [False]):
        if value and start is None:
            start = i
        elif not value and start is not None:
            result.append({"first": start + 1, "last": i, "length": i - start})
            start = None
    return result


def pearson(xs, ys):
    if len(xs) < 2:
        return None
    mx, my = mean(xs), mean(ys)
    denominator = sqrt(sum((x - mx) ** 2 for x in xs) * sum((y - my) ** 2 for y in ys))
    return sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / denominator if denominator else None


def region_row(variant, seed, r):
    detail = r.get("details") or {}
    sites = detail.get("sites", [])
    footprints = [cells(s["footprint"]) for s in sites]
    centers = [(mean(x for x, y in f), mean(y for x, y in f)) for f in footprints]
    nn = nearest(centers)
    expected = uniform_nn(len(sites))
    settlement_centers = [c for s, c in zip(sites, centers) if s["kind"] == "Settlement"]
    settlement_nn = nearest(settlement_centers)
    building_footprints = [cells(b["footprint"]) for s in sites for b in s["buildings"]]
    building_centers = [(mean(x for x, y in f), mean(y for x, y in f)) for f in building_footprints]
    building_nn = nearest(building_centers)
    allocation = detail.get("civilizationAllocation") or {}
    remains = allocation.get("remains") or {}
    outpost = allocation.get("outpost") or {}
    hub = allocation.get("hub")
    hub_distances = [dist(c, (hub["x"], hub["y"])) for c in settlement_centers] if hub else []
    guard = detail.get("completionGuard") or {}
    stats = detail.get("civilizationStats") or {}
    measurements = detail.get("measurements", [])
    campsite = detail.get("campsiteDensity") or {}
    settlement = detail.get("settlementDensity") or {}
    return {"variant": variant, "seed": seed, "index": r["index"],
            "regionX": r["region"]["x"], "regionY": r["region"]["y"], "error": r.get("error"),
            "density": allocation.get("densitySignal", r["fields"]["civilization"]),
            "clustering": allocation.get("clusteringSignal"),
            "ruinSignal": remains.get("ruinSignal"), "landmarkSignal": remains.get("landmarkSignal"),
            "outpostSignal": outpost.get("signal"), "outpostRequested": int(outpost.get("anchor") is not None),
            "outpostDetails": detail.get("outposts", []),
            "outpostOmissions": [failure for failure in detail.get("planningFailures", [])
                                 if "/outpost-" in failure["requestId"]],
            "ruinBuildingLoot": (detail.get("remains") or {}).get("ruinBuildingLoot", 0),
            "landmarkTerrains": (detail.get("remains") or {}).get("landmarkTerrains", []),
            "settlementTarget": allocation.get("settlementTarget"), "campsiteTarget": allocation.get("campsiteTarget"),
            "dungeonTarget": allocation.get("dungeonTarget"), "sites": len(sites),
            "campsiteRequested": campsite.get("requested"), "campsiteOmitted": campsite.get("omitted"),
            "settlementRequested": settlement.get("requested"), "settlementOmitted": settlement.get("omitted"),
            "campsiteOmissionReason": campsite.get("reason"), "settlementOmissionReason": settlement.get("reason"),
            **{kind.lower(): sum(s["kind"] == kind for s in sites) for kind in KINDS},
            "siteFootprintCells": len(set().union(*footprints)) if footprints else 0,
            "buildings": len(building_footprints),
            "buildingFootprintCells": len(set().union(*building_footprints)) if building_footprints else 0,
            "buildingNearestMean": mean(building_nn) if building_nn else None,
            "settlementNearestMean": mean(settlement_nn) if settlement_nn else None,
            "settlementNearestUniformRatio": mean(settlement_nn) / uniform_nn(len(settlement_centers)) if settlement_nn else None,
            "settlementHubDistanceMean": mean(hub_distances) if hub_distances else None,
            "settlementHubDistanceMax": max(hub_distances) if hub_distances else None,
            "siteNearestMean": mean(nn) if nn else None,
            "siteNearestUniformRatio": mean(nn) / expected if nn else None,
            "occupiedQuadrants": len({(int(x >= 30), int(y >= 30)) for x, y in centers}),
            "workUsed": guard.get("workUsed"), "guardCount": guard.get("count"),
            "civilizationWorkUsed": guard.get("civilizationWorkUsed"), "civilizationGuardCount": guard.get("civilizationCount"),
            "civilizationPlanningAttempts": stats.get("attempts"), "civilizationPlanningWork": stats.get("work"),
            "civilizationMilliseconds": sum(m["milliseconds"] for m in measurements if m["stage"] == "Civilization"),
            "milliseconds": r.get("generationMilliseconds"), "pathSteps": len(detail["access"]["path"]) - 1,
            "constructionProfiles": stats.get("constructionProfiles"), "beacons": stats.get("beaconsPlaced"), "dungeonRequested": stats.get("dungeonsRequested"),
            "dungeonsOmitted": stats.get("dungeonsOmitted")}


def region_image(r, scale=3, overlay=True, occupancy=True):
    terrain = r.get("finalTerrain") or r["baseTerrain"]
    image = Image.new("RGB", (SIZE, SIZE))
    image.putdata([tuple(bytes.fromhex(COLORS.get(t, "#b0a090")[1:])) for t in terrain])
    image = image.resize((SIZE * scale, SIZE * scale), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(image)
    if scale >= 6:
        for index, material in enumerate(terrain):
            x, y = (index % SIZE) * scale, (index // SIZE) * scale
            left, right = x + scale // 4, x + scale - scale // 4 - 1
            bottom = y + scale - scale // 6
            if material == "grass-with-marker":
                draw.rounded_rectangle((left, y + scale // 3, right, bottom), radius=2, fill="#d2c6a5")
            elif material == "gravel-with-statue":
                draw.rectangle((left, y + scale // 2, right, bottom), fill="#acaea5")
                draw.ellipse((left + 1, y + 1, right - 1, y + scale // 2 + 1), fill="#e1dfca")
            elif material == "stone-floor-with-obelisk":
                draw.polygon(((x + scale // 2, y + 1), (right, y + scale // 2), (right, bottom),
                              (left, bottom), (left, y + scale // 2)), fill="#c4cfd7")
    for edge in r.get("edges") or []:
        a, b = edge["a"], edge["b"]
        color = "#f7cca4" if edge["kind"] in ("ClosedDoor", "OpenDoor") else "#332f32"
        if a["x"] != b["x"]:
            x, y = max(a["x"], b["x"]) * scale, a["y"] * scale
            draw.line((x, y, x, y + scale), fill=color, width=max(1, scale // 5))
        else:
            x, y = a["x"] * scale, max(a["y"], b["y"]) * scale
            draw.line((x, y, x + scale, y), fill=color, width=max(1, scale // 5))
    if overlay:
        detail = r.get("details") or {}
        for site in detail.get("sites", []):
            footprint = cells(site["footprint"])
            color = PROFILES.get(site.get("constructionProfile"), KINDS.get(site["kind"], "white"))
            for x, y in footprint:
                for dx, dy, line in [(-1, 0, (x, y, x, y+1)), (1, 0, (x+1, y, x+1, y+1)),
                                     (0, -1, (x, y, x+1, y)), (0, 1, (x, y+1, x+1, y+1))]:
                    if (x+dx, y+dy) not in footprint:
                        draw.line(tuple(v * scale for v in line), fill=color, width=1)
        path = detail.get("access", {}).get("path", [])
        if len(path) > 1:
            draw.line([((p["x"]+.5)*scale, (p["y"]+.5)*scale) for p in path], fill="#f5f6ff", width=1)
        if occupancy:
            for unit in detail.get("access", {}).get("initialMonsters", []):
                for p in unit["cells"]:
                    x, y = p["x"] * scale, p["y"] * scale
                    draw.rectangle((x, y, x+scale-1, y+scale-1), fill="#d92935")
        hub = (detail.get("civilizationAllocation") or {}).get("hub")
        if hub:
            x, y = (hub["x"]+.5)*scale, (hub["y"]+.5)*scale
            draw.ellipse((x-4, y-4, x+4, y+4), outline="#00ffff", width=2)
        for site in detail.get("sites", []):
            centerpiece = (site.get("settlementLayout") or {}).get("centerpieceIndex")
            if centerpiece is not None:
                x, y = (centerpiece % SIZE + .5) * scale, (centerpiece // SIZE + .5) * scale
                radius = max(2, scale / 2)
                draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill="#4096a4", outline="#e0e4cc")
    return image


def site_comparison(before, after, site, output, versions=("ExpeditionConstruction1", "ExpeditionConstruction2")):
    footprint = cells(site["footprint"])
    margin, scale = 5, 16
    x0, y0 = max(0, min(x for x, _ in footprint) - margin), max(0, min(y for _, y in footprint) - margin)
    x1, y1 = min(SIZE, max(x for x, _ in footprint) + margin + 1), min(SIZE, max(y for _, y in footprint) + margin + 1)
    box = tuple(p * scale for p in (x0, y0, x1, y1))
    width, height = box[2] - box[0], box[3] - box[1]
    image = Image.new("RGB", (max(2 * width + 36, 515), height + 62), "#171c23")
    draw = ImageDraw.Draw(image)
    draw.text((12, 9), f"{site['constructionProfile']} at local Cells ({x0}, {y0}) to ({x1 - 1}, {y1 - 1})", fill="white")
    if site.get("outpost"):
        gate = site["entrances"][0]
        assert gate["kind"] == "ClosedDoor"
        gate_index = gate["outsideIndex"]
        draw.text((385, 9), f"Closed gate at ({gate_index % SIZE}, {gate_index // SIZE})", fill="#ffc689")
    for i, (label, region) in enumerate(zip(versions, (before, after))):
        x = 12 + i * (width + 12)
        draw.text((x, 29), label, fill="#d3d6da")
        image.paste(region_image(region, scale, occupancy=False).crop(box), (x, 47))
    image.save(output)


def figures(seed, before, after, output, short=False):
    regions = after["regions"]
    xs = [r["region"]["x"] for r in regions]; ys = [r["region"]["y"] for r in regions]
    lo_x, lo_y = min(xs), min(ys)
    cols, rows = max(xs)-lo_x+1, max(ys)-lo_y+1
    tile = 122
    canvas = Image.new("RGB", (max(cols*tile*2+45, 600), rows*tile+100), "#171c23")
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), f"Seed {seed}: before (left) / after (right), exact final terrain + Site outlines", fill="white")
    draw.text((12, 27), "Tan cottage | gold camp | pink settlement | lavender dungeon | orange ruin | teal landmark | green outpost", fill="white")
    draw.text((12, 45), "Dark Walls, pale Doors, white path, red Monsters, cyan hub. Numbers show visit order.", fill="#d3d6da")
    for variant, journey in enumerate((before, after)):
        for r in journey["regions"]:
            x = 12 + variant*(cols*tile+20)+(r["region"]["x"]-lo_x)*tile
            y = 65+(r["region"]["y"]-lo_y)*tile
            canvas.paste(region_image(r, 2), (x, y))
            draw.text((x+3, y+3), str(r["index"]), fill="white", stroke_width=1, stroke_fill="black")
    label = "short-journey" if short else "before-after"
    canvas.save(output/f"seed-{seed}-{label}.png")
    if short:
        return
    def signal_map(label, title, channels, subfield=None):
        signal = Image.new("RGB", (max(cols*42*2+45, 500), rows*42+85), "#171c23")
        draw = ImageDraw.Draw(signal)
        draw.text((10, 8), f"Seed {seed}: {title} | 0 dark / 1 bright", fill="white")
        draw.text((10, 26), "Each tile is one 60x60 region. Numbers show accepted Sites of the named kind.", fill="white")
        for k, (field, kind) in enumerate(channels):
            for r in regions:
                detail = r.get("details") or {}
                allocation = detail.get("civilizationAllocation") or {}
                values = allocation.get(subfield, {}) if subfield else allocation
                v = values.get(field)
                x, y = 10+k*(cols*42+20)+(r["region"]["x"]-lo_x)*42, 65+(r["region"]["y"]-lo_y)*42
                color = (int(25+220*v), int(35+155*v), int(55+70*v)) if v is not None else (80,80,80)
                draw.rectangle((x,y,x+40,y+40), fill=color)
                draw.text((x+3,y+4), str(sum(s["kind"] == kind for s in detail.get("sites", []))), fill="white")
        signal.save(output/f"seed-{seed}-{label}.png")

    signal_map("signals", "Settlement density (left), Settlement clustering (right)",
               (("densitySignal", "Settlement"), ("clusteringSignal", "Settlement")))
    if after["profile"].get("constructionVersion") in ("ExpeditionConstruction2", "ExpeditionConstruction3"):
        signal_map("remains-signals", "Ruin opportunity (left), Landmark opportunity (right)",
                   (("ruinSignal", "Ruin"), ("landmarkSignal", "Landmark")), "remains")
    if after["profile"].get("constructionVersion") == "ExpeditionConstruction3":
        signal_map("outpost-signals", "Fortified outpost opportunity", (("signal", "FortifiedOutpost"),), "outpost")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("before", "after", "output"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args()
    before, after = load(args.before), load(args.after)
    assert before and before.keys() == after.keys(), "Before/after seed sets differ or are empty"
    args.output.mkdir(parents=True, exist_ok=True)
    baseline = load(args.baseline) if args.baseline else {}
    construction = any(journey["profile"].get("constructionVersion") for journey in after.values())
    remains = any(journey["profile"].get("constructionVersion") == "ExpeditionConstruction2" for journey in after.values())
    outposts = any(journey["profile"].get("constructionVersion") == "ExpeditionConstruction3" for journey in after.values())
    rows, comparisons, journeys, closeups = [], [], [], []
    for seed in before:
        a, b = before[seed], after[seed]
        assert a.get("schemaVersion") == b.get("schemaVersion") == 2, "Re-export schema v2 journeys with exact Site diagnostics"
        assert a["worldSeed"] == b["worldSeed"] and a["baselineRecipe"] == b["baselineRecipe"]
        ignored_version = "constructionVersion" if construction else "civilizationVersion"
        assert {k: v for k, v in a["profile"].items() if k != ignored_version} == {
            k: v for k, v in b["profile"].items() if k != ignored_version}
        if remains:
            assert a["profile"]["constructionVersion"] == "ExpeditionConstruction1"
            assert b["profile"]["constructionVersion"] == "ExpeditionConstruction2"
        if outposts:
            assert a["profile"]["constructionVersion"] == "ExpeditionConstruction2"
            assert b["profile"]["constructionVersion"] == "ExpeditionConstruction3"
        assert len(a["regions"]) == len(b["regions"])
        count = a["fullRegionsRequested"]
        assert 0 < count <= len(a["regions"])
        assert a["fullRegionsSucceeded"] == b["fullRegionsSucceeded"] == b["fullRegionsRequested"] == count, "Incomplete journey"
        a["regions"], b["regions"] = a["regions"][:count], b["regions"][:count]
        assert a["repeatedFirstSuccessfulScenarioMatched"] and b["repeatedFirstSuccessfulScenarioMatched"]
        for old, new in zip(a["regions"], b["regions"]):
            assert old.get("details") and new.get("details"), "Missing full diagnostic details"
            assert new["details"].get("civilizationAllocation"), "After region has no Civilization1 allocation"
            for key in ("region", "origin", "nextRegion", "entryEdge", "entrySeam", "exitEdge", "exitSeam",
                        "localSeed", "transitionSeed", "baseTerrain", "fields"):
                assert old[key] == new[key], (seed, old["index"], key)
            assert old["details"]["preCivilizationTerrain"] == new["details"]["preCivilizationTerrain"]
            assert old["details"]["access"]["allEndpointsReachable"] and new["details"]["access"]["allEndpointsReachable"]
            if construction:
                prior = old["details"]["civilizationAllocation"]
                current = new["details"]["civilizationAllocation"]
                additions = ("constructionVersion", "outpost" if outposts else "remains")
                assert {k: v for k, v in prior.items() if k != "constructionVersion"} == {
                    k: v for k, v in current.items() if k not in additions}
            if remains:
                assert old["details"]["sites"] == [
                    s for s in new["details"]["sites"] if s["kind"] not in ("Ruin", "Landmark")], (seed, old["index"])
            if outposts:
                assert old["details"]["sites"] == [
                    s for s in new["details"]["sites"] if s["kind"] != "FortifiedOutpost"], (seed, old["index"])
            rows.extend([region_row("before", seed, old), region_row("after", seed, new)])
            recorded = baseline.get(seed, {}).get("regions", [])
            comparisons.append({"seed": seed, "index": old["index"], "natureMatched": True,
                                "finalChanged": old["scenarioSha256"] != new["scenarioSha256"],
                                "guardWorkDelta": new["details"]["completionGuard"]["workUsed"] - old["details"]["completionGuard"]["workUsed"],
                                "guardWorkRatio": new["details"]["completionGuard"]["workUsed"] / max(1, old["details"]["completionGuard"]["workUsed"]),
                                "corpusBaselineMatched": recorded[old["index"]-1]["scenarioSha256"] == old["scenarioSha256"] if recorded else None})
        for variant, journey in (("before", a), ("after", b)):
            rr = [r for r in rows if r["seed"] == seed and r["variant"] == variant]
            journeys.append({"seed": seed, "variant": variant,
                             "zeroSettlementRuns": runs([r["settlement"] == 0 for r in rr]),
                             "zeroSiteRuns": runs([r["sites"] == 0 for r in rr]),
                             "lowSettlementRuns": runs([r["settlement"] <= 1 for r in rr])})
        figures(seed, a, b, args.output)
        if construction:
            span = min(6, count)
            def variety(start):
                sites = [site for r in b["regions"][start:start+span] for site in r["details"]["sites"]]
                profiles = {s.get("constructionProfile") for s in sites} - {None}
                focal = "FortifiedOutpost" in profiles if outposts else len(profiles & REMAINS) if remains else 0
                return focal, len(profiles), len(sites)
            start = max(range(count - span + 1), key=variety)
            figures(seed, {"regions": a["regions"][start:start+span]},
                    {"regions": b["regions"][start:start+span]}, args.output, short=True)
    with (args.output/"regions.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
    aggregate = {}
    for variant, source in (("before", before), ("after", after)):
        rr = [r for r in rows if r["variant"] == variant]
        metrics = {}
        for key in ("sites", "settlement", "campsite", "mountaindungeon", "ruin", "landmark", "siteFootprintCells", "siteNearestMean", "siteNearestUniformRatio",
                    "buildings", "buildingFootprintCells", "buildingNearestMean", "settlementNearestMean", "settlementNearestUniformRatio",
                    "settlementHubDistanceMean", "settlementHubDistanceMax",
                    "campsiteRequested", "campsiteOmitted", "settlementRequested", "settlementOmitted", "dungeonRequested", "dungeonsOmitted",
                    "civilizationPlanningAttempts", "civilizationPlanningWork",
                    "workUsed", "civilizationWorkUsed", "guardCount", "civilizationGuardCount", "milliseconds", "civilizationMilliseconds", "pathSteps"):
            values = sorted(r[key] for r in rr if r[key] is not None)
            metrics[key] = {"mean": mean(values), "median": median(values), "p95": values[ceil(.95 * len(values)) - 1],
                            "min": values[0], "max": values[-1], "sum": sum(values)} if values else None
        metrics["settlementCountHistogram"] = dict(sorted(Counter(r["settlement"] for r in rr).items()))
        metrics["zeroSettlementRegions"] = sum(r["settlement"] == 0 for r in rr)
        metrics["zeroSiteRegions"] = sum(r["sites"] == 0 for r in rr)
        metrics["ruinsWithoutLivingSettlements"] = sum(r["ruin"] for r in rr if r["settlement"] == 0)
        metrics["ruinBuildingLoot"] = sum(r["ruinBuildingLoot"] for r in rr)
        metrics["landmarkTerrains"] = dict(Counter(terrain for r in rr for terrain in r["landmarkTerrains"]))
        if outposts:
            metrics["outpostsWithoutLivingSettlements"] = sum(r["fortifiedoutpost"] for r in rr if r["settlement"] == 0)
            metrics["outpostBuildingLoot"] = sum(s["buildingLoot"] for r in rr for s in r["outpostDetails"])
            metrics["outpostInitialPopulation"] = dict(sorted(Counter(s["enclosedMonsters"] for r in rr for s in r["outpostDetails"]).items()))
        if remains and variant == "after":
            for key in ("ruinSignal", "landmarkSignal"):
                neighbors = [(first[key], second[key]) for first, second in zip(rr, rr[1:]) if first["seed"] == second["seed"]]
                metrics[key + "JourneyNeighborCorrelation"] = pearson(*zip(*neighbors)) if neighbors else None
        metrics["civilizationPlacementFailures"] = dict(Counter(
            f'{failure["generator"]}: {failure["code"]}'
            for journey in source.values() for region in journey["regions"]
            for failure in region["details"]["planningFailures"] if failure["requestId"].startswith("civilization/")))
        metrics["densityAcceptedSiteCorrelation"] = pearson([r["density"] for r in rr], [r["sites"] for r in rr])
        metrics["densitySettlementCorrelation"] = pearson([r["density"] for r in rr], [r["settlement"] for r in rr])
        eligible = [r for r in rr if r["clustering"] is not None and r["siteNearestUniformRatio"] is not None]
        metrics["clusteringNearestRatioCorrelation"] = pearson([r["clustering"] for r in eligible], [r["siteNearestUniformRatio"] for r in eligible])
        eligible = [r for r in rr if r["clustering"] is not None and r["settlementNearestUniformRatio"] is not None]
        metrics["clusteringSettlementNearestRatioCorrelation"] = pearson([r["clustering"] for r in eligible], [r["settlementNearestUniformRatio"] for r in eligible])
        aggregate[variant] = metrics
    quantiles = {}
    for signal in ("density", "clustering"):
        eligible = sorted([r for r in rows if r["variant"] == "after" and r[signal] is not None], key=lambda r: r[signal])
        n = max(1, len(eligible) // 3)
        quantiles[signal] = {}
        for label, group in (("lowThird", eligible[:n]), ("highThird", eligible[-n:])):
            quantiles[signal][label] = {"regions": len(group), "meanSignal": mean(r[signal] for r in group),
                                       "meanAcceptedSites": mean(r["sites"] for r in group),
                                       "meanAcceptedSettlements": mean(r["settlement"] for r in group),
                                       "meanSettlementNearestUniformRatio": mean(r["settlementNearestUniformRatio"] for r in group if r["settlementNearestUniformRatio"] is not None)
                                       if any(r["settlementNearestUniformRatio"] is not None for r in group) else None,
                                       "meanNearestUniformRatio": mean(r["siteNearestUniformRatio"] for r in group if r["siteNearestUniformRatio"] is not None)
                                       if any(r["siteNearestUniformRatio"] is not None for r in group) else None}
    # Compare deliberately chosen extremes, plus high-density/low-clustering cases.
    successful = [r for r in rows if r["variant"] == "after"]
    multiple = [r for r in successful if r["settlement"] >= 2]
    strong = [r for r in multiple if (r["clustering"] or 0) >= .65]
    concentrated = sorted(strong or multiple, key=lambda r: r["settlementNearestUniformRatio"])
    dense = [r for r in multiple if r["settlement"] >= 4]
    scattered = sorted(dense, key=lambda r: r["settlementNearestUniformRatio"], reverse=True)
    wilderness = sorted((r for r in successful if r["settlement"] == 0), key=lambda r: (r["sites"], r["density"]))
    isolated = sorted((r for r in successful if r["settlement"] == 1), key=lambda r: (r["sites"], r["density"]))
    choices = [("wilderness", r) for r in wilderness[:1]]
    choices += [("isolated-settlement", r) for r in isolated[:1]]
    choices += [("strong-signal-accepted-concentration" if strong else "accepted-concentration", r) for r in concentrated[:2]]
    choices += [("dense-scattered-comparison", r) for r in scattered[:1]]
    choices += [("highest-settlement-count", max(successful, key=lambda r: r["settlement"]))]
    construction_totals = {}
    if construction:
        choices = []
        options = [(r, after[r["seed"]]["regions"][r["index"]-1]["details"]["sites"]) for r in successful]
        examples = (
            ("isolated cottage", lambda s: s.get("constructionProfile") == "Cottage"),
            ("open camp", lambda s: s.get("constructionProfile") == "OpenCamp"),
            ("open shelter", lambda s: s.get("constructionProfile") == "ShelteredCamp"),
            ("primitive hut", lambda s: s.get("constructionProfile") == "PrimitiveHut"),
            ("communal square", lambda s: s.get("constructionProfile") == "VillageCenter" and
                s["settlementLayout"].get("centerpieceIndex") is None),
            ("communal square with centerpiece", lambda s: s.get("constructionProfile") == "VillageCenter" and
                s["settlementLayout"].get("centerpieceIndex") is not None),
        )
        if remains:
            examples = [("ruined cottage", lambda s: s.get("constructionProfile") == "RuinedCottage"),
                        ("partially collapsed building", lambda s: s.get("constructionProfile") == "CollapsedBuilding"),
                        ("ruined hamlet", lambda s: s.get("constructionProfile") == "RuinedHamlet"),
                        ("roadside marker", lambda s: s.get("constructionProfile") == "RoadsideMarker"),
                        ("isolated statue or obelisk", lambda s: s.get("constructionProfile") == "IsolatedLandmark")]
            quiet_ruins = [r for r in successful if r["settlement"] == 0 and r["ruin"]]
            if quiet_ruins:
                choices.append(("wilderness ruin", min(quiet_ruins, key=lambda r: r["sites"])))
        if outposts:
            examples = []
            placed = [r for r in successful if r["outpostDetails"]]
            quality = lambda r: r["outpostDetails"][0]
            route_distance = lambda r: quality(r)["primaryRouteDistance"] if quality(r)["primaryRouteDistance"] is not None else 2 * SIZE
            if placed:
                examples_outpost = [
                    ("open-terrain", max(placed, key=lambda r: quality(r)["openPercent"])),
                    ("forest", max(placed, key=lambda r: quality(r)["forestPercent"])),
                    ("route-adjacent", min(placed, key=route_distance)),
                    ("closed-gate", min(placed, key=lambda r: quality(r)["enclosedMonsters"])),
                ]
                wild = [r for r in placed if r["settlement"] == 0]
                if wild:
                    examples_outpost.append(("isolated-wilderness", max(wild, key=route_distance)))
                for label, row in examples_outpost:
                    seed, index = row["seed"], row["index"]
                    old, new = before[seed]["regions"][index - 1], after[seed]["regions"][index - 1]
                    site = next(site for site in new["details"]["sites"] if site.get("outpost"))
                    path = args.output / f"closeup-{label}-outpost-{seed}-{index:02}.png"
                    site_comparison(old, new, site, path, ("ExpeditionConstruction2", "ExpeditionConstruction3"))
                    closeups.append(path)
                    choices.append((label + " outpost", row))
            omitted = [r for r in successful if r["outpostRequested"] and not r["fortifiedoutpost"]]
            if omitted:
                row = omitted[0]
                failure = (row["outpostOmissions"] or [{"code": "See completion diagnostics"}])[-1]
                choices.append((f"outpost omitted ({failure['code']})", row))
        for label, matches in examples:
            matching = [r for r, sites in options if any(matches(site) for site in sites)]
            if label == "isolated statue or obelisk":
                statuary = [r for r in matching if any(t in r["landmarkTerrains"]
                            for t in ("gravel-with-statue", "stone-floor-with-obelisk"))]
                if statuary:
                    matching = statuary
                else:
                    label = "isolated landmark"
            if matching:
                representative = min(matching, key=lambda r: (r["settlement"], r["sites"]))
                choices.append((label, representative))
                if remains:
                    seed, index = representative["seed"], representative["index"]
                    old, new = before[seed]["regions"][index - 1], after[seed]["regions"][index - 1]
                    site = next(s for s in new["details"]["sites"] if matches(s))
                    path = args.output / f"closeup-{site['constructionProfile']}-{seed}-{index:02}.png"
                    site_comparison(old, new, site, path)
                    closeups.append(path)
        dense_mix = max(options, key=lambda pair: (len({s.get("constructionProfile") for s in pair[1]} - {None}),
                                                   pair[0]["buildings"]))
        choices.append(("dense construction mix", dense_mix[0]))
        for profile in (p for p in PROFILES if (remains or outposts or p not in REMAINS)
                        and (outposts or p != "FortifiedOutpost")):
            allocations = [a for r in successful for a in r["constructionProfiles"] or [] if a["profile"] == profile]
            construction_totals[profile] = {key: sum(a[key] for a in allocations) for key in ("requested", "accepted", "omitted")}
        construction_totals["centerpiecesPlaced"] = sum(
            (s.get("settlementLayout") or {}).get("centerpieceIndex") is not None for _, sites in options for s in sites)
    selected = {(r["seed"], r["index"]) for _, r in choices}
    width, height = 760, 420
    for seed, index in sorted(selected):
        image = Image.new("RGB", (width, height), "#171c23"); draw = ImageDraw.Draw(image)
        titles = [reason for reason, r in choices if (r["seed"], r["index"]) == (seed, index)]
        draw.text((10, 26), " / ".join(titles), fill="#ebcba0")
        for k, source in enumerate((before, after)):
            r = source[seed]["regions"][index-1]
            image.paste(region_image(r, 6), (10+k*380, 45))
            row = next(r for r in rows if r["seed"] == seed and r["index"] == index and r["variant"] == ("before" if k == 0 else "after"))
            draw.text((10+k*380, 10), f"{row['variant']} seed {seed} · step {index} ({row['regionX']}, {row['regionY']}) · {row['sites']} Sites",
                      fill="white")
        image.save(args.output/f"representative-{seed}-{index:02}.png")
    report = {"methodology": "Exact SiteInfo footprint centroids. Nearest distance divided by deterministic uniform 60x60 simulation matched on Site count (300 draws); terrain suitability and footprint exclusion are not modeled. Below one indicates clustering relative to that limited null. Zero/low runs follow journey order. Regional signals are not cell rasters. Timing includes separate process scheduling and is observational.",
              "aggregate": aggregate, "signalThirds": quantiles, "journeys": journeys, "comparisons": comparisons,
              "constructionProfiles": construction_totals,
              "rows": rows, "representatives": [{"reason": reason, "seed": r["seed"], "index": r["index"],
                                                        "settlements": r["settlement"], "clustering": r["clustering"],
                                                        "settlementNearestUniformRatio": r["settlementNearestUniformRatio"]} for reason, r in choices]}
    (args.output/"report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    text = ["# Expedition Civilization comparison", "", report["methodology"], "",
            f"Compared {len(comparisons)} paired regions; all base terrain and seams match; first-success repeats passed.",
            f"Existing corpus fingerprints checked: {sum(c['corpusBaselineMatched'] is not None for c in comparisons)}; mismatches: {sum(c['corpusBaselineMatched'] is False for c in comparisons)}.", "",
            "| Measure (region mean) | Before | After |", "| --- | ---: | ---: |"]
    for key, value in aggregate["before"].items():
        if isinstance(value, dict) and "mean" in value and aggregate["after"][key]:
            text.append(f"| {key} | {value['mean']:.3f} | {aggregate['after'][key]['mean']:.3f} |")
    text.extend(["", "Wilderness runs and regional correlations are in report.json; exact regional measurements are in regions.csv.",
                 "Mosaics show final terrain, structural Edges, exact Site outlines and the actual access witness with initial Monsters."])
    for journey in journeys:
        for kind in ("zeroSettlementRuns", "zeroSiteRuns", "lowSettlementRuns"):
            longest = max((r["length"] for r in journey[kind]), default=0)
            text.append(f"{journey['variant']} seed {journey['seed']}: longest {kind} = {longest} regions.")
    for signal, groups in quantiles.items():
        text.extend(["", f"After {signal} thirds: " + json.dumps(groups)])
    text.extend(["", f"Maximum paired guard-work ratio: {max(c['guardWorkRatio'] for c in comparisons):.3f}; "
                 f"maximum paired guard-work delta: {max(c['guardWorkDelta'] for c in comparisons)}."])
    if construction:
        text.extend(["", "| Construction profile | Requested | Accepted | Omitted |", "| --- | ---: | ---: | ---: |"])
        for profile in construction_totals:
            if profile == "centerpiecesPlaced":
                continue
            counts = construction_totals[profile]
            text.append(f"| {profile} | {counts['requested']} | {counts['accepted']} | {counts['omitted']} |")
        text.extend(["", f"Accepted decorative centerpieces: {construction_totals['centerpiecesPlaced']}."])
    if outposts:
        results = aggregate["after"]
        occupancy = ", ".join(f"{count} outposts with {units} Monsters" for units, count in results["outpostInitialPopulation"].items())
        text.extend(["", f"{results['outpostsWithoutLivingSettlements']} outposts appeared without living settlements.",
                     f"Initial occupancy by compound was {occupancy}. Ordinary guardhouse exploration Loot totaled "
                     f"{results['outpostBuildingLoot']} markers."])
    if remains or outposts:
        text.extend(["", "## Generated boards", "",
                     "Each comparison uses the original on the left. Close-ups leave the Monster overlay off so the Walls and passages are visible.", ""])
        for seed in after:
            signal = f"[Outpost field](seed-{seed}-outpost-signals.png)" if outposts else f"[Remains fields](seed-{seed}-remains-signals.png)"
            text.append(f"- Seed {seed} · [Paired journey](seed-{seed}-before-after.png) · "
                        f"[Six-region excerpt](seed-{seed}-short-journey.png) · {signal}")
        for path in sorted(closeups):
            text.append(f"- [{path.stem.removeprefix('closeup-')}]({path.name})")
        if outposts:
            for reason, row in choices:
                if reason.startswith("outpost omitted"):
                    seed, index = row["seed"], row["index"]
                    text.append(f"- [{reason}, seed {seed} region {index}](representative-{seed}-{index:02}.png)")
        text.extend(["", "[Per-region measurements](regions.csv) · [Machine-readable report](report.json)"])
    (args.output/"report.md").write_text("\n".join(text)+"\n", encoding="utf-8")
    print(f"Compared {len(comparisons)} pairs; wrote {args.output}")


if __name__ == "__main__":
    main()
