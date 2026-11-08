using System;
using UnityEngine;

namespace OpenTerritory {
[Serializable] public class WorldData {
 public int version=1,playerId,droneId; public string sourceHash,layoutId; public Vector3 spawn,fountain;
 public RigData playerRig;
 public NaturalTreeData[] natureTrees=new NaturalTreeData[0];public NaturalRockData[] rocks=new NaturalRockData[0];
 public NaturalGroveData[] natureGroves=new NaturalGroveData[0];
 public ResidentData[] residents=new ResidentData[0]; public AnimalData[] animals=new AnimalData[0];
 public VehicleData[] vehicles=new VehicleData[0]; public BoatData[] boats=new BoatData[0];
 public PlaceData[] places=new PlaceData[0]; public SeatData[] seats=new SeatData[0];
 public SolidData[] solids=new SolidData[0]; public NavNodeData[] nav=new NavNodeData[0];
 public LocationData[] locations=new LocationData[0]; public StopData[] stops=new StopData[0];
 public DeviceData[] devices=new DeviceData[0]; public ServiceData[] services=new ServiceData[0];
 public DestructibleData[] destructibles=new DestructibleData[0]; public SignalData[] signals=new SignalData[0];
 public DockData[] docks=new DockData[0]; public LadderData[] ladders=new LadderData[0];
 public Vector3[] roadRoute=new Vector3[0]; public GroundData ground;
 public MusicData music=new MusicData(); public FishData fish=new FishData();
 public CinemaData cinema=new CinemaData(); public HarborData harbor=new HarborData();
 public PracticeData practice=new PracticeData();
 public StreetLampData[] streetLamps=new StreetLampData[0]; public int[] streetLightIds=new int[0],shopLightIds=new int[0]; public int canopyLightId;
}
[Serializable] public class StreetLampData {public int node,bulb;public Vector3 position;}
[Serializable] public class NaturalTreeData {public int node,solidIndex,variant,grove;public Vector3 position,scale;public float yaw;}
[Serializable] public class NaturalRockData {public int solidIndex,variant;public Vector3 position,scale;public float yaw;}
[Serializable] public class NaturalGroveData {public float x,z,radius,ratio,yaw,clearingX,clearingZ,clearingRadius;public int count,originalCount;}
[Serializable] public class RigData {
 public int root,hips,chest,head; public int[] limbs=new int[0],knees=new int[0],elbows=new int[0],hands=new int[0],ankles=new int[0];
 public float hipHeight=.84f,ankleHeight=.08f,thighLength=.41f,shinLength=.35f,upperLength=.38f,lowerLength=.4f;
 public bool seated;public float seatHeight;
}
[Serializable] public class ResidentData {
 public int id; public string name,line,role,activity,initialDestination; public string[] lines=new string[0],routine=new string[0];
 public bool stationary,sitting,indoor,child,performance,musicGuest; public float phase,speed=1.15f; public RigData rig;
 public Vector3 position; public string place;
}
[Serializable] public class AnimalData {public int id,head,body;public int[] legs=new int[0],wings=new int[0];public string type;public Vector3 position;public float phase;}
[Serializable] public class WheelData {public int pivot,spin;public Vector3 position;public float radius=.43f,width=.29f;public bool front;}
[Serializable] public class RouteStopData {public string id;public Vector3 position,junction;}
[Serializable] public class VehicleData {
 public int id,driver,rider,steeringWheel; public string kind,role,model;public bool auto;
 public float width=2.5f,length=4.6f,mass=1350,wheelbase=2.7f,maxSteering=.57f,tire=.43f,speed,direction=1;
 public string axis;public float road; public Vector3 seatMount,wheelMount;public int[] doors=new int[0],headlights=new int[0];
 public float rearOffset=1.35f,centerOffset,driveScale=1,massScale=1,frontWeight=.54f,frontDrive=.83f;
 public int[] driverHands=new int[0],driverArms=new int[0],riderLegs=new int[0];
 public int[] brakeLights=new int[0],leftIndicators=new int[0],rightIndicators=new int[0],reverseLights=new int[0];
 public WheelData[] wheels=new WheelData[0];public SurfaceData[] surfaces=new SurfaceData[0];
 public Vector3[] route=new Vector3[0];public Vector3 position;public float yaw;
 public bool oneWay;public RouteStopData[] routeStops=new RouteStopData[0];public Vector3[] alternateRoute=new Vector3[0];
}
[Serializable] public class SurfaceData {public float x,z,w,d,top;}
[Serializable] public class BoatData {public int id,wheel;public string name,model;public float width,length,top,speed,yaw,draft,turn;public Vector3 seat,position;}
[Serializable] public class PlaceData {
 public string name,kind;public float x,z,w,d,elevation,yaw,lotWidth,lotDepth;public DoorData door;
 public bool Contains(Vector3 point,float margin=0){var p=Quaternion.Euler(0,-yaw*Mathf.Rad2Deg,0)*(point-new Vector3(x,elevation,z));return Mathf.Abs(p.x)<(lotWidth>0?lotWidth:w)*.5f-margin&&Mathf.Abs(p.z)<(lotDepth>0?lotDepth:d)*.5f-margin;}
}
[Serializable] public class DoorData {public int id;public float x,z,width,yaw;public bool automatic;public Vector3 position;public Vector3 Approach(float y,float distance)=>new Vector3(x,y,z)+Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0)*Vector3.forward*distance;}
[Serializable] public class SeatData {public int id;public float x,z,yaw,seatTop,width,depth;public bool occupied;public Vector3 position;}
[Serializable] public class SolidData {public int index,owner;public float x,z,w,d,bottom,top,yaw;public bool active=true,vault,opaque,rangeBackstop;}
[Serializable] public class NavNodeData {public int id;public float x,z;public NavEdgeData[] edges=new NavEdgeData[0];}
[Serializable] public class NavEdgeData {public int to;public float cost;public bool crossing;}
[Serializable] public class LocationData {public string id,activity,role;public float x,z,yaw,spacing=1.6f,duration=24;public int capacity=1;}
[Serializable] public class StopData {public string id,name;public float x,z,roadX,roadZ,yaw;}
[Serializable] public class DeviceData {public int node,target;public string id,type,label;public Vector3 position,lookAt;public float yaw,pitch,range=24;}
[Serializable] public class ServiceData {public string id,kind,mode,item,label,place;public Vector3 position,anchor;public float price;public int node;}
[Serializable] public class DestructibleData {public int id,solidIndex;public string type;public Vector3 position;}
[Serializable] public class SignalApproachData {public string road;public int axis,node;public float width;public Vector3 stop,heading;}
[Serializable] public class SignalData {public float x,z,phase;public int phaseCount=2;public SignalApproachData[] approaches=new SignalApproachData[0];public int[] lamps=new int[0],lampAxes=new int[0],lampColors=new int[0];}
[Serializable] public class DockData {public float x,z,w,d,top,yaw;}
[Serializable] public class LadderData {public float x,z,landX,landZ,top;}
[Serializable] public class GroundData {public float x,z,step;public int width,height;public float[] heights=new float[0];}
[Serializable] public class MusicData {public Vector3 position;public int musician,guitar;public int[] handNodes=new int[0];}
[Serializable] public class FishData {public Vector3 position;public int worker,beam,leftPan,rightPan;public Vector3[] actions=new Vector3[0];public int[] fish=new int[0],weights=new int[0];}
[Serializable] public class CinemaData {public int screen;public Vector3 projector;public int[] spools=new int[0];}
[Serializable] public class PracticeData {public int[] targets=new int[0],lamps=new int[0];public Vector3 position;}
[Serializable] public class HarborData {public int load,mural,sock,platter;public int[] slats=new int[0],cables=new int[0];public Vector3 loadPosition;}

[Serializable] public class VisualData {public VisualNode[] nodes;public GeometryData[] geometries;public MaterialData[] materials;public FloorData[] floors;}
[Serializable] public class VisualNode {
 public int id,parent=-1,geometry=-1;public string name,kind;public bool visible=true,dynamic,castShadow=true;
 public Vector3 position,scale=Vector3.one;public Quaternion rotation=Quaternion.identity;public int[] materials=new int[0];
 public InstanceData[] instances=new InstanceData[0];public float[] surfaceCodes;public string lightType;public float intensity,range;public Color lightColor;
}
[Serializable] public class InstanceData {public float[] matrix;public Color color=Color.white;public float surface;}
[Serializable] public class GeometryData {public string name;public float[] positions,normals,uv,colors;public int[] indices;public GeometryGroup[] groups;}
[Serializable] public class GeometryGroup {public int start,count,materialIndex;}
[Serializable] public class MaterialData {
 public string name,texture;public Color color=Color.white,emission=Color.black;public float roughness=.8f,metalness,opacity=1,surface;public int sea;
 public bool transparent,unlit,doubleSide;public Vector2 repeat=Vector2.one,offset;
}
[Serializable] public class FloorData {public float[] vertices;}
}
