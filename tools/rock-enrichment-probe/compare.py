"""Compare the exact paired Rock-only corpus. Python standard library only."""
import argparse
import json
import statistics
from pathlib import Path


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines()]
    keyed = {(r["key"], r["intensity"]): r for r in rows}
    if len(rows) != 180 or len(keyed) != 180:
        raise ValueError("Expected 45 worlds × four intensities, without duplicate pairs")
    return keyed


def summarize(rows):
    metrics = [r["metrics"] for r in rows]
    features = [f for m in metrics for f in m["features"]]
    formations = [f for m in metrics for f in m["formations"]]
    budget = sum(f["budget"] for f in formations)
    used = sum(f["used"] for f in formations)
    rock_stages = [s for r in rows for s in r["stages"] if s["stepId"] == "rock"]
    return dict(
        cases=len(rows), newlyTraversable=sum(m["newlyTraversable"] for m in metrics),
        anchoredNewCells=sum(m["anchoredNewCells"] for m in metrics),
        previousGroundConnected=sum(m["previousGroundConnected"] for m in metrics),
        features=len(features), anchoredFeatures=sum(f["anchored"] for f in features),
        usefulAnchoredFeatures=sum(f["usefulAnchoredSpace"] for f in features),
        newlyAnchoredNaturalClearings=sum(not f["beforeAnchored"] and f["afterAnchored"] for m in metrics for f in m["naturalClearings"]),
        deepBlockingCells=sum(m["deepBlockingCells"] for m in metrics),
        blockingBeyondMainSightRange=sum(m["blockingBeyondMainSightRange"] for m in metrics),
        maxRemainingDepth=max(m["afterMaxDepth"] for m in metrics),
        largestRemainingMass=max(m["largestRemainingExactMass"] for m in metrics),
        nominalBudget=budget, usedBudget=used, budgetUtilization=used / budget if budget else 0,
        occupiedSpatialBins=sum(f["occupiedSpatialBins"] for f in formations),
        touchedSpatialBins=sum(f["touchedSpatialBins"] for f in formations),
        routingSearches=sum(m["routingSearches"] for m in metrics),
        routingVisitedCells=sum(m["routingVisitedCells"] for m in metrics),
        medianCaseMs=statistics.median(r["medianMs"] for r in rows),
        sumCaseMedianMs=sum(r["medianMs"] for r in rows),
        medianCaseAllocatedBytes=statistics.median(r["allocatedMedian"] for r in rows),
        sumCaseMedianAllocatedBytes=sum(r["allocatedMedian"] for r in rows),
        sumRockStageMedianMs=sum(s["medianMs"] for s in rock_stages),
        sumRockStageMedianAllocatedBytes=sum(s["allocatedMedian"] for s in rock_stages),
        engineValid=all(r["engineValid"] for r in rows), deterministic=all(r["deterministic"] for r in rows),
        nonRockEdits=sum(m["nonRockEdits"] for m in metrics),
    )


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("before")
parser.add_argument("after", nargs="?")
parser.add_argument("--output", required=True)
parser.add_argument("--evidence-output", help="Write compact paired outcomes and measured costs as JSONL")
parser.add_argument("--verify-evidence", help="Verify stable outcomes against a repository compact JSONL; timing/allocation costs are excluded")
args = parser.parse_args()
before = load(args.before)
after = load(args.after) if args.after else None
result = {"definition": "Rock only; original principal top-left root retained; cardinal ordinary-land topology; no Civilization/routes/Population"}
groups = {f"intensity-{i}": [k for k in before if k[1] == i] for i in range(4)}
groups.update({f"high-{w}": [k for k, r in before.items() if k[1] == 3 and r["w"] == w] for w in (30, 60, 120)})
groups.update({f"high-{style}": [k for k, r in before.items() if k[1] == 3 and r["style"] == style] for style in ("Woodland", "RockyWilds", "DenseForest", "DenseRock", "FineMixed")})
result["groups"] = {name: {"before": summarize([before[k] for k in keys]), **({"after": summarize([after[k] for k in keys])} if after else {})} for name, keys in groups.items()}
if after:
    if before.keys() != after.keys():
        raise ValueError("Corpus pair mismatch")
    disabled_changed = [k for k in before if k[1] == 0 and before[k]["fingerprint"] != after[k]["fingerprint"]]
    if disabled_changed:
        raise ValueError(f"Disabled baselines changed: {disabled_changed}")
    result["disabledFingerprintsUnchanged"] = True
    result["pairedHigh"] = [dict(key=k[0], beforeFingerprint=before[k]["fingerprint"], afterFingerprint=after[k]["fingerprint"],
        **{metric: {"before": before[k]["metrics"][metric], "after": after[k]["metrics"][metric]} for metric in ("newlyTraversable", "anchoredNewCells", "previousGroundConnected", "afterMaxDepth", "deepBlockingCells", "blockingBeyondMainSightRange")}) for k in before if k[1] == 3]
    if args.evidence_output or args.verify_evidence:
        def compact(row):
            m = row["metrics"]
            return dict(fingerprint=row["fingerprint"], deterministic=row["deterministic"], engineValid=row["engineValid"],
                metrics={k: v for k, v in m.items() if not isinstance(v, (list, dict))},
                features=len(m["features"]), anchoredFeatures=sum(f["anchored"] for f in m["features"]),
                usefulAnchoredFeatures=sum(f["usefulAnchoredSpace"] for f in m["features"]),
                newlyAnchoredNaturalClearings=sum(not f["beforeAnchored"] and f["afterAnchored"] for f in m["naturalClearings"]),
                budget=sum(f["budget"] for f in m["formations"]),
                usedBudget=sum(f["used"] for f in m["formations"]),
                occupiedSpatialBins=sum(f["occupiedSpatialBins"] for f in m["formations"]),
                touchedSpatialBins=sum(f["touchedSpatialBins"] for f in m["formations"]))
        evidence = [dict(key=k[0], intensity=k[1], code=after[k]["code"], before=compact(before[k]), after=compact(after[k]),
            cost={side: {f: row[f] for f in ("medianMs", "allocatedMedian")} for side, row in (("before", before[k]), ("after", after[k]))}) for k in before]
        if args.evidence_output:
            Path(args.evidence_output).write_text("".join(json.dumps(r, separators=(",", ":")) + "\n" for r in evidence), encoding="utf-8")
        if args.verify_evidence:
            expected = [json.loads(line) for line in Path(args.verify_evidence).read_text(encoding="utf-8").splitlines()]
            stable = lambda rows: [{k: v for k, v in row.items() if k != "cost"} for row in rows]
            if stable(expected) != stable(evidence):
                raise ValueError("Stable compact evidence differs")
            result["compactEvidenceVerified"] = len(evidence)
elif args.evidence_output or args.verify_evidence:
    raise ValueError("Compact evidence requires both before and after inputs")
Path(args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result["groups"]["intensity-3"], indent=2))


