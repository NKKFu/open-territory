from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent
frames = sorted((root / "native-final" / "frames").glob("frame-*.png"))
if len(frames) < 10:
    raise RuntimeError("Current native frames are missing")
gaps = [(b.stat().st_mtime - a.stat().st_mtime, i + 1) for i, (a, b) in enumerate(zip(frames, frames[1:]))]
gap, split = max(gaps)
if gap < 1.2:
    raise RuntimeError("Could not identify the interval between the two recorded actions")
for name, clip, duration in [("carjacking-preview.gif", frames[:split], 100), ("boarding-preview.gif", frames[split:], 80)]:
    pictures = []
    for path in clip:
        with Image.open(path) as picture:
            pictures.append(picture.convert("RGB").crop((315, 250, 900, 570)).quantize(colors=160))
    pictures[0].save(root / name, save_all=True, append_images=pictures[1:], duration=duration, loop=0, optimize=True)
    print(name, len(pictures), "native frames")
