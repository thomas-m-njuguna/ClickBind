# ClickBind

ClickBind is a small Windows desktop app that turns mouse-button gestures into everyday shortcuts — copy, paste, cut, undo, select all, screen snips and more — anywhere in Windows. It lives in the system tray, starts with Windows, and works in every application.

**Author:** Thomas Mulwa

---

## Features

- **Global mouse gestures.** Bind double clicks, button chords, the middle wheel click, thumb buttons 4 and 5, and press-and-hold gestures to Windows actions.
- **Editable bindings.** Rebind any trigger, toggle each shortcut on or off, or delete a shortcut from its card.
- **Built-in gesture pad.** Test the mouse buttons inside the app and see exactly which button, chord or hold was detected.
- **Clipboard history.** The Clipboard page lists everything ClickBind copied or cut this session, with text previews, images and timestamps.
- **Copy confirmation.** A toast (or tray balloon when the window is hidden) confirms when something was copied.
- **Hot corners.** Moving the pointer into the top-left or top-right corner of the primary screen opens Task View. It is a simple on/off preference, not a binding.
- **Tray-first.** Runs quietly in the system tray, toggles from its taskbar/tray icon like any normal app, and can start minimized at sign-in.
- **Tunable timing.** Adjust the double-click window (120–600 ms) and the both-buttons chord window (30–200 ms).
- **Light, dark and system themes.** The Theme page under Clipboard switches the interface palette and remembers the choice.
- **Screenshot anywhere.** The screen-snapshot shortcut works across the whole desktop, including the ClickBind window, except over the gesture pad where clicks are only detected.

### Default shortcuts

| Gesture | Action |
| --- | --- |
| Double right click | Copy selection (`Ctrl + C`) |
| Double left click | Paste from clipboard (`Ctrl + V`) |
| Left + right click together | Screen snapshot (`Win + Shift + S`) |
| Pointer in the top-left or top-right corner | Task View (`Win + Tab`) |

Every row also has its own on/off switch, and the whole engine can be paused from the sidebar or the tray menu.

### Supported triggers

- Double left click, double right click, left + right together, middle wheel click.
- **Hold left** and **hold right** — press and keep the button still for about 0.5 s.
- **Hold left + right together** — hold both buttons still for about 0.5 s.
- Thumb buttons **4** (rear, normally *Back* in browsers) and **5** (front, normally *Forward*).

While **hold right** is bound, single right clicks wait for the hold window so a hold can be recognised; leave that trigger unbound if you want instant context menus. Hold gestures are only captured while they are bound.

### Supported actions

Copy, Paste, Cut, Undo, Redo, Select all, Windows screen snapshot (`Win + Shift + S`), or no action.

The snapshot action uses the normal Windows snipping overlay, so you still get the standard region / window / full-screen choices.

---

## Requirements

| | |
| --- | --- |
| OS | Windows 10 or Windows 11 (64-bit) |
| Runtime | .NET Framework 4.x — included with Windows 10/11 |
| Browser engine | Microsoft Edge WebView2 Runtime — included with current Windows builds |
| Build tools (source only) | The C# compiler bundled with .NET Framework (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) |

If the app opens a blank window or exits immediately, install the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Evergreen Bootstrapper) and start ClickBind again.

---

## Installation

### Option 1 — Run the prebuilt app

1. Download or copy the `dist` folder somewhere permanent, for example `C:\Program Files\ClickBind` or `%LOCALAPPDATA%\ClickBind`.
2. Keep the folder intact: `ClickBind.exe` needs `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`, `ClickBind.exe.config` and the `ui` folder beside it.
3. Double-click `ClickBind.exe`. It starts hidden in the system tray.
4. Open the window from the tray icon (or run `ClickBind.exe --show` for a visible first launch).
5. Optional: create a desktop shortcut by right-clicking `ClickBind.exe` → **Send to** → **Desktop (create shortcut)**.

The executable is not code-signed, so Windows SmartScreen may show *"Windows protected your PC"* the first time. Choose **More info** → **Run anyway**.

### Option 2 — Build from source

1. Install [Git for Windows](https://git-scm.com/download/win) and clone the repository:

   ```bat
   git clone https://github.com/thomas-m-njuguna/ClickBind.git
   cd clickbind
   ```

2. Double-click `build.cmd` (or run it from a terminal). It compiles every file in `src`, copies the WebView2 assemblies from `lib`, copies the `ui` folder and writes `ClickBind.exe.config` into `dist`.
3. Run `dist\ClickBind.exe` (add `--show` to open the window immediately).

No Visual Studio installation is required — `build.cmd` uses the C# compiler that ships with .NET Framework.

---

## First run

1. ClickBind appears in the system tray. Click the tray icon to show the window.
2. Check the banner reads **Engine active across all Windows apps**.
3. Use **Add shortcut** to bind a trigger to an action, **Change shortcut** to edit one, the row switch to enable/disable it and the red trash button to delete it.
4. Use **Test gesture** and click the gesture pad to confirm your mouse buttons are detected.
5. Enable **Launch ClickBind at Windows startup** on the *Timing & sensitivity* page so gestures are available after every sign-in.

Settings are saved to `%APPDATA%\ClickBind\settings.txt` and survive restarts and updates.

---

## Usage notes

- Clicks that belong to a gesture are swallowed, so context menus and double-click selections do not interfere.
- Gestures are deliberately ignored while the ClickBind window itself is in the foreground, so testing the pad never triggers your bindings.
- Disabled triggers pass straight through to the active application.
- The app must be running for gestures to work. Closing the window hides it to the tray instead of quitting; quit from the tray menu.
- The top-left and top-right hot corners open Task View. Turn them off under **Timing & sensitivity** if you use those corners for something else.
- Only the screen-snapshot gesture runs inside the ClickBind window; over the gesture test pad no gesture runs at all, so testing never triggers a shortcut.

---

## Project structure

```
ClickBind/
├─ build.cmd            Builds dist\ClickBind.exe with the .NET Framework compiler
├─ app.config           Runtime configuration copied to dist\ClickBind.exe.config
├─ lib/                 Microsoft.Web.WebView2 assemblies used at build and run time
├─ src/                 C# sources
│  ├─ Program.cs        Entry point and single-instance handling
│  ├─ MainForm.cs       Borderless window, WebView2 host and UI bridge
│  ├─ TrayApp.cs        Tray lifecycle, clipboard capture and notifications
│  ├─ MouseHook.cs      Low-level mouse hook and gesture recognition
│  ├─ Settings.cs       Triggers, actions, bindings and persistence
│  ├─ Actions.cs        Key chord injection (copy, paste, snapshot, ...)
│  ├─ Native.cs         Win32 interop
│  ├─ Startup.cs        Run-at-sign-in registration
│  └─ TrayIcon.cs / TrayMenu.cs
├─ ui/                  HTML/CSS UI rendered by WebView2
└─ dist/                Build output (generated by build.cmd)
```

## Build

```bat
build.cmd
```

The UI in `ui` is plain HTML with Tailwind generated at runtime by WebView2, so it can be edited without recompiling — update `dist\ui` or rebuild.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Blank or white window | Install the WebView2 Runtime (see Requirements). |
| Gestures do nothing | Make sure the engine is active and the shortcut's row switch is on. Some apps running as administrator ignore input sent from a non-elevated process; run ClickBind as administrator if needed. |
| Right-click menus feel slow | You have a *hold right* shortcut bound. Unbind it or disable that row. |
| App not in Task Manager startup | Toggle **Launch ClickBind at Windows startup** off and on again. |
| Window will not close | Closing hides to the tray by design. Right-click the tray icon and choose **Exit** to quit. |

---

## Author

**Thomas Mulwa**

Built as a lightweight, dependency-free alternative to heavyweight mouse-gesture suites: one small executable, one HTML UI, and no background services.

## License

Released for personal and commercial use. See the repository owner for redistribution terms.
