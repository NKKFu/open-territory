# Unity MCP — Open Territory

CoplayDev MCP for Unity v10.3.0, pinned to commit `aa5fc638d623f56178d50329d9ad8c541a57fe66`, is installed through the Unity package manifest. Editor: Unity 6000.3.27f1. The local server uses Python 3.13 in a dedicated environment; it does not use Python 3.14 from the reported Windows log-growth incident.

## Daily use

Open `Unity/OpenTerritory` in Unity. `Assets/Editor/OpenTerritoryMcpBootstrap.cs` starts the local server hidden and connects the Editor on load/domain reload. The server binds only to `127.0.0.1:8087`, with MCP at `/mcp` and health at `/health`. Telemetry is disabled for both the Editor and Python server. Package auto-rewriting of client configurations is disabled.

`.codex/config.toml` configures `unityMCP` for this repository. This machine's user Codex configuration also points to the same endpoint so it is accessible from the existing chat. Restart/reconnect MCP in the client if its current tool catalog predates installation; shell CLI access works immediately.

Manual startup from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Start-UnityMcp.ps1
$env:PYTHONIOENCODING = 'utf-8'
& "$env:USERPROFILE\.local\share\unity-mcp-venv\Scripts\unity-mcp.exe" --port 8087 --format json instances
```

Target `OpenTerritory@a13b5c63dbd7b54e` on this machine. Enumerate instances again if the project moves. Prefer MCP over Computer Use; inspect scene dirty state before tests and stop Play Mode afterward.

## Recreate the local Python environment

Install `uv`, Git, and Python 3.13, then:

```powershell
uv venv --python 3.13 "$env:USERPROFILE\.local\share\unity-mcp-venv"
uv pip install --python "$env:USERPROFILE\.local\share\unity-mcp-venv\Scripts\python.exe" 'git+https://github.com/CoplayDev/unity-mcp.git@aa5fc638d623f56178d50329d9ad8c541a57fe66#subdirectory=Server'
```

The launcher uses the server's rotating internal logs under `%LOCALAPPDATA%\OpenTerritoryTools\Logs`; it does not redirect unbounded terminal output to a project log file.

## Validation

Run only when the Editor is idle and nobody else is editing/testing it:

```powershell
$env:PYTHONIOENCODING = 'utf-8'
& "$env:USERPROFILE\.local\share\unity-mcp-venv\Scripts\python.exe" Tools/Test-UnityMcp.py
```

This performs MCP discovery, scene inspection, a requested script compilation, reconnection, and three Play/Stop cycles with hierarchy reads during Play Mode. It preserves Console entries and writes `Unity/Reports/unity-mcp-smoke.json`. It does not validate gameplay controls, purchases, networking, or a player build.

Known upstream quirk observed during validation: in v10.3.0, `editor/state.editor.play_mode.is_changing` remains true while playing because it directly uses Unity's `isPlayingOrWillChangePlaymode`. Do not wait for that field to become false during Play Mode. Confirm `is_playing`, `ready_for_tools`, and a successful live scene query instead. The Windows CLI also needs UTF-8 output to print its status emoji.

Validation on 2026-11-07: 49 MCP tools discovered; compilation and ready-state recovery took 8.65 seconds; three Play/Stop cycles passed (11.41, 11.35 and 11.31 seconds, including a 3-second play interval and live hierarchy reads). No Console errors were returned before or after the successful run. Play Mode was stopped and `BaiaDoSol` remained clean. The first stricter readiness probe timed out while waiting for `is_changing` to clear and also observed a transient ping-not-answered response; its evidence is retained in `Unity/Reports/unity-mcp-initial-probe.json`. These are smoke-test results, not a long-duration stability guarantee.
