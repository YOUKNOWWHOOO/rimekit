; probe_ime_toggle.ahk — 切换 IME 中英文模式（AutoHotkey v2）
; 仅发送 Shift 键一次，然后退出。
; 调用方负责确保目标窗口在前台。
; 注意：有意不使用 #Requires——某些环境下会导致静默挂起。

#SingleInstance Force
SetKeyDelay(30, 30)
Sleep(500)
Send("{Shift}")
Sleep(500)
ExitApp(0)
