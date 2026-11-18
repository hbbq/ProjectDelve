"""Measure coverage and terrain seams in Expedition journey exports."""

import argparse
import json
from collections import Counter
from itertools import groupby
from pathlib import Path
from statistics import correlation, mean, median


CATEGORIES = ("forest", "rock", "water")
LAGS = (1, 2, 5, 10, 20)
STABLE_LIMIT = 5
MAJOR_LIMIT = 15


def landscape_distance(left, right):
    differences = [right[key] - left[key] for key in CATEGORIES]
    return (sum(map(abs, differences)) + abs(sum(differences))) / 2


def lag_statistics(regions, key):
    result = {}
    for lag in LAGS:
        pairs = [(a[key], b[key]) for a, b in zip(regions, regions[lag:]) if a[key] is not None and b[key] is not None]
        if pairs:
            distances = [landscape_distance(a, b) for a, b in pairs]
            result[lag] = {
                "pairs": len(pairs),
                "categoryMeanAbsoluteChangePp": {
                    category: mean(abs(b[category] - a[category]) for a, b in pairs) for category in CATEGORIES
                },
                "meanLandscapeChangePp": mean(distances),
                "medianLandscapeChangePp": median(distances),
                "maxLandscapeChangePp": max(distances),
            }
    return result


def landscape_transitions(regions):
    stable_runs = []
    big_transitions = []
    adjacent = []
    run_start = None
    for left, right in zip(regions, regions[1:]):
        distance = landscape_distance(left["baseCoverage"], right["baseCoverage"])
        adjacent.append(distance)
        if distance >= MAJOR_LIMIT:
            big_transitions.append({"from": left["index"], "to": right["index"], "changePp": distance})
        if distance <= STABLE_LIMIT:
            if run_start is None:
                run_start = left["index"]
        elif run_start is not None:
            stable_runs.append({"from": run_start, "to": left["index"], "regions": left["index"] - run_start + 1})
            run_start = None
    if run_start is not None:
        last = regions[-1]["index"]
        stable_runs.append({"from": run_start, "to": last, "regions": last - run_start + 1})
    return {
        "stableLinks": sum(distance <= STABLE_LIMIT for distance in adjacent),
        "stableRuns": stable_runs,
        "majorTransitions": big_transitions,
        "adjacentLandscapeChangePp": adjacent,
    }


def edge_pairs(left, right, axis, stride):
    if axis == "x":
        return ((left[y * stride + stride - 1], right[y * stride]) for y in range(stride))
    start = (stride - 1) * stride
    return ((left[start + x], right[x]) for x in range(stride))


def seam_statistics(regions, key, stride):
    adjacent = []
    for a, b in zip(regions, regions[1:]):
        if a[key] is None or b[key] is None:
            continue
        axis = "x" if a["region"]["x"] != b["region"]["x"] else "y"
        left, right = sorted((a, b), key=lambda region: region["region"][axis])
        changed = sum(x != y for x, y in edge_pairs(left[key], right[key], axis, stride))
        adjacent.append({"from": a["index"], "to": b["index"], "axis": axis, "changedPairs": changed})
    differing = pairs = 0
    for region in regions:
        terrain = region[key]
        if terrain is None:
            continue
        for y in range(stride):
            for x in range(stride):
                index = y * stride + x
                if x + 1 < stride:
                    differing += terrain[index] != terrain[index + 1]
                    pairs += 1
                if y + 1 < stride:
                    differing += terrain[index] != terrain[index + stride]
                    pairs += 1
    seam_pairs = len(adjacent) * stride
    return {
        "seamPairs": seam_pairs,
        "seamChangePercent": 100 * sum(pair["changedPairs"] for pair in adjacent) / seam_pairs if seam_pairs else None,
        "interiorPairs": pairs,
        "interiorChangePercent": 100 * differing / pairs if pairs else None,
        "seams": adjacent,
    }


def ranges(rows):
    return {key: [min(row[key] for row in rows), max(row[key] for row in rows)] for key in rows[0]}


def dominant_runs(rows):
    # Match the overview's first encountered Cell tie breaker.
    def dominant(row):
        return Counter(row["baseTerrain"]).most_common(1)[0][0]

    result = []
    for terrain, run in groupby(rows, dominant):
        indices = [row["index"] for row in run]
        result.append({"terrainId": terrain, "from": indices[0], "to": indices[-1], "regions": len(indices)})
    return result


def summarize(journey):
    rows = journey["regions"]
    complete = [row for row in rows if row["features"] is not None]
    controls = [row["parameters"] for row in rows]
    return {
        "worldSeed": journey["worldSeed"],
        "baselineRecipe": journey["baselineRecipe"],
        "coordinates": [[row["region"]["x"], row["region"]["y"]] for row in rows],
        "completed": len(complete),
        "coverageRanges": {key: ranges([row[key] for row in rows if row[key] is not None])
                           for key in ("sampledCoverage", "baseCoverage", "finalCoverage")
                           if any(row[key] is not None for row in rows)},
        "fieldCenterRanges": ranges([row["fields"] for row in rows]),
        "lagMetrics": {key: lag_statistics(rows, key) for key in ("sampledCoverage", "baseCoverage", "finalCoverage")},
        "landscapeTransitions": landscape_transitions(rows),
        "dominantTerrainRuns": dominant_runs(rows),
        "seams": {key: seam_statistics(rows, key, journey["stride"]) for key in ("baseTerrain", "finalTerrain")},
        "parameterFrequencies": {key: dict(Counter(str(row[key]) for row in controls)) for key in controls[0]},
        "parameterChanges": {key: sum(a[key] != b[key] for a, b in zip(controls, controls[1:])) for key in controls[0]},
        "features": ranges([row["features"] for row in complete]) if complete else None,
        "meanTerrainChangedByFullPipelinePercent": mean(
            100 * row["features"]["terrainChangedCells"] / len(row["baseTerrain"]) for row in complete
        ) if complete else None,
        "meanCoverageShiftPp": {
            category: mean(row["finalCoverage"][category] - row["baseCoverage"][category] for row in complete)
            for category in CATEGORIES
        } if complete else None,
        "generationSeconds": {
            "sum": journey["generationMilliseconds"] / 1000,
            "median": median(row["generationMilliseconds"] for row in complete) / 1000,
            "max": max(row["generationMilliseconds"] for row in complete) / 1000,
        } if complete else None,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Diagnostic root containing seed-*/journey.json")
    arguments = parser.parse_args()
    sources = sorted(arguments.directory.glob("seed-*/journey.json"))
    if not sources:
        parser.error("no journey.json exports found")
    exports = [json.loads(path.read_text(encoding="utf-8")) for path in sources]
    complete = [row for export in exports for row in export["regions"] if row["finalCoverage"] is not None]
    coverage_correlation = {}
    for category in CATEGORIES:
        base = [row["baseCoverage"][category] for row in complete]
        final = [row["finalCoverage"][category] for row in complete]
        coverage_correlation[category] = correlation(base, final) if len(set(base)) > 1 and len(set(final)) > 1 else None
    analysis = {
        "stableLinkLimitPp": STABLE_LIMIT, "majorTransitionLimitPp": MAJOR_LIMIT,
        "pooledBaseFinalCoverageCorrelation": coverage_correlation,
        "journeys": [summarize(export) for export in exports],
    }
    destination = arguments.directory / "metrics.json"
    destination.write_text(json.dumps(analysis, indent=2) + "\n", encoding="utf-8")
    print(destination)


if __name__ == "__main__":
    main()
