import sys, os, json, time, subprocess, glob as globmod, argparse, tempfile

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

if os.environ.get("RIMEKIT_ALLOW_REAL_INPUT") != "1":
    print(json.dumps({"status": "blocked", "error": "RIMEKIT_ALLOW_REAL_INPUT not set"}))
    sys.exit(1)

# ── helpers ──

def _log(msg):
    sys.stderr.write(f"[probe] {msg}\n")
    sys.stderr.flush()

def find_ahk():
    for c in [
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs", "AutoHotkey", "v2", "AutoHotkey64.exe"),
        os.path.join(os.environ.get("TEMP", ""), "ahk-v2-portable", "AutoHotkey64.exe"),
    ]:
        if os.path.exists(c):
            return c
    return None

def find_weasel_deployer():
    for base in [r"C:\Program Files\Rime", r"C:\Program Files (x86)\Rime"]:
        if os.path.isdir(base):
            for candidate in sorted(globmod.glob(os.path.join(base, "weasel-*", "WeaselDeployer.exe")), reverse=True):
                if os.path.exists(candidate):
                    return candidate
    return None

def ensure_notepad_foreground(hwnd, label=""):
    import win32gui, win32con
    import ctypes
    # Disable foreground lock timeout (needed when process tree runs in background)
    SPI_FOREGROUNDLOCKTIMEOUT = 0x2001
    ctypes.windll.user32.SystemParametersInfoW(SPI_FOREGROUNDLOCKTIMEOUT, 0, 0, 0)
    for attempt in range(5):
        fg = win32gui.GetForegroundWindow()
        if fg == hwnd:
            return
        try:
            current_tid = ctypes.windll.user32.GetWindowThreadProcessId(fg, None)
            target_tid = ctypes.windll.user32.GetWindowThreadProcessId(hwnd, None)
            if current_tid != target_tid:
                ctypes.windll.user32.AttachThreadInput(target_tid, current_tid, True)
            win32gui.SetForegroundWindow(hwnd)
            win32gui.BringWindowToTop(hwnd)
            if current_tid != target_tid:
                ctypes.windll.user32.AttachThreadInput(target_tid, current_tid, False)
        except Exception:
            try:
                win32gui.SetForegroundWindow(hwnd)
            except Exception:
                pass
        time.sleep(0.3)
    fg = win32gui.GetForegroundWindow()
    if fg != hwnd:
        _log(f"FATAL: SetForegroundWindow FAILED for hwnd={hwnd} ({label}). fg={fg}")
        raise RuntimeError(f"SetForegroundWindow FAILED for hwnd={hwnd}. Foreground hwnd={fg}. Aborting.")

def get_hkl(hwnd):
    import win32process, win32api
    try:
        tid = win32process.GetWindowThreadProcessId(hwnd)[0]
        return win32api.GetKeyboardLayout(tid)
    except Exception:
        return 0

def take_screenshot(output_path):
    import take_screenshot as ts
    old_argv = sys.argv
    sys.argv = ["take_screenshot.py", output_path]
    try:
        ts
    finally:
        sys.argv = old_argv
    _log(f"screenshot saved: {output_path}")

def call_take_screenshot(output_path):
    from PIL import ImageGrab
    img = ImageGrab.grab()
    img.save(output_path, "PNG")
    _log(f"screenshot: {img.size} -> {output_path}")
    return {"size": f"{img.size[0]}x{img.size[1]}"}

def detect_ime_via_template():
    """Template-matching IME detector. Returns dict with mode/confidence, or None on failure."""
    detect_script = os.path.join(VISION_DIR, "detect_ime.py")
    if not os.path.exists(detect_script):
        _log(f"FATAL: detect_ime.py not found at {detect_script}")
        return None

    ref_dir = os.path.join(os.environ.get("TEMP", "."), "ime_refs")
    ref_cn = os.path.join(ref_dir, "ref_chinese.png")
    ref_en = os.path.join(ref_dir, "ref_english.png")
    if not os.path.exists(ref_cn):
        _log(f"FATAL: ref_chinese.png missing at {ref_cn}")
    if not os.path.exists(ref_en):
        _log(f"FATAL: ref_english.png missing at {ref_en}")

    try:
        r = subprocess.run(
            [sys.executable, detect_script],
            capture_output=True, text=True, timeout=10, creationflags=0x08000000)
        if r.returncode != 0:
            _log(f"FATAL: detect_ime.py exit={r.returncode} stdout={r.stdout.strip()[:200]} stderr={r.stderr.strip()[:200]}")
            return None
        raw = r.stdout.strip()
        if not raw:
            _log("FATAL: detect_ime.py produced NO output")
            return None
        result = json.loads(raw)
        mode = result.get("mode", "unknown")
        confidence = result.get("confidence", 0)
        cn_d = result.get("diff_chinese", "?")
        en_d = result.get("diff_english", "?")
        _log(f"template detect: mode={mode} conf={confidence} cn_diff={cn_d} en_diff={en_d}")
        if mode == "unknown":
            _log(f"  reason: {result.get('error','?')}")
            return None
        if confidence < 0.5:
            _log(f"  REJECTED: confidence={confidence} < 0.5 (ambiguous match, ref images may be stale)")
            return None
        return result
    except json.JSONDecodeError:
        _log(f"FATAL: detect_ime.py output not valid JSON: {raw[:200]}")
        return None
    except subprocess.TimeoutExpired:
        _log("FATAL: detect_ime.py timed out after 10s")
        return None
    except Exception as e:
        _log(f"FATAL: detect_ime.py crashed: {e} ({type(e).__name__})")
        return None

# ── resolve paths ──

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
VISION_DIR = SCRIPT_DIR
AHK_EXE = find_ahk()
AHK_TOGGLE = os.path.join(SCRIPT_DIR, "probe_ime_toggle.ahk")
AHK_TYPE = os.path.join(SCRIPT_DIR, "probe_ime_type.ahk")
ACTIVATOR_PATH = os.environ.get("RIMEKIT_WEASEL_ACTIVATOR_PATH", "")
TEMP = os.environ.get("TEMP", os.path.join(os.environ.get("USERPROFILE", "."), "AppData", "Local", "Temp"))
STATE_FILE = os.path.join(TEMP, "rimekit_probe_state.json")
PROBE_DIR = TEMP  # all temp files go here

# ── parse args ──

parser = argparse.ArgumentParser()
parser.add_argument("case_name", nargs="?")
parser.add_argument("input_text", nargs="?")
parser.add_argument("extra_keys", nargs="*", default=[])
parser.add_argument("--phase", choices=["vision", "type", "screenshot"], default=None)
parser.add_argument("--state-file", default=STATE_FILE)
parser.add_argument("--vision-result", default=None)
parser.add_argument("--vision-mode", default=None)
args = parser.parse_args()

if args.vision_mode is None and args.vision_result:
    try:
        vdata = json.loads(open(args.vision_result, encoding="utf-8").read())
        args.vision_mode = vdata.get("mode", "")
    except Exception:
        pass

# ── pre-flight ──

def preflight():
    errors = []
    if not ACTIVATOR_PATH or not os.path.exists(ACTIVATOR_PATH):
        errors.append("RIMEKIT_WEASEL_ACTIVATOR_PATH not set or not found")
    if not AHK_EXE:
        errors.append("AutoHotkey64.exe not found")
    if not os.path.exists(AHK_TOGGLE):
        errors.append(f"AHK toggle missing: {AHK_TOGGLE}")
    if not os.path.exists(AHK_TYPE):
        errors.append(f"AHK type missing: {AHK_TYPE}")
    ts_path = os.path.join(VISION_DIR, "take_screenshot.py")
    if not os.path.exists(ts_path):
        errors.append(f"take_screenshot.py missing: {ts_path}")
    deployer = find_weasel_deployer()
    weasel_running = False
    try:
        out = subprocess.run(["tasklist", "/fi", "IMAGENAME eq WeaselServer.exe"], capture_output=True, text=True)
        weasel_running = "WeaselServer.exe" in out.stdout
    except Exception:
        pass
    if not deployer and not weasel_running:
        errors.append("Weasel not detected. Use CLI: uninstall-weasel → install-weasel → doctor.")
    return errors

preflight_errors = preflight()
if args.phase in ("vision", "type") and preflight_errors:
    print(json.dumps({"status": "blocked", "error": " | ".join(preflight_errors)}, ensure_ascii=False))
    sys.exit(1)

# ──────────────────────────────────────────────────────────────
# Phase: vision
#   Opens Notepad, activates Weasel, takes screenshot, writes state, exits.
#   NOTEPAD STAYS OPEN. State restoration deferred to --phase=type.
# ──────────────────────────────────────────────────────────────

# ──────────────────────────────────────────────────────────────
# Phase: screenshot
#   Opens Notepad, activates IME, types text (no Space), LEAVES NOTEPAD OPEN.
#   Pipeline takes screenshot, then kills notepad.exe.
# ──────────────────────────────────────────────────────────────

if args.phase == "screenshot":
    import win32gui, win32con, win32clipboard, win32process
    from pywinauto import Application

    case_name = args.case_name or "screenshot"
    input_text = args.input_text or "nihao"

    if not os.environ.get("RIMEKIT_ALLOW_REAL_INPUT"):
        print(json.dumps({"status": "blocked", "error": "RIMEKIT_ALLOW_REAL_INPUT not set"}, ensure_ascii=False))
        sys.exit(1)

    # Kill any lingering notepad
    subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
    time.sleep(1)

    # Open Notepad
    try:
        app = Application(backend="win32").start("notepad.exe")
    except Exception:
        app = Application(backend="uia").start("notepad.exe")
    time.sleep(2)
    w = app.window()
    hwnd_notepad = w.handle
    ensure_notepad_foreground(hwnd_notepad, "screenshot open")

    # Activate Weasel profile
    if ACTIVATOR_PATH and os.path.exists(ACTIVATOR_PATH):
        subprocess.run([ACTIVATOR_PATH], capture_output=True)
        time.sleep(2)

    # Detect IME mode via template matching, toggle if needed
    mode = "unknown"
    tm_result = detect_ime_via_template()
    if tm_result:
        mode = tm_result.get("mode", "unknown")
    if mode not in ("chinese", "english"):
        _log(f"template matching failed, mode={mode} — stopping")
        subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
        print(json.dumps({
            "status": "blocked",
            "error": f"template matching failed — detected mode={mode}. Cannot proceed for screenshot.",
            "next_action": "Check ref images in %TEMP%\\ime_refs\\. Verify IMEModeButton area."
        }, ensure_ascii=False))
        sys.exit(1)
    if mode != "chinese":
        _log(f"IME mode={mode}, toggling Shift to Chinese")
        subprocess.run([AHK_EXE, AHK_TOGGLE], capture_output=True)
        time.sleep(2)
        ensure_notepad_foreground(hwnd_notepad, "after Shift toggle")

    # Minimal warm-up: type a+Space to prime IME, then clear
    warmup = """#SingleInstance Force
SetKeyDelay(30, 30)
Send("a")
Sleep(400)
Send("{Space}")
"""
    wp = os.path.join(tempfile.gettempdir(), f"rimekit_warmup_{case_name}.ahk")
    with open(wp, "w", encoding="utf-8") as f:
        f.write(warmup)
    subprocess.run([AHK_EXE, wp], capture_output=True)
    time.sleep(2)
    # Clear Notepad (no active IME composition → Ctrl+A reaches Notepad)
    clear_script = "#SingleInstance Force\nSetKeyDelay(30, 30)\nSend(\"^a\")\nSleep(500)\nSend(\"{Backspace}\")\n"
    cp = os.path.join(tempfile.gettempdir(), f"rimekit_clear_{case_name}.ahk")
    with open(cp, "w", encoding="utf-8") as f:
        f.write(clear_script)
    subprocess.run([AHK_EXE, cp], capture_output=True)
    time.sleep(1)

    # Type test input (IME intercepts in Chinese mode, shows preedit + candidates)
    type_script = f"#SingleInstance Force\nSetKeyDelay(30, 30)\nSend(\"{input_text}\")\n"
    tp = os.path.join(tempfile.gettempdir(), f"rimekit_type_{case_name}.ahk")
    with open(tp, "w", encoding="utf-8") as f:
        f.write(type_script)
    subprocess.run([AHK_EXE, tp], capture_output=True)
    # Wait for candidate window to fully render
    time.sleep(4)

    print(json.dumps({
        "status": "completed",
        "case": case_name,
        "input": input_text,
        "ime_mode": mode,
        "hwnd_notepad": hwnd_notepad,
        "next_action": "NOTEPAD IS OPEN with IME active. Take screenshot now, then kill notepad.exe."
    }, ensure_ascii=False))

    sys.exit(0)


if args.phase == "vision":
    import win32gui, win32con, win32clipboard, win32process, win32api, ctypes

    case_name = args.case_name
    input_text = args.input_text
    extra_keys = " ".join(args.extra_keys) if args.extra_keys else ""

    # ── backup state ──
    prev_fg = win32gui.GetForegroundWindow()
    placement = win32gui.GetWindowPlacement(prev_fg) if prev_fg else None
    prev_max = (placement[1] == win32con.SW_SHOWMAXIMIZED) if placement else False
    prev_cb = ""
    try:
        win32clipboard.OpenClipboard()
        prev_cb = win32clipboard.GetClipboardData(win32con.CF_UNICODETEXT)
        win32clipboard.CloseClipboard()
    except Exception:
        try:
            win32clipboard.CloseClipboard()
        except Exception:
            pass
    prev_hkl = get_hkl(prev_fg) if prev_fg else 0
    win32clipboard.OpenClipboard()
    win32clipboard.EmptyClipboard()
    win32clipboard.CloseClipboard()

    # ── open Notepad ──
    subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
    time.sleep(0.5)
    from pywinauto.application import Application
    app = Application(backend="win32").start("notepad.exe")
    time.sleep(3)
    w = app.window(class_name="Notepad")
    w.wait("visible", 10)
    hwnd_notepad = w.handle
    ensure_notepad_foreground(hwnd_notepad, "after notepad open")
    time.sleep(2)

    # ── activate Weasel profile ──
    if os.path.exists(ACTIVATOR_PATH):
        subprocess.run([ACTIVATOR_PATH], capture_output=True, timeout=15, creationflags=0x08000000)
        _log("activator called")
    time.sleep(3)

    # ── IME mode detection via template matching ──
    tpl = detect_ime_via_template()
    ime_method = "template_matching"
    shift_toggled = False

    if tpl and tpl.get("mode") == "english":
        _log("pixel: english → toggling Shift")
        ensure_notepad_foreground(hwnd_notepad, "before toggle")
        time.sleep(0.3)
        subprocess.run([AHK_EXE, AHK_TOGGLE], capture_output=True, timeout=30, creationflags=0x08000000)
        time.sleep(2)
        shift_toggled = True
        tpl2 = detect_ime_via_template()
        if tpl2 and tpl2.get("mode") != "chinese":
            _log(f"AFTER TOGGLE: still {tpl2.get('mode')} — stopping")
            print(json.dumps({
                "status": "blocked",
                "error": f"template matching still shows {tpl2.get('mode')} after Shift toggle. Cannot proceed.",
                "next_action": "Check if Shift is mapped to IME toggle. Verify IMEModeButton reference images."
            }, ensure_ascii=False))
            subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
            sys.exit(1)
    elif tpl and tpl.get("mode") == "chinese":
        _log("template match: chinese — no toggle needed")
    else:
        _log("template match inconclusive — stopping, no Kimi fallback")
        print(json.dumps({
            "status": "blocked",
            "error": "template matching inconclusive — cannot determine IME mode. Check ref images in %TEMP%\\ime_refs\\",
            "next_action": "Verify that IMEModeButton area is correct. Take a screenshot and compare with reference images manually."
        }, ensure_ascii=False))
        subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
        sys.exit(1)

    # ── AHK typing ──
    ensure_notepad_foreground(hwnd_notepad, "before typing")
    time.sleep(1.5)
    result_file = os.path.join(PROBE_DIR, f"rimekit_probe_ahk_result_{case_name}.txt")
    try: os.remove(result_file)
    except OSError: pass
    p = subprocess.Popen([AHK_EXE, AHK_TYPE, input_text, extra_keys, result_file],
                         creationflags=0x08000000)
    ctypes.windll.user32.AllowSetForegroundWindow(p.pid)
    time.sleep(0.5)
    p.wait(timeout=45)
    _log(f"AHK exit={p.returncode}")
    observed = ""
    ahk_err = ""
    if os.path.exists(result_file):
        content = open(result_file, encoding="utf-8-sig").read()
        _log(f"AHK output ({len(content)} chars): {repr(content[:120])}")
        if content.startswith("OK:"):
            observed = content[3:]
        else:
            ahk_err = content[:200]
    else:
        ahk_err = "result file not created"

    # ── close Notepad ──
    subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
    try: os.remove(result_file)
    except OSError: pass

    # ── restore state ──
    cb_ok = "no"
    try:
        if prev_cb:
            win32clipboard.OpenClipboard()
            win32clipboard.EmptyClipboard()
            win32clipboard.SetClipboardText(prev_cb, win32con.CF_UNICODETEXT)
            win32clipboard.CloseClipboard()
        cb_ok = "yes"
    except Exception:
        cb_ok = "no (exception)"

    win_ok = "no"
    try:
        if prev_fg and win32gui.IsWindow(prev_fg):
            win32gui.SetForegroundWindow(prev_fg)
            if prev_max:
                win32gui.ShowWindow(prev_fg, win32con.SW_MAXIMIZE)
            win_ok = "yes"
    except Exception:
        win_ok = "no (exception)"

    ime_restore_attempted = "yes"
    ime_restore_succeeded = "no"
    ime_restore_reason = ""
    if prev_hkl and prev_fg and win32gui.IsWindow(prev_fg):
        try:
            win32gui.SetForegroundWindow(prev_fg)
            time.sleep(0.1)
            ctypes.windll.user32.PostMessageW(prev_fg, 0x0050, 0, prev_hkl)
            time.sleep(0.3)
            tid = win32process.GetWindowThreadProcessId(prev_fg)[0]
            if win32api.GetKeyboardLayout(tid) == prev_hkl:
                ime_restore_succeeded = "yes"
            else:
                ime_restore_reason = "PostMessage failed"
        except Exception as e:
            ime_restore_reason = str(e)
    else:
        ime_restore_reason = "no HKL or window gone"

    result = {
        "case": case_name, "status": "completed" if observed and not ahk_err else "failed",
        "input": input_text, "extra_keys": extra_keys,
        "shift_toggled": shift_toggled, "observed_output": observed,
        "clipboard_restored": cb_ok, "window_restored": win_ok,
        "ime_restore_attempted": ime_restore_attempted,
        "ime_restore_succeeded": ime_restore_succeeded,
        "ime_restore_reason": ime_restore_reason,
        "ime_detection_method": ime_method, "error": ahk_err,
    }
    print(json.dumps(result, ensure_ascii=False))
    success = (result["status"] == "completed"
               and result["ime_restore_succeeded"] == "yes"
               and result["clipboard_restored"] == "yes"
               and result["window_restored"] == "yes")
    sys.exit(0 if success else 1)

# ──────────────────────────────────────────────────────────────
# Phase: type (resume after template matching)
# ──────────────────────────────────────────────────────────────

if args.phase == "type":
    import win32gui, win32con, win32clipboard, win32process, win32api, ctypes

    if not args.vision_mode:
        print(json.dumps({"status": "blocked", "error": "--vision-result or --vision-mode required for --phase=type"}, ensure_ascii=False))
        sys.exit(1)

    # ── load state ──
    with open(args.state_file, "r", encoding="utf-8") as f:
        state = json.load(f)

    hwnd_notepad = state["hwnd_notepad"]
    prev_fg = state["prev_fg"]
    prev_max = state["prev_max"]
    prev_cb = state["prev_cb"]
    prev_hkl = state["prev_hkl"]
    case_name = state["case_name"]
    input_text = state["input_text"]
    extra_keys = state["extra_keys"]

    # ── verify Notepad exists ──
    if not win32gui.IsWindow(hwnd_notepad):
        print(json.dumps({"status": "failed", "error": f"notepad window {hwnd_notepad} no longer exists"}, ensure_ascii=False))
        sys.exit(1)

    ensure_notepad_foreground(hwnd_notepad, "phase=type start")
    time.sleep(0.5)

    vision_mode = args.vision_mode

    if vision_mode == "english":
        _log("vision says english, toggling Shift")
        ensure_notepad_foreground(hwnd_notepad, "before toggle")
        time.sleep(0.3)

        # ── toggle via AHK ──
        subprocess.run([AHK_EXE, AHK_TOGGLE], capture_output=True, timeout=30, creationflags=0x08000000)
        time.sleep(2)

        # ── re-screenshot ──
        ensure_notepad_foreground(hwnd_notepad, "after toggle, before re-screenshot")
        time.sleep(0.5)
        new_screenshot = os.path.join(PROBE_DIR, f"rimekit_probe_{case_name}_after_toggle_{int(time.time())}.png")
        call_take_screenshot(new_screenshot)

        # ── update state ──
        state["screenshot_path"] = new_screenshot
        state["phase"] = "awaiting_vision_after_toggle"
        state["shift_toggled"] = True
        with open(args.state_file, "w", encoding="utf-8") as f:
            json.dump(state, f, ensure_ascii=False)

        print(json.dumps({
            "status": "blocked",
            "phase": "awaiting_vision_after_toggle",
            "screenshot_path": new_screenshot,
            "state_file": args.state_file,
            "shift_toggled": True,
            "next_action": "vision_agent_required",
        }, ensure_ascii=False))
        sys.exit(0)

    elif vision_mode == "chinese":
        _log("vision says chinese, proceeding to type")
        ensure_notepad_foreground(hwnd_notepad, "before typing")
        time.sleep(0.3)

        result_file = os.path.join(PROBE_DIR, f"rimekit_probe_ahk_result_{case_name}.txt")
        try:
            os.remove(result_file)
        except OSError:
            pass

        # ── AHK typing ──
        ahk_cmd = [AHK_EXE, AHK_TYPE, input_text, extra_keys, result_file]
        _log(f"running AHK: {ahk_cmd}")
        p = subprocess.run(ahk_cmd, capture_output=True, timeout=45, creationflags=0x08000000)
        _log(f"AHK exit={p.returncode}")

        observed = ""
        ahk_error = ""
        if os.path.exists(result_file):
            content = open(result_file, encoding="utf-8-sig").read()
            _log(f"AHK raw output ({len(content)} chars): {repr(content[:120])}")
            if content.startswith("OK:"):
                observed = content[3:]
            else:
                ahk_error = content[:200]
        else:
            ahk_error = "result file not created"

        # ── close Notepad ──
        try:
            subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
        except Exception:
            pass
        try:
            os.remove(result_file)
        except OSError:
            pass

        # ── restore state ──
        cb_ok = "no"
        try:
            if prev_cb:
                win32clipboard.OpenClipboard()
                win32clipboard.EmptyClipboard()
                win32clipboard.SetClipboardText(prev_cb, win32con.CF_UNICODETEXT)
                win32clipboard.CloseClipboard()
            cb_ok = "yes"
        except Exception:
            cb_ok = "no (exception)"

        win_ok = "no"
        try:
            if prev_fg and win32gui.IsWindow(prev_fg):
                win32gui.SetForegroundWindow(prev_fg)
                if prev_max:
                    win32gui.ShowWindow(prev_fg, win32con.SW_MAXIMIZE)
                win_ok = "yes"
        except Exception:
            win_ok = "no (exception)"

        ime_restore_attempted = "yes"
        ime_restore_succeeded = "no"
        ime_restore_reason = ""
        if prev_hkl and prev_fg and win32gui.IsWindow(prev_fg):
            try:
                win32gui.SetForegroundWindow(prev_fg)
                time.sleep(0.1)
                ctypes.windll.user32.PostMessageW(prev_fg, 0x0050, 0, prev_hkl)
                time.sleep(0.3)
                tid = win32process.GetWindowThreadProcessId(prev_fg)[0]
                if win32api.GetKeyboardLayout(tid) == prev_hkl:
                    ime_restore_succeeded = "yes"
                else:
                    ime_restore_succeeded = "no"
                    ime_restore_reason = "PostMessage failed"
            except Exception as e:
                ime_restore_succeeded = "no"
                ime_restore_reason = str(e)
        else:
            ime_restore_reason = "no HKL or window gone"

        # ── final result ──
        result = {
            "case": case_name,
            "status": "completed" if observed and not ahk_error else "failed",
            "input": input_text,
            "extra_keys": extra_keys,
            "shift_toggled": state.get("shift_toggled", False),
            "observed_output": observed,
            "clipboard_restored": cb_ok,
            "window_restored": win_ok,
            "ime_restore_attempted": ime_restore_attempted,
            "ime_restore_succeeded": ime_restore_succeeded,
            "ime_restore_reason": ime_restore_reason,
            "ime_detection_method": "template_matching",
            "error": ahk_error,
        }
        try:
            os.remove(args.state_file)
        except OSError:
            pass

        print(json.dumps(result, ensure_ascii=False))

        success = (result["status"] == "completed"
                   and result["ime_restore_succeeded"] == "yes"
                   and result["clipboard_restored"] == "yes"
                   and result["window_restored"] == "yes")
        sys.exit(0 if success else 1)

    else:
        print(json.dumps({"status": "blocked", "error": f"unexpected vision_mode: {vision_mode}"}, ensure_ascii=False))
        sys.exit(1)


# ──────────────────────────────────────────────────────────────
# Legacy single-phase mode (no --phase)
#   Opens Notepad, detects IME mode via template matching, types, captures output.
#   Uses Activator ime_open only as reference; still toggles Shift
#   unconditionally for safety, since ime_open is unreliable.
# ──────────────────────────────────────────────────────────────

if len(sys.argv) < 3:
    print(json.dumps({"status": "failed", "error": "usage: probe_notepad_ime.py <case_name> <input_text> [keystrokes]  OR  probe_notepad_ime.py --phase=vision|type ..."}))
    sys.exit(1)

import win32gui, win32con, win32clipboard, win32process, win32api, ctypes
from pywinauto.application import Application

case_name = args.case_name
input_text = args.input_text
extra_keys = " ".join(args.extra_keys) if args.extra_keys else ""

result = {
    "case": case_name, "status": "pending", "input": input_text, "extra_keys": extra_keys,
    "shift_toggled": "no", "observed_output": "",
    "clipboard_restored": "no", "window_restored": "no",
    "ime_restore_attempted": "no", "ime_restore_succeeded": "no",
    "ime_restore_reason": "", "error": "",
    "ime_detection_method": "legacy_activator_fallback",
}

# ── pre-flight for legacy mode ──
legacy_errors = []
if not AHK_EXE:
    legacy_errors.append("AutoHotkey64.exe not found")
if not os.path.exists(AHK_TOGGLE):
    legacy_errors.append(f"AHK toggle missing: {AHK_TOGGLE}")
if not os.path.exists(AHK_TYPE):
    legacy_errors.append(f"AHK type missing: {AHK_TYPE}")
deployer = find_weasel_deployer()
ws_running = False
try:
    out = subprocess.run(["tasklist", "/fi", "IMAGENAME eq WeaselServer.exe"], capture_output=True, text=True)
    ws_running = "WeaselServer.exe" in out.stdout
except Exception:
    pass
if not deployer and not ws_running:
    legacy_errors.append("Weasel not detected")
if legacy_errors:
    result["status"] = "blocked"
    result["error"] = " | ".join(legacy_errors)
    print(json.dumps(result, ensure_ascii=False))
    sys.exit(1)

# ── backup ──
prev_fg = win32gui.GetForegroundWindow()
placement = win32gui.GetWindowPlacement(prev_fg) if prev_fg else None
prev_max = (placement[1] == win32con.SW_SHOWMAXIMIZED) if placement else False
prev_cb = ""
try:
    win32clipboard.OpenClipboard()
    prev_cb = win32clipboard.GetClipboardData(win32con.CF_UNICODETEXT)
    win32clipboard.CloseClipboard()
except Exception:
    try:
        win32clipboard.CloseClipboard()
    except Exception:
        pass
prev_hkl = get_hkl(prev_fg) if prev_fg else 0
win32clipboard.OpenClipboard()
win32clipboard.EmptyClipboard()
win32clipboard.CloseClipboard()

# ── open Notepad ──
subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
time.sleep(0.5)
app = Application(backend="win32").start("notepad.exe")
time.sleep(3)
w = app.window(class_name="Notepad")
w.wait("visible", 10)
hwnd_notepad = w.handle
ensure_notepad_foreground(hwnd_notepad, "legacy notepad open")
time.sleep(2)

# ── activate Weasel ──
if os.path.exists(ACTIVATOR_PATH):
    subprocess.run([ACTIVATOR_PATH], capture_output=True, timeout=15, creationflags=0x08000000)
time.sleep(3)

# ── detect IME mode via template matching, toggle if english ──
ensure_notepad_foreground(hwnd_notepad, "legacy before detect")
time.sleep(0.3)
tpl_result = detect_ime_via_template()
if tpl_result and tpl_result.get("mode") == "english":
    subprocess.run([AHK_EXE, AHK_TOGGLE], capture_output=True, timeout=30, creationflags=0x08000000)
    time.sleep(2)
    result["shift_toggled"] = "yes"
    _log("legacy: detected english → toggled")
elif tpl_result and tpl_result.get("mode") == "chinese":
    result["shift_toggled"] = "no"
    _log("legacy: detected chinese → no toggle needed")
else:
    # template matching failed — stop, do NOT proceed
    result["status"] = "blocked"
    result["error"] = "template matching failed — cannot determine IME mode. Check ref images in %TEMP%\\ime_refs\\"
    subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
    print(json.dumps(result, ensure_ascii=False))
    sys.exit(1)

# ── AHK typing ──
ensure_notepad_foreground(hwnd_notepad, "legacy before typing")
time.sleep(0.3)
ahk_result_file = os.path.join(PROBE_DIR, f"rimekit_legacy_ahk_{case_name}.txt")
try:
    os.remove(ahk_result_file)
except OSError:
    pass

p = subprocess.Popen([AHK_EXE, AHK_TYPE, input_text, extra_keys, ahk_result_file],
                     creationflags=0x08000000)
ctypes.windll.user32.AllowSetForegroundWindow(p.pid)
time.sleep(0.5)
p.wait(timeout=45)

if os.path.exists(ahk_result_file):
    content = open(ahk_result_file, encoding="utf-8-sig").read()
    if content.startswith("OK:"):
        result["observed_output"] = content[3:]
        result["status"] = "completed"
    else:
        result["status"] = "failed"
        result["error"] += " | AHK: " + content[:200]
else:
    result["status"] = "failed"
    result["error"] += " | AHK result file missing"

# ── close Notepad ──
subprocess.run(["taskkill", "/f", "/im", "notepad.exe"], capture_output=True)
try:
    os.remove(ahk_result_file)
except OSError:
    pass

# ── restore ──
try:
    if prev_cb:
        win32clipboard.OpenClipboard()
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardText(prev_cb, win32con.CF_UNICODETEXT)
        win32clipboard.CloseClipboard()
    result["clipboard_restored"] = "yes"
except Exception:
    result["clipboard_restored"] = "no (exception)"

try:
    if prev_fg and win32gui.IsWindow(prev_fg):
        win32gui.SetForegroundWindow(prev_fg)
        if prev_max:
            win32gui.ShowWindow(prev_fg, win32con.SW_MAXIMIZE)
        result["window_restored"] = "yes"
    else:
        result["window_restored"] = "no"
except Exception:
    result["window_restored"] = "no (exception)"

result["ime_restore_attempted"] = "yes"
ime_restore_succeeded = "no"
ime_restore_reason = ""
if prev_hkl and prev_fg and win32gui.IsWindow(prev_fg):
    try:
        win32gui.SetForegroundWindow(prev_fg)
        time.sleep(0.1)
        ctypes.windll.user32.PostMessageW(prev_fg, 0x0050, 0, prev_hkl)
        time.sleep(0.3)
        tid = win32process.GetWindowThreadProcessId(prev_fg)[0]
        if win32api.GetKeyboardLayout(tid) == prev_hkl:
            ime_restore_succeeded = "yes"
        else:
            ime_restore_reason = "PostMessage failed"
    except Exception as e:
        ime_restore_reason = str(e)
else:
    ime_restore_reason = "no HKL or window gone"
result["ime_restore_succeeded"] = ime_restore_succeeded
result["ime_restore_reason"] = ime_restore_reason

print(json.dumps(result, ensure_ascii=False))
success = (result["status"] == "completed"
           and result["ime_restore_succeeded"] == "yes"
           and result["clipboard_restored"] == "yes"
           and result["window_restored"] == "yes")
sys.exit(0 if success else 1)
