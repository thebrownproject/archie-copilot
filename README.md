# Archie Copilot

**Talk to Revit. It listens.**

A dockable chat panel for Autodesk Revit 2025 that turns natural language into executable IronPython code. Ask it to create walls, floors, roofs. It generates the code, runs it, and if something breaks, it fixes itself automatically.

---

## How it works

Type a command in plain English. Claude generates IronPython code targeting the Revit 2025 API. Click **Run in Revit** and the code executes inside Revit's API thread via the ExternalEvent pattern. If execution fails, the error is automatically sent back to Claude, which generates a fix and re-executes. Up to 3 retries, no manual intervention.

The whole conversation is preserved, so Claude learns from errors mid-session and gets progressively better at understanding your project.

---

## Demo

https://github.com/user-attachments/assets/cca7e666-d4f5-48de-8fca-dce514af1668

Built a house from scratch using only natural language prompts:

1. *"Make a 12m x 8m rectangular floor on Level 0"*
2. *"Put walls along all four edges, going up to Level 1"*
3. *"Add an internal wall to split the living area from the bedroom"*
4. *"Put a door in that internal wall"*
5. *"Add 3 windows along the south wall, 900mm sill height"*
6. *"Throw a pitched roof on top"*

---

## Tech Stack

**Platform:** WPF · .NET 8 · Revit 2025 API <br>
**AI:** Claude Sonnet 4 (direct HTTP, no SDK) <br>
**Execution:** IronPython 3.4 · ExternalEvent pattern <br>
**Serialisation:** Newtonsoft.Json

---

## Architecture

The add-in registers as an `IExternalApplication` on Revit startup, creating a dockable WPF panel and a ribbon tab.

Revit's API is single-threaded and only accessible from its own thread. The chat panel runs on the WPF UI thread, so code execution goes through an `IExternalEventHandler`: the panel queues code, calls `ExternalEvent.Raise()`, and Revit calls `Execute()` when ready. Results dispatch back to WPF via `Dispatcher`.

Claude receives a system prompt with Revit 2025 API specifics like deprecated methods (`NewFloor`, `NewSlab`), IronPython 3.4 limitations (no f-strings, no match/case), and the pre-defined `doc`/`uidoc`/`uiapp` scope variables.

---

## Setup

**Build:**

```bash
dotnet build ArchieCopilot.csproj
```

**Deploy:**

1. Copy `ArchieCopilot.addin` to `%AppData%\Autodesk\Revit\Addins\2025\`
2. Update the `<Assembly>` path in the .addin file to point to your built DLL
3. Set your API key. Either set the `ARCHIE_COPILOT_API_KEY` env var, or paste it directly into the chat panel on first launch

**Requires:** Revit 2025, .NET 8 SDK, Anthropic API key

---

## Why this project

This demonstrates building a production-grade plugin for domain-specific professional software:

- Integrating AI into an existing desktop application with strict threading constraints
- Working with the Revit API's transaction model and element creation patterns
- Building a self-correcting feedback loop (error → AI fix → re-execute)
- WPF UI development with custom templates, animations, and data binding
- Bridging C#, IronPython, and a REST API in a single coherent workflow
