"""Pairing window and entry point of the AI Manager Bridge (imported by aim_bridge.py's main())."""
import os
import sys
import threading

CLI_COMMANDS = {"ask", "approve", "report", "status", "register", "next", "worker", "hook", "mcp",
                "connect-claude", "disconnect-claude"}


def hide_own_console():
    """Double-clicked exe: hide the black console window; it exists only for the CLI commands."""
    if os.name != "nt" or not getattr(sys, "frozen", False):
        return
    try:
        import ctypes
        k = ctypes.windll.kernel32
        procs = (ctypes.c_uint * 4)()
        if k.GetConsoleProcessList(procs, 4) <= 2:
            hwnd = k.GetConsoleWindow()
            if hwnd:
                ctypes.windll.user32.ShowWindow(hwnd, 0)
    except Exception:
        pass


def run_gui(bridge, port):
    import tkinter as tk
    import webbrowser
    import claude_setup

    BG, CARD, TXT, MUTED, TEAL, AMBER = "#0e0f12", "#17191e", "#eef0f3", "#9aa3b2", "#4fd1c5", "#ffb347"
    root = tk.Tk()
    root.title("AI Manager Bridge")
    root.configure(bg=BG)
    root.geometry("480x620")
    root.minsize(440, 580)

    def label(text="", size=11, color=TXT, bold=False, pady=(0, 0)):
        lab = tk.Label(root, text=text, bg=BG, fg=color, font=("Segoe UI", size, "bold" if bold else "normal"),
                       wraplength=420, justify="center")
        lab.pack(pady=pady)
        return lab

    def button(text, cmd, primary=False):
        b = tk.Button(root, text=text, command=cmd, bd=0, relief="flat", cursor="hand2", padx=16, pady=9,
                      bg=("#13675f" if primary else "#2a2f37"), fg=TXT, activebackground="#1b8a80",
                      activeforeground=TXT, font=("Segoe UI", 11, "bold"))
        b.pack(pady=5, ipadx=10)
        return b

    label("AI MANAGER BRIDGE", 13, TEAL, True, (22, 2))
    label("Connect your headset and your real AI agents", 10, MUTED, False, (0, 14))
    box = tk.Frame(root, bg=CARD, padx=26, pady=14)
    box.pack(pady=4)
    tk.Label(box, text="PAIRING CODE", bg=CARD, fg=MUTED, font=("Segoe UI", 9, "bold")).pack()
    code_var = tk.StringVar()
    tk.Label(box, textvariable=code_var, bg=CARD, fg=TXT, font=("Consolas", 34, "bold")).pack()
    label("On your headset:  Settings > Agent Bridge > Find my PC,\nthen type this code. Both must be on the same Wi-Fi.",
          10, MUTED, False, (12, 8))
    status = label("", 11, TXT, True, (6, 0))
    agents = label("", 10, MUTED, False, (0, 10))

    claude_btn = button("", lambda: None, True)
    note = label("", 9, MUTED, False, (0, 4))

    def refresh_claude():
        claude_btn.config(text="Claude Code: connected  (click to disconnect)" if claude_setup.is_connected()
                          else "Connect Claude Code")

    def toggle_claude():
        try:
            if claude_setup.is_connected():
                claude_setup.disconnect()
                note.config(text="Claude Code disconnected.", fg=TEAL)
            else:
                claude_setup.connect()
                note.config(text="Done. Restart open Claude Code sessions to apply.", fg=TEAL)
        except Exception as e:
            note.config(text=f"Could not change Claude Code settings: {e}", fg=AMBER)
        refresh_claude()

    claude_btn.config(command=toggle_claude)
    button("Open dashboard in browser", lambda: webbrowser.open(f"http://127.0.0.1:{port}"))

    def unpair():
        bridge.reset_pairing()
        note.config(text="New code created. Every headset must pair again.", fg=TEAL)

    button("New code (unpair all headsets)", unpair)
    label("Keep this window open (you can minimise it). If Windows asks about the firewall, choose Allow.",
          9, MUTED, False, (14, 0))

    def tick():
        c = bridge.CODE
        code_var.set(f"{c[:3]} {c[3:]}")
        s = bridge.STATE.summary()
        status.config(text="Headset connected" if s["headsetConnected"] else "Waiting for your headset...",
                      fg=TEAL if s["headsetConnected"] else AMBER)
        n, p = len(s["agents"]), len(s["pending"])
        agents.config(text=f"{n} real agent(s) connected" + (f"  -  {p} waiting for you" if p else ""))
        root.after(1000, tick)

    refresh_claude()
    tick()
    root.mainloop()


def main(bridge):
    import argparse
    argv = sys.argv[1:]
    if argv and (argv[0] in CLI_COMMANDS or argv[0] in ("--agent", "--role")):
        if argv[0] in ("connect-claude", "disconnect-claude"):
            import claude_setup
            on = argv[0] == "connect-claude"
            (claude_setup.connect if on else claude_setup.disconnect)()
            print("Claude Code connected. Restart open sessions." if on else "Claude Code disconnected.")
            return 0
        import aim
        sys.argv = ["aim"] + argv
        return aim.main()

    ap = argparse.ArgumentParser(description="AI Manager Bridge")
    ap.add_argument("--port", type=int, default=int(os.environ.get("AIM_PORT", 8787)))
    ap.add_argument("--local-only", action="store_true", help="this PC only (headset over USB with adb reverse)")
    ap.add_argument("--lan", action="store_true", help=argparse.SUPPRESS)       # old flag: LAN is the default now
    ap.add_argument("--no-gui", action="store_true", help="console only, no pairing window")
    args = ap.parse_args()

    try:
        bridge.start_server(args.port, not args.local_only)
    except OSError:
        print(f"Port {args.port} is already in use. Is the bridge already running?")
        if not args.no_gui:
            try:
                import tkinter
                from tkinter import messagebox
                r = tkinter.Tk()
                r.withdraw()
                messagebox.showinfo("AI Manager Bridge", "The bridge is already running.")
            except Exception:
                pass
        return 1

    print(f"AI Manager Bridge {bridge.VERSION}  dashboard: http://127.0.0.1:{args.port}")
    if args.local_only:
        print("Local only. Quest over USB:  adb reverse tcp:%d tcp:%d" % (args.port, args.port))
    else:
        c = bridge.CODE
        print(f"Pairing code: {c[:3]} {c[3:]}   (headset: Settings > Agent Bridge > Find my PC)")
    if not args.no_gui:
        try:
            hide_own_console()
            run_gui(bridge, args.port)
            return 0
        except Exception as e:                      # no tkinter / no display: fall back to the console
            print(f"(window unavailable: {e}; running in the console)")
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        pass
    return 0
