"""Compare paired --passage-quality runs without third-party dependencies."""
import argparse
import json
from pathlib import Path


def load(directory):
    rows = [json.loads(line) for line in (directory / "samples.jsonl").read_text().splitlines()]
    result = {row["recipe"]: row for row in rows}
    if len(result) != len(rows):
        raise ValueError(f"Duplicate recipes in {directory}")
    return result


def summarize(rows):
    good = [row for row in rows if "error" not in row]
    result = {"cases": len(rows), "errors": len(rows) - len(good)}
    for key in ("walkable", "thin", "LongChains", "substantialArticulations", "LongBottleneckRuns", "sites", "dungeons", "dungeonRooms"):
        result[key] = sum(row[key] for row in good)
    result["maxChain"] = max((row["MaxChain"] for row in good), default=0)
    result["maxBottleneckRun"] = max((row["MaxBottleneckRun"] for row in good), default=0)
    result["casesWithLongBottleneck"] = sum(row["LongBottleneckRuns"] > 0 for row in good)
    result["terrainDisturbance"] = sum(sum(row["disturbance"].values()) for row in good)
    result["disturbedTrees"] = sum(row["disturbance"].get("tree", 0) + row["disturbance"].get("fruit-tree", 0) for row in good)
    result["disturbedRock"] = sum(row["disturbance"].get("solid-rock", 0) for row in good)
    for stage in ("ForestEnrichment", "RockEnrichment"):
        result[stage] = sum(row["enrichment"].get(stage, 0) for row in good)
    result["repairConversions"] = sum((row["repair"] or {}).get("Conversions", 0) for row in good)
    result["repairFallbacks"] = sum((row["repair"] or {}).get("UsedFallback", False) for row in good)
    result["worldsRepaired"] = sum((row["repair"] or {}).get("Conversions", 0) > 0 for row in good)
    result["playReadyValid"] = sum(row["validation"] == "Valid" for row in good)
    result["buildings"] = sum(len(row["buildings"]) for row in good)
    routes = [r for row in good for r in (row["routes"] or [])]
    result["widthTwoThroughRoutes"] = sum(r["Purpose"] == "ThroughRoute" and r["RealizedWidth"] >= 2 for r in routes)
    result["throughRoutes"] = sum(r["Purpose"] == "ThroughRoute" for r in routes)
    result["widthTwoSiteRoutes"] = sum(r["Purpose"] == "SiteConnection" and r["RealizedWidth"] >= 2 for r in routes)
    result["siteRoutes"] = sum(r["Purpose"] == "SiteConnection" for r in routes)
    for label, observations in (
        ("route", [r["clearance"] for r in routes]),
        ("throughRoute", [r["clearance"] for r in routes if r["Purpose"] == "ThroughRoute"]),
        ("exterior", [b["exterior"] for row in good for b in row["buildings"]]),
        ("approach", [b["approach"] for row in good for b in row["buildings"]]),
        ("siteExterior", [s["exterior"] for row in good for s in row["siteClearance"]]),
        ("siteApproach", [s["approach"] for row in good for s in row["siteClearance"]]),
    ):
        for key in ("cells", "passable", "thin", "twoByTwo", "substantial"):
            result[label + key[0].upper() + key[1:]] = sum(o[key] for o in observations)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    before, after = load(args.before), load(args.after)
    if before.keys() != after.keys():
        raise ValueError("Recipe sets differ; paired comparison requires identical corpus")
    paired = [recipe for recipe in before if "error" not in before[recipe] and "error" not in after[recipe]]
    groups = {"all": paired}
    for recipe in paired:
        row = before[recipe]
        groups.setdefault(row["name"], []).append(recipe)
        key = f'{row["name"]}-{row["width"]}x{row["height"]}'
        groups.setdefault(key, []).append(recipe)
    result = {
        "before": summarize(list(before.values())),
        "after": summarize(list(after.values())),
        "errors": {"before": [r for r in before.values() if "error" in r], "after": [r for r in after.values() if "error" in r]},
        "paired": {},
    }
    for group, recipes in groups.items():
        a, b = summarize([before[r] for r in recipes]), summarize([after[r] for r in recipes])
        result["paired"][group] = {"before": a, "after": b, "delta": {key: b[key] - a[key] for key in a}}
    text = json.dumps(result, indent=2)
    (args.after / "comparison.json").write_text(text + "\n")
    print(text)


if __name__ == "__main__":
    main()
