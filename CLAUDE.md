# ArchieCopilot

AI-powered Revit 2025 add-in that lets users type natural language commands and generates + executes IronPython code via the Revit API. Built as a dockable chat panel with a warm amber/copper themed UI.

## Project Structure

```
ArchieCopilot/
├── ArchieCopilot.csproj          # .NET 8 WPF, IronPython, Newtonsoft.Json
├── ArchieCopilot.addin           # Revit add-in manifest (source copy)
├── App.cs                        # IExternalApplication entry point, ribbon tab, dockable pane registration
├── ChatPanel.xaml                # WPF dark-themed chat UI (dockable panel, amber/copper accent)
├── ChatPanel.xaml.cs             # Chat logic, message handling, auto-retry feedback loop
├── RevitCommandHandler.cs        # IExternalEventHandler — runs IronPython on Revit's thread
├── ClaudeService.cs              # Claude API via HttpClient with conversation history
├── Config.cs                     # API key from env var or config.json
```

## Architecture

- **Entry point**: `App.cs` implements `IExternalApplication` (not `IExternalCommand`)
- **UI**: WPF `Page` implementing `IDockablePaneProvider`, docked right in Revit
- **AI**: Direct HTTP calls to `https://api.anthropic.com/v1/messages` using `claude-sonnet-4-20250514` (no SDK — just HttpClient + Newtonsoft.Json)
- **Execution**: ExternalEvent pattern — chat panel sets code on `RevitCommandHandler`, calls `ExternalEvent.Raise()`, Revit calls `Execute()` on its API thread
- **IronPython**: Scripts get `doc`, `uidoc`, `uiapp` variables pre-set; print() output is captured
- **Auto-retry**: When code execution fails, the error is automatically sent back to Claude, which generates a fix and re-executes (up to 3 retries)
- **Conversation history**: Claude remembers the last 20 messages including execution results, so it can learn from errors mid-session

## UI Features

- Dark theme with amber/copper accent (#D4944C) — deliberately not blue to stand out
- Welcome screen with suggestion chips (List all walls, Create a floor, Count elements, Place a wall)
- Assistant messages have an "A" avatar dot
- Code blocks have a "Copy" button and a gradient "Run in Revit" button
- Result boxes show checkmark/cross with Output/Error labels
- Auto-expanding input box (Shift+Enter for newlines)
- Clear chat button in header
- Placeholder text in input field
- Animated loading dots while waiting for Claude
- Timestamps on messages

## Build & Deploy

```powershell
# Build (must close Revit first — it locks the DLL)
cd C:\Users\frase\source\repos\ArchieCopilot
dotnet build ArchieCopilot/ArchieCopilot.csproj

# .addin manifest is deployed to:
# %AppData%\Autodesk\Revit\Addins\2025\ArchieCopilot.addin
# Points to: C:\Users\frase\source\repos\ArchieCopilot\ArchieCopilot\bin\Debug\net8.0-windows\ArchieCopilot.dll
```

## API Key

Stored in `bin/Debug/net8.0-windows/config.json` (NOT tracked by git). Can also be set via `ARCHIE_COPILOT_API_KEY` environment variable. Users can paste their `sk-ant-...` key directly into the chat panel on first use.

## Key Dependencies

- **RevitAPI.dll / RevitAPIUI.dll**: Referenced from `C:\Program Files\Autodesk\Revit 2025\` (Private=False)
- **IronPython 3.4.2**: Script execution engine
- **Newtonsoft.Json 13.0.3**: JSON serialization for Claude API and config

## Revit API Gotchas (enforced in system prompt)

- `doc.NewFloor()` / `doc.NewSlab()` are REMOVED in 2025 — use `Floor.Create(doc, profile, floorTypeId, levelId)`
- `__revit__` is pyRevit-only — NOT available in our IronPython engine. Use pre-defined `doc`, `uidoc`, `uiapp`
- `doc.Create.NewFootPrintRoof()` needs `clr.StrongBox[ModelCurveArray](ModelCurveArray())` for the ref parameter in IronPython
- IronPython 3.4 does NOT support f-strings or match/case
- Revit internal units are feet — mm values must be divided by 304.8
- Typed .NET lists need `List[CurveLoop]()` from `System.Collections.Generic`

## Important Notes

- Revit dockable panes require WPF (not WinForms)
- Never call Revit API from the WPF UI thread — always use the ExternalEvent pattern
- The DLL gets locked by Revit while running — close Revit before rebuilding
- `config.json` contains the API key — never commit it to git
