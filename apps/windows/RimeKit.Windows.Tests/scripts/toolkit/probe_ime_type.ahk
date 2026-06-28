; probe_ime_type.ahk — IME 中文输入探针（AutoHotkey v2）
; 负责：激活记事本 → 测试输入 → 上屏 → 复制 → 结果写文件
; 用法: AutoHotkey64.exe probe_ime_type.ahk <input_text> <extra_keys> <result_file_path>
; 注意：调用方（Python 编排层）负责 IME 激活和中英文模式切换。本脚本不检测也不切换 IME 模式。
; 注意：有意不使用 #Requires——某些环境下会导致静默挂起。

#SingleInstance Force

if A_Args.Length < 3 {
    f := FileOpen("ahk_type_usage_error.txt", "w", "UTF-8")
    f.Write("USAGE: probe_ime_type.ahk <input_text> <extra_keys> <result_file_path>")
    f.Close()
    ExitApp(1)
}

inputText := A_Args[1]
extraKeys := A_Args[2]
resultFile := A_Args[3]

SetKeyDelay(10, 0)
SetTitleMatchMode(2)
A_Clipboard := ""

try {
    ; ── reuse existing Notepad (opened by Python probe) to preserve IME state ──
    if !WinExist("ahk_class Notepad") {
        Run("notepad.exe")
        if !WinWait("ahk_class Notepad", , 10) {
            fh := FileOpen(resultFile, "w", "UTF-8")
            fh.Write("ERROR: notepad did not start within 10s")
            fh.Close()
            ExitApp(1)
        }
    }
    WinActivate("ahk_class Notepad")
    if !WinWaitActive("ahk_class Notepad", , 5)
    {
        Sleep(2000)
        WinActivate("ahk_class Notepad")
        WinWaitActive("ahk_class Notepad", , 5)
    }
    Sleep(1500)

    ; ── clear leftover from previous probe ──
    Send("{Escape}")
    Sleep(400)
    Send("^a")
    Sleep(200)
    Send("{Delete}")
    Sleep(400)

    ; ── test input: SendEvent mode, one char at a time ──
    SendMode("Event")
    SetKeyDelay(30, 30)
    Loop Parse, inputText
    {
        Send(A_LoopField)
        Sleep(350)
    }
    Sleep(3000)

    ; ── commit ──
    if extraKeys != "" {
        for _, key in StrSplit(extraKeys, " ") {
            key := Trim(key)
            if key = "{SPACE}" {
                Send("{Space}")
            } else if key = "{TAB}" {
                Send("{Tab}")
            } else if key = "{ENTER}" {
                Send("{Enter}")
            } else {
                Send(key)
            }
            Sleep(300)
        }
    }
    Sleep(3000)

    ; ── copy ──
    Send("^a")
    Sleep(500)
    Send("^c")
    Sleep(500)
    ClipWait(3, 1)

    ; ── write result ──
    fh := FileOpen(resultFile, "w", "UTF-8")
    fh.Write("OK:" . A_Clipboard)
    fh.Close()

} catch as e {
    fh := FileOpen(resultFile, "w", "UTF-8")
    fh.Write("ERROR:" . e.Message)
    fh.Close()
}

ExitApp(0)
