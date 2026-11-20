"""Summarize the opt-in diagnostic exports; no gameplay or mutable-content assertions."""
import collections
import json
from pathlib import Path
import statistics

root = Path(__file__).resolve().parent
summary = {}
for name in ("before", "after"):
    gameplay = json.loads((root / f"{name}.json").read_text(encoding="utf-8-sig"))
    frequency = json.loads((root / f"{name}.json.frequency.json").read_text(encoding="utf-8-sig"))
    rows = gameplay["Rows"]
    distribution = collections.defaultdict(lambda: {"Count": 0, "RegionsPresent": 0, "ExpectedCredits": 0})
    for region in frequency["Rows"]:
        for population in region["Population"]:
            total = distribution[population["TypeId"]]
            total["Count"] += population["Count"]
            total["RegionsPresent"] += 1
            total["ExpectedCredits"] += population["HypotheticalExpectedCredits"]
            for field in ("LootDropSuccesses", "CreditsDropSuccesses", "CreditsDropAmount"):
                total[field] = population[field]
    terminals = {row["Seed"]: row["Closing"] for row in rows}
    fields = ("GeneratedCredits", "CollectedCredits", "OtherSideCollectedCredits", "SaleCredits", "UncollectedCredits")
    summary[name] = {
        "ContentRevision": gameplay["ContentRevision"],
        "Seeds": list(terminals), "PlayedRegions": len(rows), "GeneratedRegions": len(frequency["Rows"]),
        "Outcomes": dict(collections.Counter(row["Outcome"] for row in rows)),
        "Totals": {field: sum(row[field] for row in rows) for field in fields},
        "IncomePerPlayedRegion": {"Minimum": min(row["CollectedCredits"] + row["SaleCredits"] for row in rows),
            "Median": statistics.median(row["CollectedCredits"] + row["SaleCredits"] for row in rows),
            "Maximum": max(row["CollectedCredits"] + row["SaleCredits"] for row in rows),
            "Mean": sum(row["CollectedCredits"] + row["SaleCredits"] for row in rows) / len(rows)},
        "TerminalBankBySeed": terminals,
        "FirstAffordableRegionAndRound": {str(threshold): [
            {"Seed": seed, "RegionIndex": match["RegionIndex"], "Round": match["FirstAffordableRound"][str(threshold)]}
            for seed in terminals if (match := next((row for row in rows if row["Seed"] == seed
                and row["FirstAffordableRound"][str(threshold)] is not None), None)) is not None]
            for threshold in (10, 12, 15, 24, 50, 75)},
        "Population": dict(sorted(distribution.items())),
        "GenerationExpectedCreditsByIndex": {index: {
            "Minimum": min(values), "Median": statistics.median(values), "Maximum": max(values),
            "Mean": statistics.mean(values)} for index in range(6)
            if (values := [sum(p["HypotheticalExpectedCredits"] for p in row["Population"])
                for row in frequency["Rows"] if row["RegionIndex"] == index])},
        "GeneratedMonsters": sum(total["Count"] for total in distribution.values()),
        "GeneratedCreditBearingMonsters": sum(total["Count"] for total in distribution.values() if total["CreditsDropSuccesses"] > 0),
        "PlayedRegionExpectedInitialDropCredits": sum(row["HypotheticalExpectedInitialDropCredits"] for row in rows),
        "CreditsConservationDifferences": [{"Seed": row["Seed"], "RegionIndex": row["RegionIndex"]}
            for row in rows if row["GeneratedCredits"] != row["CollectedCredits"] + row["OtherSideCollectedCredits"] + row["UncollectedCredits"]],
    }

legacy = json.loads((root.parent / "economy-phase-7/gameplay-income.json").read_text(encoding="utf-8-sig"))
before = json.loads((root / "before.json").read_text(encoding="utf-8-sig"))
before_by_key = {(row["Seed"], row["RegionIndex"]): row for row in before["Rows"]}
summary["LegacyReproduction"] = {
    "RowsCompared": len(legacy["Rows"]),
    "DifferingFields": [{"Seed": row["Seed"], "RegionIndex": row["RegionIndex"], "Field": field,
        "Legacy": value, "Before": before_by_key.get((row["Seed"], row["RegionIndex"]), {}).get(field)}
        for row in legacy["Rows"] for field, value in row.items()
        if before_by_key.get((row["Seed"], row["RegionIndex"]), {}).get(field) != value],
}
after = json.loads((root / "after.json").read_text(encoding="utf-8-sig"))
summary["PairedPlayedRegionKeys"] = sorted(set(before_by_key) & {(row["Seed"], row["RegionIndex"]) for row in after["Rows"]})
old_frequency = json.loads((root / "before.json.frequency.json").read_text(encoding="utf-8-sig"))
new_frequency = json.loads((root / "after.json.frequency.json").read_text(encoding="utf-8-sig"))
old_populations = {(row["Seed"], row["RegionIndex"]): {p["TypeId"]: p["Count"] for p in row["Population"]}
    for row in old_frequency["Rows"]}
summary["GenerationPopulationPairs"] = {
    "Compared": len(new_frequency["Rows"]),
    "Different": [{"Seed": row["Seed"], "RegionIndex": row["RegionIndex"]} for row in new_frequency["Rows"]
        if old_populations.get((row["Seed"], row["RegionIndex"])) != {p["TypeId"]: p["Count"] for p in row["Population"]}],
}
probe = json.loads((root / "after-889-eligibility.json").read_text(encoding="utf-8-sig"))
after_by_key = {(row["Seed"], row["RegionIndex"]): row for row in after["Rows"]}
summary["EligibilityProbe"] = {
    "RowsCompared": len(probe["Rows"]),
    "DifferentSharedFields": [{"RegionIndex": row["RegionIndex"], "Field": field}
        for row in probe["Rows"] for field, value in row.items()
        if field in after_by_key[(row["Seed"], row["RegionIndex"])]
        and after_by_key[(row["Seed"], row["RegionIndex"])][field] != value],
    "FundedStatesObserved": sum(row["FundedStatesObserved"] for row in probe["Rows"]),
    "ObservedFundedLivingBoots": any(row["FirstFundedLivingBoots"] is not None for row in probe["Rows"]),
    "ObservedFundedBootsStationQuotes": sum(len(row["NaturallyReachedFundedBootsStationQuotes"]) for row in probe["Rows"]),
}
(root / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
print(json.dumps({name: {key: value for key, value in summary[name].items() if key != "Population"}
    for name in ("before", "after", "LegacyReproduction")}, indent=2))
