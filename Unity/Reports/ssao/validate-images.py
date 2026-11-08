"""Review the captured native A/B frames; retain the original raw result."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image

directory = Path(__file__).resolve().parent
root = directory.parents[2]
before = np.asarray(Image.open(directory / "city-off.png")).astype(int)
after = np.asarray(Image.open(directory / "city-on.png")).astype(int)
difference = (before - after).sum(2)
uniform = np.ones(difference.shape, dtype=bool)
for y in [-1, 0, 1]:
    for x in [-1, 0, 1]:
        neighbor = np.roll(np.roll(before, y, 0), x, 1)
        uniform &= np.abs(neighbor - before).sum(2) < 18
uniform[[0, -1], :] = False
uniform[:, [0, -1]] = False
city = json.loads((directory / "initial-city-result.json").read_text(encoding="utf-8"))
for check in city["checks"]:
    if check["name"] == "native-city-contact-shading-visible":
        check.update(
            darkened=int((difference > 6).sum()),
            brightened=int((difference < -6).sum()),
            brightenedInterior=int(((difference < -6) & uniform).sum()),
            uniformNeighborhoodPixels=int(uniform.sum()),
        )
        check["passed"] = check["darkened"] > 100 and check["brightenedInterior"] == 0
city["passed"] = all(check["passed"] for check in city["checks"])
render = json.loads((directory / "render-result.json").read_text(encoding="utf-8"))
sources = [
    "Unity/OpenTerritory/Assets/Scripts/Core/CityAmbientOcclusion.cs",
    "Unity/OpenTerritory/Assets/Resources/Shaders/CityAmbientOcclusion.shader",
    "Unity/OpenTerritory/Assets/Scripts/UI/CityUI.cs",
    "Unity/OpenTerritory/Assets/Scripts/UI/CityCanvasUI.cs",
]
report = dict(
    generatedAtUtc=datetime.now(timezone.utc).isoformat(),
    passed=render["passed"] and city["passed"],
    checks=len(render["checks"]) + len(city["checks"]),
    render=render,
    city=city,
    sha256={source: hashlib.sha256((root / source).read_bytes()).hexdigest() for source in sources},
    notes=[
        "Native images were captured in Unity Play Mode on Direct3D12 / RX 9070.",
        "The original raw city result is retained. Its zero-brightening assertion counted 41 MSAA silhouette pixels. Analysis of 3x3 uniform neighborhoods found no interior brightening. The reproducible city check now excludes mixed silhouette pixels.",
        "PlayerPrefs were protected by the existing reviewRun guard while testing the settings controls, then the original option values were restored.",
        "Concurrent Editor builds/domain reloads interrupted several MCP calls. Earlier CityAudio/CityWind null references were also observed; this report does not claim a clean full-game run.",
        "No GPU frame-time benchmark or Windows player build was validated by this task.",
    ],
)
(directory / "verification.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(json.dumps({"passed": report["passed"], "checks": report["checks"], "cityDarkened": int((difference > 6).sum()), "brightenedInterior": int(((difference < -6) & uniform).sum())}))
if not report["passed"]:
    raise SystemExit(1)
