"""Capture one full-screen PNG + resize for Kimi test."""
import ctypes, pathlib, subprocess, sys, time
sys.stdout.reconfigure(encoding="utf-8")

AHK = pathlib.Path.home() / "AppData/Local/Programs/AutoHotkey/v2/AutoHotkey64.exe"
ACT = pathlib.Path("apps/windows/RimeKit.Windows.Activator/bin/Debug/net10.0-windows/RimeKit.Windows.Activator.exe")
OUT = pathlib.Path.home() / "AppData/Local/Temp/rimekit_visual_poc"
OUT.mkdir(parents=True, exist_ok=True)
from PIL import Image, ImageGrab

subprocess.run(["taskkill","/f","/im","notepad.exe"],capture_output=True)
time.sleep(1)
subprocess.Popen(["notepad.exe"]); time.sleep(3)
ctypes.windll.user32.SystemParametersInfoW(0x2001,0,0,0)
subprocess.run([str(ACT)],capture_output=True); time.sleep(3)

s = OUT / "type.ahk"
s.write_text("#SingleInstance Force\nSetKeyDelay(30,30)\nSend('nihao')\nSleep(2500)",encoding="utf-8")
subprocess.run([str(AHK),str(s)],capture_output=True,timeout=30)

img = ImageGrab.grab(all_screens=True)
w, h = img.size
img = img.resize((960, int(h*960/w)), Image.LANCZOS)
p = OUT / "nihao_png.png"
img.save(str(p), "PNG")
print(f"{p} ({p.stat().st_size//1024}KB) {img.size}", file=sys.stderr)
