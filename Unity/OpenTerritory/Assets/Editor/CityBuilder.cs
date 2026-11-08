using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor {
public static class CityBuilder {
 const string Root="Assets/Imported/",ScenePath="Assets/Scenes/BaiaDoSol.unity",LibraryPath=Root+"CityAssets.asset";
 static CityAssetLibrary library;static int meshCount;
 sealed class Batch {
  public readonly List<Vector3> p=new List<Vector3>(),n=new List<Vector3>();public readonly List<Vector2> uv=new List<Vector2>(),surface=new List<Vector2>();public readonly List<Color> colors=new List<Color>();public readonly List<int> tris=new List<int>();
  public int material;public Transform parent;public bool shadows;public string sharedKey;
  public void Add(GeometryData g,GeometryGroup group,Matrix4x4 m,Color tint,float code){
   int start=p.Count,count=g.positions.Length/3;var normal=m.inverse.transpose;
   for(int i=0;i<count;i++){
    p.Add(m.MultiplyPoint3x4(new Vector3(g.positions[i*3],g.positions[i*3+1],g.positions[i*3+2])));
    n.Add(g.normals.Length>=i*3+3?normal.MultiplyVector(new Vector3(g.normals[i*3],g.normals[i*3+1],g.normals[i*3+2])).normalized:Vector3.up);
    uv.Add(g.uv.Length>=i*2+2?new Vector2(g.uv[i*2],g.uv[i*2+1]):Vector2.zero);surface.Add(new Vector2(code,0));
    colors.Add(g.colors.Length>=i*3+3?new Color(g.colors[i*3],g.colors[i*3+1],g.colors[i*3+2],1)*tint:tint);
   }
   int end=Mathf.Min(g.indices.Length,group.start+group.count);
   // Coordinates are kept in the native +Z convention. Preserve the cross-product
   // orientation so triangle winding agrees with the supplied outward normals.
   bool mirrored=m.determinant<0;
   for(int j=group.start;j+2<end;j+=3){tris.Add(start+g.indices[j]);tris.Add(start+g.indices[j+(mirrored?2:1)]);tris.Add(start+g.indices[j+(mirrored?1:2)]);}
  }
  public Mesh Mesh(string name){var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(p);m.SetNormals(n);m.SetUVs(0,uv);m.SetUVs(1,surface);m.SetColors(colors);m.SetTriangles(tris,0);m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);meshCount++;return m;}
 }
 [MenuItem("Open Territory/Reconstruir cidade nativa")]
 public static void Import(){
  var watch=System.Diagnostics.Stopwatch.StartNew();meshCount=0;AssetDatabase.Refresh();
  var visual=JsonUtility.FromJson<VisualData>(File.ReadAllText(Root+"visual.json"));var manifest=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/world.json");var world=JsonUtility.FromJson<WorldData>(manifest.text);
  Directory.CreateDirectory("Assets/Scenes");if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LibraryPath))AssetDatabase.DeleteAsset(LibraryPath);
  library=ScriptableObject.CreateInstance<CityAssetLibrary>();library.sourceHash=world.sourceHash;AssetDatabase.CreateAsset(library,LibraryPath);
  foreach(string file in Directory.GetFiles(Root+"Textures","*.png")){var imp=AssetImporter.GetAtPath(file.Replace('\\','/')) as TextureImporter;if(!imp)continue;imp.mipmapEnabled=true;imp.maxTextureSize=2048;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.sRGBTexture=!file.Contains("city-atlas")&&!file.Contains("city-cover");imp.wrapMode=TextureWrapMode.Repeat;imp.filterMode=FilterMode.Bilinear;imp.alphaIsTransparency=true;imp.SaveAndReimport();}
  var opaque=Shader.Find("OpenTerritory/CitySurface");var glass=Shader.Find("OpenTerritory/CityGlass");if(!opaque||!glass)throw new Exception("Native city shaders did not compile");
  var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/city-atlas.png");var cover=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/city-cover.png");
  var materials=visual.materials.Select((m,i)=>{var a=new Material(m.transparent?glass:opaque){name=m.name,enableInstancing=true};a.SetColor("_Color",new Color(m.color.r,m.color.g,m.color.b,m.opacity).gamma);a.SetColor("_EmissionColor",m.emission.gamma);a.SetFloat("_Glossiness",1-m.roughness);a.SetFloat("_Metallic",m.metalness);a.SetFloat("_Unlit",m.unlit?1:0);a.SetFloat("_Cull",m.doubleSide?0:2);if(!m.transparent){a.SetFloat("_Surface",m.surface);a.SetFloat("_Sea",m.sea);a.SetTexture("_CityAtlas",atlas);a.SetTexture("_CityCover",cover);}if(!string.IsNullOrEmpty(m.texture)){a.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Textures/"+m.texture+".png");a.mainTextureScale=new Vector2(-m.repeat.x,m.repeat.y);a.mainTextureOffset=new Vector2(1-m.offset.x,m.offset.y);}AssetDatabase.AddObjectToAsset(a,library);return a;}).ToArray();
  CityHomeBuilder.PrepareImport(visual,world);
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);scene.name="Baía do Sol";
  GameObject root=new GameObject(string.IsNullOrEmpty(world.layoutId)?"Baía do Sol • cidade nativa":"Marin • costa e vias traçadas da referência"),visualRoot=new GameObject("Assets reaproveitados e articulações"),staticRoot=new GameObject("Novo mapa • geometria agrupada"),physicsRoot=new GameObject("Colisões e relevo");visualRoot.transform.SetParent(root.transform);staticRoot.transform.SetParent(root.transform);physicsRoot.transform.SetParent(root.transform);
  var nodes=new Dictionary<int,Transform>();var visible=new Dictionary<int,bool>();
  foreach(var node in visual.nodes){var go=new GameObject(node.name);go.transform.SetParent(node.parent>=0&&nodes.TryGetValue(node.parent,out var par)?par:visualRoot.transform,false);go.transform.localPosition=node.position;go.transform.localRotation=node.rotation;go.transform.localScale=node.scale;nodes.Add(node.id,go.transform);visible[node.id]=node.visible&&(node.parent<0||!visible.ContainsKey(node.parent)||visible[node.parent]);if(!node.visible)go.SetActive(false);}
  // The gameplay manifest owns spawn; the reused visual donor can retain an old pose.
  if(nodes.TryGetValue(world.playerId,out var playerSpawn))playerSpawn.position=world.spawn;
  var batches=new Dictionary<string,Batch>();var lodStaticRoots=new Dictionary<string,Transform>();int lightCount=0;
  foreach(var node in visual.nodes){
   var transform=nodes[node.id];
   if(CityVegetation.IsTree(node))continue;
   if(node.kind=="light"){
    if(node.lightType=="SpotLight"||node.lightType=="PointLight"){var l=transform.gameObject.AddComponent<Light>();l.type=node.lightType=="SpotLight"?LightType.Spot:LightType.Point;l.color=node.lightColor;l.intensity=Mathf.Clamp(node.intensity*.04f,0,6);l.range=node.range>0?node.range:14;l.spotAngle=58;l.shadows=LightShadows.None;if(l.type==LightType.Spot)transform.localRotation*=Quaternion.Euler(0,0,0);lightCount++;}continue;
   }
   if(node.geometry<0||!node.dynamic&&!visible[node.id])continue;var geometry=visual.geometries[node.geometry];
   var groups=node.materials.Length>1&&geometry.groups.Length>0?geometry.groups:new[]{new GeometryGroup{start=0,count=geometry.indices.Length,materialIndex=0}};
   var instances=node.instances.Length>0?node.instances:new[]{new InstanceData{matrix=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},color=Color.white}};
   foreach(var instance in instances){var matrix=new Matrix4x4();for(int c=0;c<4;c++)matrix.SetColumn(c,new Vector4(instance.matrix[c*4],instance.matrix[c*4+1],instance.matrix[c*4+2],instance.matrix[c*4+3]));
    if(!node.dynamic)matrix=transform.localToWorldMatrix*matrix;
    foreach(var group in groups){int mi=node.materials[Mathf.Min(node.materials.Length-1,group.materialIndex)];Vector3 point=matrix.MultiplyPoint3x4(Vector3.zero);string staticKey=node.dynamic?null:CityLodBuilder.StaticKey(node,visual.materials[mi],point);string key=node.dynamic?"d-"+node.id+"-"+mi:"s-"+staticKey+"-"+mi;
     Transform batchParent=transform;if(!node.dynamic&&!lodStaticRoots.TryGetValue(staticKey,out batchParent)){batchParent=new GameObject(staticKey).transform;batchParent.SetParent(staticRoot.transform,false);lodStaticRoots[staticKey]=batchParent;}
     if(!batches.TryGetValue(key,out var batch)){batch=new Batch{material=mi,parent=batchParent,sharedKey=node.dynamic&&node.instances.Length==0?node.geometry+"-"+group.start+"-"+group.count:null};batches[key]=batch;}
     batch.shadows|=node.castShadow;batch.Add(geometry,group,matrix,instance.color,instance.surface);
    }
   }
  }
  var meshCache=new Dictionary<string,Mesh>();
  CityVegetation.Build(root.transform,visual,world,library);
  foreach(var pair in batches){var b=pair.Value;var go=new GameObject(pair.Key);go.transform.SetParent(b.parent,false);Mesh mesh;if(b.sharedKey==null||!meshCache.TryGetValue(b.sharedKey,out mesh)){mesh=b.Mesh(pair.Key);if(b.sharedKey!=null)meshCache[b.sharedKey]=mesh;}go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=materials[b.material];r.shadowCastingMode=b.shadows?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=true;if(pair.Key.StartsWith("s-"))go.isStatic=true;}
  // Dynamic scripted nodes expect their own renderer, e.g. projector and kinetic mural.
  foreach(var node in visual.nodes.Where(n=>n.dynamic&&n.geometry>=0)){
   var t=nodes[node.id];if(t.childCount!=1)continue;var child=t.GetChild(0);var filter=child.GetComponent<MeshFilter>();var render=child.GetComponent<MeshRenderer>();if(!filter||!render)continue;t.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterials=render.sharedMaterials;r.shadowCastingMode=render.shadowCastingMode;UnityEngine.Object.DestroyImmediate(child.gameObject);
  }
  var residentIds=new HashSet<int>(world.residents.Select(r=>r.id));var rockSolids=new HashSet<int>(world.rocks.Select(r=>r.solidIndex));int solids=0;
  foreach(var s in world.solids){if(!s.active||residentIds.Contains(s.owner)||rockSolids.Contains(s.index)||s.top<=s.bottom)continue;var go=new GameObject((s.rangeBackstop?"Anteparo-de-treino-":"Colisão-")+s.index);go.layer=CityContext.WorldLayer;go.transform.SetParent(physicsRoot.transform,false);go.transform.position=new Vector3(s.x,(s.bottom+s.top)/2,s.z);go.transform.rotation=Quaternion.Euler(0,s.yaw*Mathf.Rad2Deg,0);var box=go.AddComponent<BoxCollider>();box.size=new Vector3(Mathf.Max(.02f,s.w),s.top-s.bottom,Mathf.Max(.02f,s.d));if(s.owner!=0&&nodes.TryGetValue(s.owner,out var owner))go.transform.SetParent(owner,true);else go.isStatic=true;solids++;}
  var floors=new Dictionary<string,List<Vector3>>();foreach(var f in visual.floors){if(f.vertices.Length<9)continue;string key=Mathf.FloorToInt(f.vertices[0]/32)+","+Mathf.FloorToInt(f.vertices[2]/32);if(!floors.TryGetValue(key,out var p)){p=new List<Vector3>();floors[key]=p;}for(int i=0;i+8<f.vertices.Length;i+=9){var a=new Vector3(f.vertices[i],f.vertices[i+1],f.vertices[i+2]);var b=new Vector3(f.vertices[i+3],f.vertices[i+4],f.vertices[i+5]);var c=new Vector3(f.vertices[i+6],f.vertices[i+7],f.vertices[i+8]);p.Add(a);if(Vector3.Cross(b-a,c-a).y>=0){p.Add(b);p.Add(c);}else{p.Add(c);p.Add(b);}}}
  foreach(var f in floors){var go=new GameObject("Piso-"+f.Key);go.transform.SetParent(physicsRoot.transform);go.layer=CityContext.GroundLayer;go.isStatic=true;var mesh=new Mesh{name=go.name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(f.Value);mesh.SetTriangles(Enumerable.Range(0,f.Value.Count).ToArray(),0);mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);go.AddComponent<MeshCollider>().sharedMesh=mesh;}
  foreach(var d in world.docks){var go=new GameObject("Píer • piso físico");go.layer=CityContext.GroundLayer;go.transform.SetParent(physicsRoot.transform);go.transform.position=new Vector3(d.x,d.top-.12f,d.z);go.transform.rotation=Quaternion.Euler(0,d.yaw*Mathf.Rad2Deg,0);go.AddComponent<BoxCollider>().size=new Vector3(d.w,.24f,d.d);}
  var camera=new GameObject("Câmera de exploração");camera.tag="MainCamera";var cam=camera.AddComponent<Camera>();camera.AddComponent<AudioListener>();camera.transform.SetPositionAndRotation(world.spawn+CityFollowCamera.DefaultOffset,Quaternion.Euler(CityFollowCamera.DefaultPitch,0,0));cam.nearClipPlane=.08f;cam.farClipPlane=750;cam.fieldOfView=CityUI.FixedFieldOfView;cam.backgroundColor=new Color(.59f,.75f,.77f);cam.clearFlags=CameraClearFlags.SolidColor;
  var sun=new GameObject("Sol da baía");sun.transform.rotation=Quaternion.Euler(46,-34,0);var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.color=new Color(1,.92f,.80f);light.shadows=LightShadows.Soft;light.shadowBias=.025f;light.shadowNormalBias=.18f;RenderSettings.sun=light;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.57f,.64f,.67f);RenderSettings.ambientEquatorColor=new Color(.45f,.48f,.43f);RenderSettings.ambientGroundColor=new Color(.28f,.27f,.23f);RenderSettings.fog=true;RenderSettings.fogColor=cam.backgroundColor;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=150;RenderSettings.fogEndDistance=420;
  var runtime=new GameObject("Simulação nativa");var registry=runtime.AddComponent<WorldRegistry>();registry.manifest=manifest;registry.nodes=nodes.Select(n=>new NodeReference{id=n.Key,transform=n.Value}).ToArray();runtime.AddComponent<CityContext>();
  CitySteeringWheelBuilder.Fit(world,registry);
  CityLodBuilder.Build(root.transform,staticRoot.transform,nodes,world,library);
  AssetDatabase.SaveAssets();
  CityWindowsBuilder.Build(visual,world);
  CityLightingBuilder.Bake(visual,world,nodes);
  CityGroundCoverBuilder.Build(root.transform,visual,world);
  CityRockBuilder.Build(root.transform,world,library);
  CityHomeBuilder.Build(root.transform,world,nodes);
  CityCemeteryBuilder.Build(root.transform,world);
  CityParkBuilder.Build(root.transform,world);
  CityCarDealershipBuilder.Build(root.transform,world);
  CityFixtureDestructionBuilder.Bake();
  CityInteriorGroundBuilder.Bake();
  CityFireGarageBuilder.Build(world,registry);
  CityFoundationBuilder.Build(world);
  CityPropGroundingBuilder.Build(world,registry);
  CityBikeShopBuilder.Build(root.transform,world);
  CityClothingShopBuilder.Build(root.transform,world);
  CityBlackthornHouseBuilder.Build(root.transform,world);
  CityFireStationPaint.Apply(world);
  CityFireStationSiteBuilder.Build(root.transform,world);
  CityWaterfrontBuilder.Build(root.transform,world);
  CityPoliceStationBuilder.Build(root.transform,world);
  CityParkingLotBuilder.Build(root.transform,world,registry);
  CityLinearParkBuilder.Build(root.transform,world);
  Configure();AssetDatabase.SetMainObject(library,LibraryPath);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
  Directory.CreateDirectory("../Reports");File.WriteAllText("../Reports/import.json",JsonUtility.ToJson(new ImportReport{nodes=nodes.Count,meshes=meshCount,renderers=batches.Count,solids=solids,groundChunks=floors.Count,lights=lightCount,seconds=(float)watch.Elapsed.TotalSeconds},true));Debug.Log("CITY_IMPORT_OK nodes="+nodes.Count+" meshes="+meshCount+" solids="+solids);
 }
 [Serializable]sealed class ImportReport{public int nodes,meshes,renderers,solids,groundChunks,lights;public float seconds;}
 // Complete a generated scene after a downstream authoring step was interrupted.
 // This deliberately refuses scenes that already contain the completed interiors.
 public static void CompleteCurrentImport(){
  if(Application.isPlaying||UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>())throw new InvalidOperationException("A cena deve estar fora do Play Mode e ainda sem os interiores gerados.");
  var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();if(!registry||!registry.manifest)throw new InvalidOperationException("Registro da cidade ausente.");
  var world=JsonUtility.FromJson<WorldData>(registry.manifest.text);var visual=JsonUtility.FromJson<VisualData>(File.ReadAllText(Root+"visual.json"));
  CityHomeBuilder.PrepareImport(visual,world);var nodes=registry.nodes.Where(n=>n.transform).ToDictionary(n=>n.id,n=>n.transform);
  library=AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(LibraryPath);if(!library)throw new InvalidOperationException("Biblioteca da importação ausente.");
  var manager=UnityEngine.Object.FindFirstObjectByType<CityLodManager>();if(!manager)throw new InvalidOperationException("Geometria da importação ausente.");var root=manager.transform;
  CityWindowsBuilder.Build(visual,world);CityLightingBuilder.Bake(visual,world,nodes);CityGroundCoverBuilder.Build(root,visual,world);CityRockBuilder.Build(root,world,library);
  CityHomeBuilder.Build(root,world,nodes);CityCemeteryBuilder.Build(root,world);CityCarDealershipBuilder.Build(root,world);CityFixtureDestructionBuilder.Bake();CityInteriorGroundBuilder.Bake();CityFoundationBuilder.Build(world);CityPropGroundingBuilder.Build(world,registry);CitySteeringWheelBuilder.Fit(world,registry);CityBlackthornHouseBuilder.Build(root,world);CityClothingShopBuilder.Build(root,world);CityWaterfrontBuilder.Build(root,world);CityParkingLotBuilder.Build(root,world,registry);CityLinearParkBuilder.Build(root,world);Configure();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
  EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};Debug.Log("CITY_IMPORT_COMPLETED_AFTER_RESUME");
 }
 static void Configure(){
  // Imported geometry is already grouped by region/material. Keep these
  // meshes readable for the coastal cut instead of baking static buffers.
  PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.StandaloneWindows64,false);
  PlayerSettings.companyName="Open Territory";PlayerSettings.productName="Open Territory";PlayerSettings.bundleVersion="1.0.0";PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=800;PlayerSettings.resizableWindow=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
  PlayerSettings.SplashScreen.show=false;
  PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Unity_4_8);PlayerSettings.usePlayerLog=true;
  QualitySettings.antiAliasing=2;QualitySettings.shadowDistance=55;QualitySettings.shadowCascades=2;QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowResolution=ShadowResolution.High;QualitySettings.vSyncCount=1;QualitySettings.pixelLightCount=4;
  Physics.defaultContactOffset=.015f;Time.fixedDeltaTime=.02f;Physics.gravity=new Vector3(0,-9.81f,0);
  var tag=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var layers=tag.FindProperty("layers");string[] names={"Ground","World","Vehicle","Actor","Player"};for(int i=0;i<names.Length;i++)layers.GetArrayElementAtIndex(i+8).stringValue=names[i];tag.ApplyModifiedPropertiesWithoutUndo();
  var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);var shaders=settings.FindProperty("m_AlwaysIncludedShaders");
  foreach(string name in new[]{"Standard","Unlit/Color","Unlit/Texture","Legacy Shaders/Particles/Alpha Blended","Particles/Standard Unlit","Sprites/Default","UI/Default","OpenTerritory/CitySurface","OpenTerritory/CityGlass","OpenTerritory/CityWindow"}){var shader=Shader.Find(name);if(!shader)continue;bool found=false;for(int i=0;i<shaders.arraySize;i++)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader)found=true;if(!found){int i=shaders.arraySize;shaders.InsertArrayElementAtIndex(i);shaders.GetArrayElementAtIndex(i).objectReferenceValue=shader;}}
  settings.ApplyModifiedPropertiesWithoutUndo();
 }
 [MenuItem("Open Territory/Construir Windows")]
 public static void Build(){if(!File.Exists(ScenePath))Import();Configure();string output="Builds/WindowsRelease/BaiaDoSol.exe";var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-city-build-path");if(at>=0&&at+1<args.Length)output=args[at+1];Directory.CreateDirectory(Path.GetDirectoryName(output));var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=Array.IndexOf(args,"-city-development")>=0?BuildOptions.Development:BuildOptions.None});Debug.Log("CITY_BUILD_"+r.summary.result+" bytes="+r.summary.totalSize);if(r.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+r.summary.result);}
 public static void ImportAndBuild(){Import();Build();}
}
}
