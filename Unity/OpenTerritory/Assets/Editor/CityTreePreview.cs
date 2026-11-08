using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityTreePreview
    {
        public static void Capture()
        {
            var root = new GameObject("Temporary tree gallery") { hideFlags = HideFlags.HideAndDontSave };
            var cameraObject = new GameObject("Temporary tree gallery camera") { hideFlags = HideFlags.HideAndDontSave };
            var meshes = new List<Mesh>(); RenderTexture rt = null; Texture2D pixels = null;
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")); material.SetColor("_Color",Color.white); material.SetFloat("_Glossiness",.06f);
            var active = RenderTexture.active;
            try
            {
                for(int i=0;i<24;i++)
                {
                    var go=new GameObject(CityVegetation.Forms[i%8]);go.layer=30;go.transform.SetParent(root.transform);
                    go.transform.position=new Vector3(i%8*11,0,i/8*16);
                    var mesh=CityTreeModels.Create(i,0);meshes.Add(mesh);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
                }
                var camera=cameraObject.AddComponent<Camera>();camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.64f,.73f,.75f);camera.orthographic=true;camera.orthographicSize=26;
                camera.transform.position=new Vector3(38.5f,32,-64);camera.transform.LookAt(new Vector3(38.5f,4.3f,16));
                rt=new RenderTexture(2000,1050,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                pixels=new Texture2D(2000,1050,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,2000,1050),0,0);pixels.Apply();
                Directory.CreateDirectory("../Reports/vegetation");File.WriteAllBytes("../Reports/vegetation/variations-natural.png",pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=active;
                Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(material);
                foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
                if(rt)Object.DestroyImmediate(rt);if(pixels)Object.DestroyImmediate(pixels);
            }
        }
    }
}
