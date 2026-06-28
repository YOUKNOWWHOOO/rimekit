import os, sys, json
sys.stdout.reconfigure(encoding='utf-8')

from PIL import ImageGrab, Image
import numpy as np

BOX = (1817, 854, 1842, 879)
REFS = os.path.join(os.environ.get("TEMP", "."), "ime_refs")
REF_CHINESE = os.path.join(REFS, "ref_chinese.png")
REF_ENGLISH = os.path.join(REFS, "ref_english.png")


def detect():
    if not os.path.exists(REF_CHINESE) or not os.path.exists(REF_ENGLISH):
        return {"mode": "unknown", "confidence": 0.0, "error": "reference templates not found"}

    current = ImageGrab.grab(bbox=BOX).convert("L")
    cur_arr = np.array(current, dtype=np.float32)

    chinese = np.array(Image.open(REF_CHINESE).convert("L"), dtype=np.float32)
    english = np.array(Image.open(REF_ENGLISH).convert("L"), dtype=np.float32)

    # Normalized difference: lower = more similar
    diff_cn = np.sum(np.abs(cur_arr - chinese)) / cur_arr.size
    diff_en = np.sum(np.abs(cur_arr - english)) / cur_arr.size

    # The reference with smaller diff is the match
    if diff_cn < diff_en:
        mode = "chinese"
        conf = min(0.95, 1.0 - (diff_cn / 255.0) * 2.0)
    else:
        mode = "english"
        conf = min(0.95, 1.0 - (diff_en / 255.0) * 2.0)

    # If difference is too large, it's uncertain
    if abs(diff_cn - diff_en) < 1.0:
        conf = min(conf, 0.5)

    return {
        "mode": mode, "confidence": round(conf, 2),
        "diff_chinese": round(float(diff_cn), 2), "diff_english": round(float(diff_en), 2),
        "box": BOX
    }


if __name__ == "__main__":
    print(json.dumps(detect(), ensure_ascii=False))
