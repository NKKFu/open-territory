$ErrorActionPreference = 'Stop'
$endpoint = 'http://127.0.0.1:8087/health'
try {
    $health = Invoke-RestMethod -Uri $endpoint -TimeoutSec 2
    if ($health.status -eq 'healthy') { return }
} catch { }

$server = Join-Path $env:USERPROFILE '.local\share\unity-mcp-venv\Scripts\mcp-for-unity.exe'
if (-not (Test-Path -LiteralPath $server)) {
    throw "MCP server missing: $server. See Tools/UnityMcp.md."
}
$env:UNITY_MCP_DISABLE_TELEMETRY = '1'
$env:UNITY_MCP_LOG_DIR = Join-Path $env:LOCALAPPDATA 'OpenTerritoryTools\Logs'
Start-Process -FilePath $server -ArgumentList @('--transport', 'http', '--http-host', '127.0.0.1', '--http-port', '8087', '--project-scoped-tools') -WindowStyle Hidden
