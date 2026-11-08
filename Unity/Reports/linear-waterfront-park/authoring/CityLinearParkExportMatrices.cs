using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityLinearParkExportMatrices
    {
        public static void Export()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
            var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            var byId=registry.nodes.Where(n=>n.transform).ToDictionary(n=>n.id,n=>n.transform);
            var idFor=registry.nodes.Where(n=>n.transform).ToDictionary(n=>n.transform,n=>n.id);
            int[] roots={46367,46373,46271,46277,46127,46133,46019,46025,45971,45977};
            var output=new StringBuilder();
            foreach(int id in roots)foreach(var node in byId[id].GetComponentsInChildren<Transform>(true))
            {
                if(node==byId[id]||!idFor.TryGetValue(node,out int childId))continue;
                var m=node.localToWorldMatrix;output.Append(childId);
                for(int c=0;c<4;c++)for(int r=0;r<4;r++)output.Append(';').Append(m[r,c].ToString("R",CultureInfo.InvariantCulture));
                output.AppendLine();
            }
            var args=Environment.GetCommandLineArgs();File.WriteAllText(args[Array.IndexOf(args,"-linear-output")+1],output.ToString());
            Debug.Log("LINEAR_PANEL_MATRICES_EXPORTED "+output.ToString().Split('\n').Length);
        }
    }
}
