using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace OpenTerritory.Editor
{
    // Local opt-in commands, never present in the player or active without a command file.
    [InitializeOnLoad]
    public static class CityUIValidation
    {
        const string Command="Library/city-ui-validation.command", Status="Library/city-ui-validation.status";
        static CityUIValidation()
        {
            if(AssetDatabase.IsAssetImportWorkerProcess())return;
            File.WriteAllText(Status,"Compiled "+DateTime.UtcNow.ToString("O"));
            EditorApplication.update+=Poll;
        }
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Command))return;
            string command=File.ReadAllText(Command).Trim();File.Delete(Command);
            try
            {
                if(command=="stop")EditorApplication.isPlaying=false;
                if(command=="play")EditorApplication.isPlaying=true;
                if(command=="build"||command=="import-build")
                {
                    if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before building.");
                    if(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Preserve unsaved scene changes before rebuilding.");
                    File.WriteAllText(Status,"Building "+DateTime.UtcNow.ToString("O"));
                    if(command=="import-build")CityBuilder.ImportAndBuild();else CityBuilder.Build();
                }
                File.WriteAllText(Status,"Done "+command+" playing="+EditorApplication.isPlaying+" "+DateTime.UtcNow.ToString("O"));
            }
            catch(Exception e){File.WriteAllText(Status,"Failed: "+e);Debug.LogException(e);}
        }
    }
}
