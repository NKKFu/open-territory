"""Integration smoke test for the local OpenTerritory Unity MCP connection."""
import asyncio
import json
import time
from datetime import datetime, timezone
from pathlib import Path

from fastmcp import Client

ENDPOINT = "http://127.0.0.1:8087/mcp"
INSTANCE = "OpenTerritory@a13b5c63dbd7b54e"
REPORT = Path(__file__).resolve().parents[1] / "Unity/Reports/unity-mcp-smoke.json"


async def main():
    report = {"at": datetime.now(timezone.utc).isoformat(), "endpoint": ENDPOINT, "checks": []}
    async with Client(ENDPOINT, timeout=40) as client:
        async def call(name, arguments):
            result = await client.call_tool(name, arguments)
            data = result.structured_content
            if data is None:
                data = json.loads(next(c.text for c in result.content if hasattr(c, "text")))
            if isinstance(data, dict) and data.get("success") is False:
                raise RuntimeError(json.dumps(data))
            return data

        async def state():
            parts = await client.read_resource("mcpforunity://editor/state")
            result = json.loads(parts[0].text)
            if not result.get("success"):
                raise RuntimeError(json.dumps(result))
            return result["data"]

        async def wait_ready(playing=None, compile_after=None):
            deadline = time.monotonic() + 120
            last_error = None
            while time.monotonic() < deadline:
                try:
                    current = await state()
                    play = current["editor"]["play_mode"]
                    compiled = current["compilation"].get("last_compile_finished_unix_ms") or 0
                    # Coplay 10.3.0 maps is_changing to isPlayingOrWillChangePlaymode,
                    # which remains true throughout Play Mode. Confirm live calls too.
                    if (current["advice"]["ready_for_tools"]
                            and (playing is None or play["is_playing"] == playing)
                            and (compile_after is None or compiled > compile_after)):
                        return current
                except Exception as error:
                    last_error = str(error)
                await asyncio.sleep(1)
            raise TimeoutError(f"Editor did not become ready: {last_error}")

        await call("set_active_instance", {"instance": INSTANCE})
        tools = await client.list_tools()
        initial = await wait_ready(playing=False)
        scene = await call("manage_scene", {"action": "get_active"})
        if scene["data"]["isDirty"]:
            raise RuntimeError("Active scene has unsaved changes; preserving user work.")
        report["tools_count"] = len(tools)
        report["initial_state"] = initial
        report["scene"] = scene
        report["errors_before"] = await call("read_console", {"types": ["error"], "count": 20})
        started_play = False
        try:
            before = initial["compilation"].get("last_compile_finished_unix_ms") or 0
            start = time.monotonic()
            requested = await call("refresh_unity", {
                "mode": "force", "scope": "scripts", "compile": "request", "wait_for_ready": False
            })
            ready = await wait_ready(playing=False, compile_after=before)
            report["checks"].append({"name": "recompile_and_reconnect", "ok": True,
                "seconds": round(time.monotonic() - start, 2), "request": requested,
                "compilation": ready["compilation"]})
            for cycle in range(1, 4):
                start = time.monotonic()
                await call("manage_editor", {"action": "play"})
                started_play = True
                ready = await wait_ready(playing=True)
                await asyncio.sleep(3)
                hierarchy = await call("manage_scene", {"action": "get_hierarchy", "max_depth": 1, "page_size": 5})
                await call("manage_editor", {"action": "stop"})
                await wait_ready(playing=False)
                started_play = False
                report["checks"].append({"name": f"play_stop_cycle_{cycle}", "ok": True,
                    "seconds": round(time.monotonic() - start, 2),
                    "hierarchy_read_in_play": hierarchy.get("success", False)})
                print(json.dumps(report["checks"][-1]), flush=True)
            report["errors_after"] = await call("read_console", {"types": ["error"], "count": 20})
            report["final_state"] = await state()
            report["final_scene"] = await call("manage_scene", {"action": "get_active"})
            report["ok"] = True
        except Exception as error:
            report["ok"] = False
            report["error"] = str(error)
            raise
        finally:
            if started_play:
                await call("manage_editor", {"action": "stop"})
                await wait_ready(playing=False)
            REPORT.parent.mkdir(parents=True, exist_ok=True)
            REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            print(f"Report: {REPORT}", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
