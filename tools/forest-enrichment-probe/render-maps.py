"""Render three Forest-only QA maps; requires Pillow, uses archived Session B masks."""
import argparse
import json
from collections import deque
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("maps")
parser.add_argument("session_b_results")
parser.add_argument("--output", required=True)
parser.add_argument("--candidate", action="store_true", help="Inspect an unbenchmarked candidate; still verify baseline but skip final-evidence equality")
args = parser.parse_args()
rows = [json.loads(line) for line in Path(args.maps).read_text().splitlines()]
evidence = {r["key"]: r for r in map(json.loads, Path(__file__).with_name("evidence.jsonl").read_text().splitlines()) if r["intensity"] == 3}
palette = {"tree": "#214f37", "grass": "#a4bd80", "dirt": "#af875a", "solid-rock": "#777780", "water": "#467fa3", "wet-grass": "#77a79a", "rocky-water": "#8dabb7", "stepping-stones": "#a2b5ad"}
scale = 3
tile = 120 * scale
image = Image.new("RGB", (3 * (tile + 20), len(rows) * (tile + 55) + 65), "#faf8f0")
draw = ImageDraw.Draw(image)
draw.text((12, 10), "Forest only: original / old Forest mask / Phase 2", fill="black")
draw.text((12, 29), "Dark green Trees; blue Water; gray Rock; yellow new root-accessible land; orange isolated new land", fill="black")


def anchor_access(terrain, original, w):
    def neighbors(i):
        if i >= w: yield i-w
        if i % w: yield i-1
        if i % w < w-1: yield i+1
        if i+w < len(terrain): yield i+w
    def components(values):
        pending = {i for i, t in enumerate(values) if t in ("grass", "dirt", "wet-grass", "rocky-water", "stepping-stones")}
        found = []
        for start in sorted(pending):
            if start not in pending: continue
            pending.remove(start); region = {start}; queue = deque([start])
            while queue:
                for j in neighbors(queue.popleft()):
                    if j in pending:
                        pending.remove(j); region.add(j); queue.append(j)
            found.append(region)
        return found
    original_regions = components(original)
    root = min(min(original_regions, key=lambda r: (-len(r), min(r)))) if original_regions else -1
    return next((r for r in components(terrain) if root in r), set())


for row_number, row in enumerate(rows):
    if not args.candidate:
        assert row["fingerprint"] == evidence[row["key"]]["after"]["fingerprint"]
    original = row["original"]
    session_b = json.loads((Path(args.session_b_results) / (row["key"] + "-I3.map.json")).read_text())
    # Restore every non-Tree original Cell: Session B also contains Rock edits.
    old = [session_b["terrain"][i] if t == "tree" else t for i, t in enumerate(original)]
    current = row["terrain"]
    assert sum(t != "tree" and original[i] == "tree" for i, t in enumerate(old)) == evidence[row["key"]]["before"]["metrics"]["newlyTraversable"]
    for column, (title, terrain) in enumerate((("Original", original), ("Before Forest", old), ("Phase 2 Forest", current))):
        reachable = anchor_access(terrain, original, row["w"])
        if column and not (column == 2 and args.candidate):
            side = "before" if column == 1 else "after"
            assert sum(original[i] == "tree" and terrain[i] != "tree" for i in reachable) == evidence[row["key"]][side]["metrics"]["anchoredNewCells"]
        ox, oy = 10 + column * (tile + 20), 70 + row_number * (tile + 55)
        draw.text((ox, oy-19), row["key"] if column == 0 else title, fill="black")
        for i, t in enumerate(terrain):
            color = palette[t]
            if original[i] == "tree" and t != "tree":
                color = "#f4d35e" if i in reachable else "#df924d"
            x, y = ox + i % row["w"] * scale, oy + i // row["w"] * scale
            draw.rectangle((x, y, x+scale-1, y+scale-1), fill=color)
image.save(args.output)
print(args.output)
