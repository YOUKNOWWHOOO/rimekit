import sys, os, json
sys.stdout.reconfigure(encoding="utf-8")

out_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get("TEMP", "."), "screenshot.png")

from PIL import ImageGrab
img = ImageGrab.grab()
img.save(out_path, "PNG")

print(json.dumps({"status": "ok", "path": out_path, "size": f"{img.size[0]}x{img.size[1]}"}))
