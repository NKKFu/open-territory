using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

/// <summary>Connect this project's Editor to the local Coplay server without UI automation.</summary>
[InitializeOnLoad]
internal static class OpenTerritoryMcpBootstrap
{
    static OpenTerritoryMcpBootstrap()
    {
        EditorApplication.delayCall += Connect;
    }

    private static async void Connect()
    {
        if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += Connect;
            return;
        }

        try
        {
            // This server is started by the project launcher, not the package's terminal launcher.
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
            EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8087");
            EditorPrefs.SetBool("MCPForUnity.TelemetryDisabled", true);
            EditorPrefs.SetBool("MCPForUnity.AutoRegisterEnabled", false);
            EditorConfigurationCache.Instance.Refresh();
            Application.runInBackground = true;

            if (MCPServiceLocator.Bridge.IsRunning) return;
            string launcher = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Tools/Start-UnityMcp.ps1"));
            var process = Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + launcher + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process != null)
            {
                await System.Threading.Tasks.Task.Run(() => process.WaitForExit(15000));
                process.Dispose();
            }
            // Allow the local Python service time to bind before connecting.
            await System.Threading.Tasks.Task.Delay(2000);
            bool connected = await MCPServiceLocator.Bridge.StartAsync();
            UnityEngine.Debug.Log("[Open Territory MCP] Local bridge connected: " + connected);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("[Open Territory MCP] Connection failed: " + exception.Message);
        }
    }
}
