import json
from pathlib import Path
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[2]
data = json.loads((root / "Verification/rhythm-ui-layout.json").read_text(encoding="utf-8"))
parts = data["bands"]
ids = list(dict.fromkeys(p["id"] for p in parts))[:2]
svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="800" height="310" viewBox="0 0 800 310">',
       '<rect width="800" height="310" fill="#fbfbf1"/>',
       '<g font-family="Arial,sans-serif" fill="#353b31">',
       '<text x="24" y="30" font-size="18">R01: actual physical bands / 192-unit offset</text>',
       '<text x="24" y="53" font-size="12">Unexpanded rectangles from ObstacleGeometry; no connecting collider or trajectory.</text>']
for index, key in enumerate(ids):
    bands = [p for p in parts if p["id"] == key]
    left, top, sx, sy = 45 + index * 390, 96, 1.25, .65
    minimum = min(p["X"] for p in bands)
    svg += [f'<text x="{left}" y="81" font-size="14">{"Floor → ceiling" if bands[0]["fromFloor"] else "Ceiling → floor"}</text>',
            f'<rect x="{left-10}" y="{top}" width="300" height="{240*sy}" fill="#f3f1e6" stroke="#a5af92"/>']
    for band in bands:
        x, y = left + (band["X"]-minimum)*sx, top + band["Y"]*sy
        w, h = band["Width"]*sx, band["Height"]*sy
        svg += [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#d9a05b" stroke="#ad7a47"/>',
                f'<text x="{x-2}" y="{top+240*sy+20}" font-size="11">X+{band["X"]-minimum:g}</text>']
    svg += [f'<text x="{left}" y="292" font-size="11">Upper Y0–140 · lower Y100–240 · each width16</text>']
svg += ['</g></svg>']
(root / "Verification/rhythm-offset-bands.svg").write_text("\n".join(svg), encoding="utf-8")
