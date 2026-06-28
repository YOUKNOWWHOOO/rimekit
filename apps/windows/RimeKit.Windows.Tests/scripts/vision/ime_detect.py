import sys, os, json, base64, http.client, time
sys.stdout.reconfigure(encoding="utf-8")

def detect_ime_mode(screenshot_path, timeout=30):
    """Call Kimi K2.6 to detect IME mode from a full-screen screenshot."""

    auth_paths = [
        os.path.join(os.environ.get("USERPROFILE", ""), ".config", "opencode", "auth.json"),
        os.path.join(os.path.expanduser("~"), ".local", "share", "opencode", "auth.json"),
        os.path.join(os.path.expanduser("~"), ".config", "opencode", "auth.json"),
    ]
    auth = None
    for p in auth_paths:
        if os.path.exists(p):
            auth = json.load(open(p, encoding="utf-8"))
            break
    if auth is None:
        return {"mode": "error", "raw": "auth.json not found", "confidence": "none"}

    key = auth.get("opencode-go", {}).get("key", "")

    with open(screenshot_path, "rb") as f:
        b64 = base64.b64encode(f.read()).decode()

    body = json.dumps({
        "model": "kimi-k2.6",
        "messages": [{
            "role": "user",
            "content": [
                {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}},
                {"type": "text", "text": "Look at the taskbar language indicator (taskbar may be on right side, vertical). Is the current input mode Chinese (showing \u4e2d) or English (showing \u82f1)? Answer ONLY one word: chinese or english. No other text."}
            ]
        }],
        "max_tokens": 50,
        "temperature": 0
    })

    conn = http.client.HTTPSConnection("opencode.ai", timeout=timeout)
    conn.request("POST", "/zen/go/v1/chat/completions", body, {
        "Content-Type": "application/json",
        "Authorization": f"Bearer {key}"
    })
    resp = conn.getresponse()
    data = json.loads(resp.read())

    if "choices" in data and len(data["choices"]) > 0:
        content = data["choices"][0].get("message", {}).get("content", "").strip().lower()
        if "chinese" in content:
            return {"mode": "chinese", "raw": content, "confidence": "high"}
        elif "english" in content:
            return {"mode": "english", "raw": content, "confidence": "high"}
        else:
            return {"mode": "unknown", "raw": content, "confidence": "low"}
    return {"mode": "error", "raw": str(data), "confidence": "none"}

if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get("TEMP", "."), "ime_check.png")
    result = detect_ime_mode(path)
    print(json.dumps(result))
