using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
namespace TaiwanRider.Editor
{
    public static class RiderSceneBuilder
    {
        const string Root="Assets/TaiwanRider";
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        static RoadRoute route;
        static Transform street;
        [Serializable] class Survey { public Vector3[] points; }
        [Serializable] class Building { public string name;public int side;public float center,width,height; }
        [Serializable] class Buildings {public Building[] items;}
        static Color Hex(string s){ColorUtility.TryParseHtmlString(s,out var c);return c;}
        static string Clean(string name)=>name.Replace(" (Instance)","").Split('.')[0];
        static string Safe(string name)=>string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
        static void Ensure(string folder){if(!Directory.Exists(folder))Directory.CreateDirectory(folder);}
        public static void PrepareImports()
        {
            foreach(string path in Directory.GetFiles(Root+"/Art","*",SearchOption.AllDirectories))
            {
                var ti=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(ti!=null)
                {
                    bool normal=path.Contains("nor_gl");
                    ti.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                    ti.sRGBTexture=!normal&&!path.Contains("metallic_smoothness")&&!path.Contains("rough.");
                    ti.maxTextureSize=path.Contains("Signs")?1024:2048;
                    ti.textureCompression=TextureImporterCompression.CompressedHQ;
                    ti.mipmapEnabled=true;ti.anisoLevel=8;ti.SaveAndReimport();
                }
            }
            Debug.Log("Taiwan Rider texture imports prepared.");
        }
        static Texture2D Texture(string relative)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/"+relative);
        static void Maps(Material m,string folder)
        {
            var diff=Texture("Textures/"+folder+"/diff.png")??Texture("Textures/"+folder+"/diff.jpg");
            var normal=Texture("Textures/"+folder+"/nor_gl.png")??Texture("Textures/"+folder+"/nor_gl.jpg");
            var smooth=Texture("Textures/"+folder+"/metallic_smoothness.png");
            if(diff)m.SetTexture("_BaseMap",diff);
            if(normal){m.SetTexture("_BumpMap",normal);m.SetFloat("_BumpScale",.8f);m.EnableKeyword("_NORMALMAP");}
            if(smooth){m.SetTexture("_MetallicGlossMap",smooth);m.EnableKeyword("_METALLICSPECGLOSSMAP");}
        }
        static Material Material(string name,Color? color=null,float metallic=0,float smoothness=.35f)
        {
            name=Clean(name);if(materials.TryGetValue(name,out var cached))return cached;
            string path=Root+"/Materials/"+Safe(name)+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color??Color.white);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            m.enableInstancing=true;
            switch(name)
            {
                case "Asphalt": Maps(m,"asphalt_02");m.SetColor("_BaseColor",new Color(.90f,.91f,.91f));break;
                case "Concrete":Maps(m,"concrete_floor_worn_001");break;
                case "Pavement":Maps(m,"concrete_floor_worn_001");break;
                case "FacadeTile":Maps(m,"concrete_tile_facade");break;
                case "OldCream":case "OldGreen":case "OldRose":Maps(m,"concrete_floor_worn_001");break;
                case "Instrument":case "Plate":m.SetTexture("_BaseMap",Texture("Signs/"+name+".png"));m.SetColor("_BaseColor",Color.white);break;
            }
            if(name.StartsWith("Sign_")){m.SetTexture("_BaseMap",Texture("Signs/"+name+".png"));m.SetColor("_BaseColor",Color.white);m.SetFloat("_Smoothness",.35f);}
            if(name=="Headlamp"){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(.25f,.23f,.16f));}
            EditorUtility.SetDirty(m);materials[name]=m;return m;
        }
        static GameObject Model(string name,Transform parent,Vector3 position,Quaternion rotation)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Models/"+name+".fbx");
            if(!prefab)throw new InvalidOperationException("Missing model: "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localRotation=rotation*prefab.transform.localRotation;
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials=r.sharedMaterials.Select(old=>
                {
                    string n=Clean(old.name);Color c=old.HasProperty("_Color")?old.color:Color.gray;
                    float metal=(n=="Chrome"||n=="TankSteel")?.85f:(n=="Alloy"||n=="Shutter"||n.Contains("Pearl"))?.5f:0;
                    float smooth=n=="Chrome"?.83f:n=="Glass"?.78f:n.Contains("Pearl")?.65f:n=="Rubber"?.08f:.32f;
                    return Material(n,c,metal,smooth);
                }).ToArray();
                r.receiveShadows=true;r.shadowCastingMode=ShadowCastingMode.On;
            }
            return go;
        }
        static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==name);
        static GameObject Empty(string name,Transform parent=null)
        {var o=new GameObject(name);if(parent)o.transform.SetParent(parent,false);return o;}
        static GameObject Box(string name,Vector3 p,Vector3 s,Material material,Transform parent=null,bool collider=false)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;
            if(parent)o.transform.SetParent(parent,false);o.transform.localPosition=p;o.transform.localScale=s;
            o.GetComponent<Renderer>().sharedMaterial=material;
            if(!collider)UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());
            return o;
        }
        static void Layer(GameObject go,int layer){foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.layer=layer;}
        static void RoadStrip(string name,float from,float to,float left,float right,Material material,float y=0,bool collision=false)
        {
            int n=Mathf.CeilToInt((to-from)/2);var verts=new Vector3[(n+1)*2];var uv=new Vector2[verts.Length];var tris=new int[n*6];
            for(int i=0;i<=n;i++)
            {
                float z=Mathf.Lerp(from,to,(float)i/n);verts[i*2]=route.Position(z,left,y);verts[i*2+1]=route.Position(z,right,y);
                uv[i*2]=new Vector2(left/3,z/3);uv[i*2+1]=new Vector2(right/3,z/3);
                if(i<n){int a=i*6,b=i*2;tris[a]=b;tris[a+1]=b+2;tris[a+2]=b+1;tris[a+3]=b+1;tris[a+4]=b+2;tris[a+5]=b+3;}
            }
            var mesh=new Mesh{name=name};mesh.vertices=verts;mesh.uv=uv;mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Root+"/Meshes/"+Safe(name)+".asset";
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);
            var go=Empty(name,street);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
        }
        static void Rod(string name,Vector3 a,Vector3 b,float radius,Material m,Transform parent)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cylinder);o.name=name;o.transform.SetParent(parent,false);
            o.transform.localPosition=(a+b)*.5f;o.transform.localScale=new Vector3(radius*2,(b-a).magnitude*.5f,radius*2);o.transform.up=(b-a).normalized;
            o.GetComponent<Renderer>().sharedMaterial=m;UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());
        }
        static void Streets()
        {
            Material asphalt=Material("Asphalt"),pavement=Material("Pavement",Hex("#CAC2AF")),white=Material("RoadWhite",Hex("#E3DED0")),yellow=Material("RoadYellow",Hex("#BFA147")),red=Material("CurbRed",Hex("#9D493E")),metal=Material("StreetSteel",Hex("#535F59"),.7f,.4f);
            RoadStrip("Tingzhou metric road",-120,120,-6.2f,6.2f,asphalt,0,true);
            Box("Lane 90",new Vector3(19,-.10f,0),new Vector3(27,.20f,9),asphalt,street,true).isStatic=true;
            for(int side=-1;side<=1;side+=2)
            {
                if(side<0)RoadStrip("West arcade paving",-118,118,-11,-6.2f,pavement,.15f,true);
                else{RoadStrip("East arcade paving south",-118,-5,6.2f,11,pavement,.15f,true);RoadStrip("East arcade paving north",5,118,6.2f,11,pavement,.15f,true);}
                for(float z=-117;z<117;z+=3)
                {
                    if(side>0 && Mathf.Abs(z)<6)continue;
                    var curb=Box("Stone curb",route.Position(z,side*6.18f,.075f),new Vector3(.20f,.15f,2.97f),pavement,street,true);curb.transform.rotation=route.Heading(z);curb.isStatic=true;
                    var paint=Box("Red curb marking",route.Position(z,side*6.10f,.157f),new Vector3(.14f,.009f,2.95f),red,street);paint.transform.rotation=route.Heading(z);paint.isStatic=true;
                }
                for(float z=-103;z<112;z+=20)
                {
                    var drain=Box("Drain cover",route.Position(z,side*5.91f,.007f),new Vector3(.43f,.011f,.78f),metal,street);drain.transform.rotation=route.Heading(z);
                    for(int j=0;j<10;j++)Box("Drain slot",route.Position(z-.33f+j*.071f,side*5.91f,.014f),new Vector3(.31f,.002f,.024f),Material("DrainDark",Hex("#17201C")),street);
                }
            }
            RoadStrip("Double yellow left",-116,116,-.16f,-.06f,yellow,.008f);
            RoadStrip("Double yellow right",-116,116,.06f,.16f,yellow,.008f);
            for(int side=-1;side<=1;side+=2)
            {
                RoadStrip("Edge line "+side,-116,116,side*4.65f-.05f,side*4.65f+.05f,white,.009f);
                for(float z=-95;z<100;z+=12)
                {
                    if(Mathf.Abs(z)<14)continue;
                    for(float dz=-2;dz<=2;dz+=4)
                        Box("Parking bay",route.Position(z+dz,side*5.35f,.011f),new Vector3(1.35f,.009f,.08f),white,street).transform.rotation=route.Heading(z);
                }
            }
            // Crossing perpendicular to the main road at the actual alley intersection.
            for(int i=0;i<12;i++)Box("Crosswalk stripe",route.Position(-7,-5.3f+i*.92f,.014f),new Vector3(.45f,.009f,2.65f),white,street).transform.rotation=route.Heading(-7);
            RoadStrip("South stop line",-10.0f,-9.65f,.23f,4.6f,white,.014f);
            var steel=Material("ManholeSteel",Hex("#4D524B"),.5f,.25f);
            for(int i=0;i<8;i++)
            {
                float z=-91+i*25;var cover=GameObject.CreatePrimitive(PrimitiveType.Cylinder);cover.name="Cast iron manhole";cover.transform.SetParent(street,false);cover.transform.position=route.Position(z,i%2==0?1.15f:-1.2f,.008f);cover.transform.localScale=new Vector3(.67f,.006f,.67f);cover.GetComponent<Renderer>().sharedMaterial=steel;UnityEngine.Object.DestroyImmediate(cover.GetComponent<Collider>());
                for(int j=-2;j<=2;j++)Box("Manhole grooves",route.Position(z+j*.09f,i%2==0?1.15f:-1.2f,.016f),new Vector3(.44f,.003f,.012f),metal,street);
            }
            for(int i=0;i<25;i++)
            {
                float z=-98+i*8.2f,x=(i%3-1)*2.2f;
                var patch=Material("RoadRepair",new Color(.68f,.69f,.68f));Maps(patch,"asphalt_02");
                Box("Resurfaced asphalt patch",route.Position(z,x,.002f),new Vector3(1.2f+i%3*.4f,.003f,2.1f),patch,street).transform.rotation=route.Heading(z)*Quaternion.Euler(0,(i%5-2)*4,0);
            }
            // Utility poles, hanging cable runs, municipal signs and practical street lamps.
            foreach(float z in new[]{-87f,-51f,-17f,24f,63f,100f})
            {
                Vector3 p=route.Position(z,-6.65f);
                Rod("Street lamp pole",p,p+Vector3.up*7,.065f,metal,street);
                Rod("Lamp outreach",p+Vector3.up*6.7f,p+new Vector3(1.9f,7,0),.043f,metal,street);
                Box("Street lamp head",p+new Vector3(1.95f,7,0),new Vector3(.78f,.16f,.32f),metal,street);
                for(int wire=0;wire<3;wire++)
                {
                    Vector3 previous=p+new Vector3(0,7.5f+wire*.11f,0);
                    for(int j=1;j<=8;j++)
                    {float t=j/8f;Vector3 next=route.Position(z+t*35,-6.75f,7.5f+wire*.11f-Mathf.Sin(t*Mathf.PI)*.65f);Rod("Overhead service cable",previous,next,.011f,metal,street);previous=next;}
                }
            }
            StreetText("Tingzhou road sign","汀州路三段",new Vector3(-5.8f,3.5f,-12),"Tingzhou Rd. Sec. 3");
            StreetText("Lane 90 sign","羅斯福路四段90巷",new Vector3(6.7f,3.3f,5.1f),"Lane 90, Roosevelt Rd.");
            for(int side=-1;side<=1;side+=2)
            {
                var pole=route.Position(side<0?8:-12,side*6.45f);
                Rod("Traffic signal pole",pole,pole+Vector3.up*4.2f,.06f,metal,street);
                var signal=Empty("Traffic signal",street);signal.transform.position=pole+Vector3.up*3.65f;signal.transform.rotation=Quaternion.Euler(0,side<0?0:180,0);
                Box("Signal housing",Vector3.zero,new Vector3(.30f,.89f,.26f),Material("SignalBlack",Hex("#1D2422")),signal.transform);
                for(int j=0;j<3;j++)
                {
                    var bulb=GameObject.CreatePrimitive(PrimitiveType.Sphere);bulb.name="Signal lens";bulb.transform.SetParent(signal.transform,false);bulb.transform.localPosition=new Vector3(0,.26f-j*.26f,-.142f);bulb.transform.localScale=new Vector3(.185f,.185f,.03f);UnityEngine.Object.DestroyImmediate(bulb.GetComponent<Collider>());
                    var m=Material("Signal "+j,j==2?Hex("#4FAA6A"):j==0?Hex("#542921"):Hex("#514626"));if(j==2){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex("#62C481")*.6f);}bulb.GetComponent<Renderer>().sharedMaterial=m;
                }
            }
        }
        static void StreetText(string name,string chinese,Vector3 position,string english)
        {
            var parent=Empty(name,street);parent.transform.position=position;
            var green=Material("StreetSignGreen",Hex("#155E49"));
            Box("Enamel street plate",Vector3.zero,new Vector3(3.4f,.63f,.075f),green,parent.transform);
            var font=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Art/Fonts/NotoSansTC.otf");
            var text=Empty("Street name",parent.transform).AddComponent<TextMesh>();text.font=font;text.GetComponent<Renderer>().sharedMaterial=font.material;text.text=chinese+"\n"+english;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.characterSize=.024f;text.fontSize=64;text.color=Hex("#F3F2DB");text.transform.localPosition=new Vector3(0,0,-.047f);text.transform.localRotation=Quaternion.identity;
        }
        static GameObject ScooterModel(string file,Transform parent)
        {
            var model=Model(file,parent,Vector3.zero,Quaternion.identity);
            Transform fw=Find(model,"FrontWheel"),rw=Find(model,"RearWheel");
            if(fw && rw && parent.InverseTransformPoint(fw.position).z<parent.InverseTransformPoint(rw.position).z)model.transform.localRotation=Quaternion.Euler(0,180,0)*model.transform.localRotation;
            return model;
        }
        static ScooterController Player()
        {
            var root=Empty("Player • 125cc scooter");root.layer=8;
            var body=root.AddComponent<Rigidbody>();var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.59f,0);collider.size=new Vector3(.54f,1.18f,1.55f);
            var contact=new PhysicsMaterial("Assisted rolling contact"){dynamicFriction=0,staticFriction=0,bounciness=0,frictionCombine=PhysicsMaterialCombine.Minimum};
            string contactPath=Root+"/Data/RollingContact.physicMaterial";
            var oldContact=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(contactPath);
            if(oldContact){EditorUtility.CopySerialized(contact,oldContact);UnityEngine.Object.DestroyImmediate(contact);contact=oldContact;}else AssetDatabase.CreateAsset(contact,contactPath);
            collider.sharedMaterial=contact;
            var rider=root.AddComponent<ScooterController>();
            string defPath=Root+"/Data/Scooter125.asset";rider.definition=AssetDatabase.LoadAssetAtPath<ScooterDefinition>(defPath);
            if(!rider.definition){rider.definition=ScriptableObject.CreateInstance<ScooterDefinition>();AssetDatabase.CreateAsset(rider.definition,defPath);}
            rider.definition.topSpeedKph=100f;EditorUtility.SetDirty(rider.definition);
            var visual=Empty("Lean visual",root.transform);rider.visualBody=visual.transform;
            var model=ScooterModel("Scooter125",visual.transform);
            rider.frontSteering=Find(model,"FrontSteering");rider.frontVisualWheel=Find(model,"FrontWheel");rider.rearVisualWheel=Find(model,"RearWheel");
            for(int j=0;j<2;j++)
            {
                var go=Empty(j==0?"Front wheel physics":"Rear wheel physics",root.transform);go.transform.localPosition=new Vector3(0,.275f,j==0?.6375f:-.6375f);
                var w=go.AddComponent<WheelCollider>();w.radius=.217f;w.mass=6;w.suspensionDistance=.12f;w.forceAppPointDistance=.1f;
                w.suspensionSpring=new JointSpring{spring=9500,damper=1450,targetPosition=.5f};
                var friction=w.sidewaysFriction;friction.stiffness=0;w.sidewaysFriction=friction;
                friction=w.forwardFriction;friction.stiffness=0;w.forwardFriction=friction;
                if(j==0)rider.frontWheel=w;else rider.rearWheel=w;
            }
            var rig=Empty("Rider eye position",root.transform);rig.transform.localPosition=new Vector3(0,1.43f,-.40f);rig.transform.localRotation=Quaternion.Euler(4,0,0);rider.cameraRig=rig.transform;
            var camera=Empty("Rider Camera",rig.transform).AddComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.025f;camera.farClipPlane=240;camera.fieldOfView=64;camera.allowHDR=true;camera.backgroundColor=Hex("#B6C4C7");camera.gameObject.AddComponent<AudioListener>();
            var additional=camera.GetUniversalAdditionalCameraData();additional.renderPostProcessing=true;additional.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;additional.antialiasingQuality=AntialiasingQuality.High;rider.riderCamera=camera;
            var jacket=Material("RiderJacket",Hex("#253A41"),0,.18f);var glove=Material("RiderGlove",Hex("#272B2A"),0,.15f);
            for(int side=-1;side<=1;side+=2)
            {
                Rod("Jacket sleeve",new Vector3(side*.21f,1.01f,-.21f),new Vector3(side*.303f,.978f,.36f),.047f,jacket,visual.transform);
                var hand=GameObject.CreatePrimitive(PrimitiveType.Sphere);hand.name="Riding glove";hand.transform.SetParent(visual.transform,false);hand.transform.localPosition=new Vector3(side*.300f,.978f,.39f);hand.transform.localScale=new Vector3(.092f,.063f,.14f);hand.GetComponent<Renderer>().sharedMaterial=glove;UnityEngine.Object.DestroyImmediate(hand.GetComponent<Collider>());
                for(int f=0;f<4;f++)Rod("Gloved finger",new Vector3(side*(.266f+f*.017f),.986f,.40f),new Vector3(side*(.266f+f*.017f),.94f,.435f),.009f,glove,visual.transform);
            }
            Layer(root,8);
            foreach(string suffix in new[]{"L","R"})
            {
                var surface=Find(model,"MirrorSurface_"+suffix);if(!surface)continue;
                var mirror=Empty("Rear view "+suffix,root.transform).AddComponent<Camera>();mirror.transform.position=surface.GetComponent<Renderer>().bounds.center;mirror.transform.localRotation=Quaternion.Euler(3,suffix=="L"?166:194,0);mirror.fieldOfView=50;mirror.nearClipPlane=.03f;mirror.farClipPlane=65;mirror.cullingMask=~(1<<8);mirror.enabled=false;
                mirror.GetUniversalAdditionalCameraData().renderShadows=false;
                var view=surface.gameObject.AddComponent<MirrorView>();view.mirrorCamera=mirror;view.surface=surface.GetComponent<Renderer>();
            }
            var sound=root.AddComponent<AudioSource>();sound.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Art/Audio/ScooterEngine.wav");sound.loop=true;sound.playOnAwake=true;sound.volume=.07f;sound.spatialBlend=0;
            return rider;
        }
        static HazardActor Actor(string modelName,ScenarioKind kind,Vector3 position)
        {
            var root=Empty(kind.ToString());root.transform.position=position;
            if(kind==ScenarioKind.ScooterCrossing)root.transform.rotation=Quaternion.Euler(0,-90,0);
            var model=kind==ScenarioKind.ScooterCrossing?ScooterModel(modelName,root.transform):Model(modelName,root.transform,Vector3.zero,Quaternion.identity);
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;
            var col=root.AddComponent<BoxCollider>();col.center=new Vector3(0,kind==ScenarioKind.CarCutIn?.74f:.59f,0);col.size=kind==ScenarioKind.CarCutIn?new Vector3(1.78f,1.42f,4.52f):new Vector3(.62f,1.05f,1.79f);
            var actor=root.AddComponent<HazardActor>();actor.kind=kind;actor.bodyCollider=col;actor.AlignModelForward();Layer(root,10);
            if(kind==ScenarioKind.ScooterCrossing)NpcRider(root.transform);
            return actor;
        }
        static void NpcRider(Transform root)
        {
            Material jacket=Material("NPCJacket",Hex("#687779"));Material dark=Material("Charcoal",Hex("#303A3C"));
            var torso=Box("Rider torso",new Vector3(0,1.08f,-.3f),new Vector3(.38f,.56f,.26f),jacket,root);torso.transform.localRotation=Quaternion.Euler(12,0,0);
            var head=GameObject.CreatePrimitive(PrimitiveType.Sphere);head.name="Helmet";head.transform.SetParent(root,false);head.transform.localPosition=new Vector3(0,1.5f,-.23f);head.transform.localScale=new Vector3(.30f,.32f,.32f);head.GetComponent<Renderer>().sharedMaterial=Material("Helmet",Hex("#B2BAB5"),.2f,.6f);UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
            Box("Helmet visor",new Vector3(0,1.5f,-.08f),new Vector3(.23f,.12f,.035f),Material("Glass",Hex("#243D43"),.4f,.7f),root);
            foreach(int s in new[]{-1,1}){Rod("Sleeve",new Vector3(s*.19f,1.25f,-.2f),new Vector3(s*.3f,.97f,.42f),.047f,jacket,root);Rod("Trousers",new Vector3(s*.16f,.72f,-.34f),new Vector3(s*.18f,.37f,.21f),.061f,dark,root);}
            Layer(root.gameObject,10);
        }
        static void ParkedVehicles()
        {
            var parking=Empty("Parked scooters");
            int i=0;
            foreach(int side in new[]{-1,1})for(float z=-97;z<98;z+=7.8f)
            {
                if(Mathf.Abs(z)<13)continue;
                var wrapper=Empty("Parked scooter "+i,parking.transform);wrapper.transform.position=route.Position(z,side*5.35f,.02f);wrapper.transform.rotation=route.Heading(z)*Quaternion.Euler(0,side*-55,0);
                var model=ScooterModel("Scooter125_LOD",wrapper.transform);
                var col=wrapper.AddComponent<BoxCollider>();col.center=new Vector3(0,.57f,0);col.size=new Vector3(.62f,1.05f,1.73f);Layer(wrapper,10);
                var lod=wrapper.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.013f,model.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();
                Color[] colors={Hex("#556267"),Hex("#85695C"),Hex("#C3C0A8"),Hex("#424E5B"),Hex("#733E38")};
                var variant=Material("ParkedPearl"+(i%colors.Length),colors[i%colors.Length],.4f,.55f);
                foreach(var renderer in model.GetComponentsInChildren<Renderer>()){var mats=renderer.sharedMaterials;for(int j=0;j<mats.Length;j++)if(mats[j].name=="Scooter_Pearl")mats[j]=variant;renderer.sharedMaterials=mats;}
                i++;
            }
            foreach(float z in new[]{-64f,43f,89f})
            {
                var wrapper=Empty("Parked sedan",parking.transform);wrapper.transform.position=route.Position(z,-5.12f,.015f);wrapper.transform.rotation=route.Heading(z)*Quaternion.Euler(0,180,0);
                Model("Sedan",wrapper.transform,Vector3.zero,Quaternion.identity);var col=wrapper.AddComponent<BoxCollider>();col.center=new Vector3(0,.73f,0);col.size=new Vector3(1.78f,1.45f,4.52f);Layer(wrapper,10);
            }
        }
        static void Lighting()
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=Hex("#B9CDD0");RenderSettings.ambientEquatorColor=Hex("#9EA69C");RenderSettings.ambientGroundColor=Hex("#777466");RenderSettings.ambientIntensity=1;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=Hex("#B9C6C7");RenderSettings.fogStartDistance=100;RenderSettings.fogEndDistance=230;
            var sun=Empty("Overcast sun").AddComponent<Light>();sun.type=LightType.Directional;sun.color=Hex("#FFF0D8");sun.intensity=1.6f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.35f;sun.shadowBias=.04f;sun.shadowNormalBias=.25f;sun.transform.rotation=Quaternion.Euler(47,-35,0);RenderSettings.sun=sun;
            var sky=Material("OvercastSky");sky.shader=Shader.Find("TaiwanRider/OvercastSky");sky.SetFloat("_SunDisk",1);sky.SetFloat("_SunSize",.025f);sky.SetColor("_SkyTint",Hex("#969E9F"));sky.SetColor("_GroundColor",Hex("#858E83"));sky.SetFloat("_AtmosphereThickness",1.1f);sky.SetFloat("_Exposure",.95f);RenderSettings.skybox=sky;
            var volume=Empty("Photographic tone").AddComponent<Volume>();volume.isGlobal=true;
            string path=Root+"/Data/DaylightVolume.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            foreach(var c in profile.components.ToArray())UnityEngine.Object.DestroyImmediate(c,true);profile.components.Clear();
            var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            var grading=profile.Add<ColorAdjustments>(true);grading.postExposure.Override(.25f);grading.contrast.Override(7);grading.saturation.Override(-8);
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(1.4f);bloom.intensity.Override(.07f);
            foreach(var c in profile.components)AssetDatabase.AddObjectToAsset(c,profile);volume.sharedProfile=profile;
            for(int j=0;j<5;j++)
            {
                var probe=Empty("Street reflection "+j).AddComponent<ReflectionProbe>();probe.transform.position=route.Position(-80+j*40,0,2);probe.size=new Vector3(30,22,49);probe.boxProjection=true;probe.resolution=128;probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;probe.cullingMask=~(1<<8);
            }
        }
        [MenuItem("Taiwan Rider/Build Gongguan scene")]
        public static void BuildScene()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before authoring the scene.");
            if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("The current scene has unsaved changes. Save it before generating a scene.");
            foreach(string folder in new[]{"Materials","Meshes","Scenes","Data","Prefabs"})Ensure(Root+"/"+folder);
            AssetDatabase.Refresh();materials.Clear();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            route=Empty("OSM route • source way 316698035").AddComponent<RoadRoute>();
            route.points=JsonUtility.FromJson<Survey>(File.ReadAllText(Root+"/Data/RouteSurvey.json")).points;
            street=Empty("Street geometry").transform;Streets();
            var buildings=Model("GongguanBuildings",null,Vector3.zero,Quaternion.identity);
            // Convert source model forward orientation once, matching the imported wheel orientation.
            var probeModel=Model("Scooter125_LOD",null,Vector3.zero,Quaternion.identity);
            bool reverse=Find(probeModel,"FrontWheel").position.z<Find(probeModel,"RearWheel").position.z;
            UnityEngine.Object.DestroyImmediate(probeModel);
            if(reverse)buildings.transform.rotation=Quaternion.Euler(0,180,0)*buildings.transform.rotation;
            foreach(var t in buildings.GetComponentsInChildren<Transform>())t.gameObject.isStatic=true;
            var layout=JsonUtility.FromJson<Buildings>("{\"items\":"+File.ReadAllText("Sources/building-layout.json")+"}");
            foreach(var b in layout.items)
            {
                var visual=Find(buildings,b.name);
                if(visual){visual.position+=Vector3.right*route.CenterX(b.center);visual.RotateAround(route.Position(b.center),Vector3.up,route.Heading(b.center).eulerAngles.y);}
                // Collision respects the arcade opening and the recessed shop wall.
                var go=Empty(b.name+" collision",street);go.transform.position=route.Position(b.center,b.side*12.1f,0);go.transform.rotation=route.Heading(b.center);
                var wall=go.AddComponent<BoxCollider>();wall.center=new Vector3(0,2,0);wall.size=new Vector3(3.7f,4,b.width-.12f);
            }
            ParkedVehicles();Lighting();var rider=Player();
            var session=Empty("Ride session").AddComponent<ScenarioRunner>();session.rider=rider;session.route=route;rider.session=session;
            session.startPosition=route.Position(-76,2.65f,.025f);rider.transform.position=session.startPosition;rider.transform.rotation=route.Heading(-76);
            session.font=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Art/Fonts/NotoSansTC.otf");
            session.car=Actor("Sedan",ScenarioKind.CarCutIn,new Vector3(5.15f,.02f,-6.5f));
            session.scooter=Actor("Scooter125_LOD",ScenarioKind.ScooterCrossing,new Vector3(13.05f,.02f,0));
            session.car.gameObject.SetActive(false);session.scooter.gameObject.SetActive(false);
            var defs=new List<ScenarioDefinition>();
            string[] titles={"日常試騎","留意汽車切入","留意巷口機車"};string[] descriptions={"沿汀州路騎乘，熟悉油門、煞車與車身寬度。","停放的汽車將駛入車道。提早減速，保留安全間距。","巷口機車可能進入你的路線。觀察遮擋後方的動態。"};
            for(int i=0;i<3;i++)
            {
                string path=Root+"/Data/Scenario"+i+".asset";var definition=AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(path);
                if(!definition){definition=ScriptableObject.CreateInstance<ScenarioDefinition>();AssetDatabase.CreateAsset(definition,path);}
                definition.kind=(ScenarioKind)i;definition.title=titles[i];definition.description=descriptions[i];definition.seed=125+i;definition.conflictZ=0;definition.triggerTimeToArrival=i==2?1f:1.1f;EditorUtility.SetDirty(definition);defs.Add(definition);
            }
            session.scenarios=defs.ToArray();
            var harness=Empty("Acceptance harness").AddComponent<RiderAcceptance>();harness.session=session;harness.route=route;
            Physics.gravity=new Vector3(0,-9.81f,0);Time.fixedDeltaTime=.02f;QualitySettings.shadowDistance=70;QualitySettings.shadows=UnityEngine.ShadowQuality.All;QualitySettings.vSyncCount=0;
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/GongguanRide.unity");AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Root+"/Scenes/GongguanRide.unity",true)};
            Selection.activeGameObject=rider.gameObject;
            Debug.Log("Gongguan riding scene built. "+scene.rootCount+" roots; metric OSM road centreline loaded.");
        }
        [MenuItem("Taiwan Rider/Apply gameplay update")]
        public static void ApplyGameplayUpdate()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=Root+"/Scenes/GongguanRide.unity" || scene.isDirty)
                throw new InvalidOperationException("Open the saved GongguanRide scene outside Play mode before applying the update.");
            var session=UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            Undo.RecordObject(session.rider.definition,"Raise scooter speed limit");
            session.rider.definition.topSpeedKph=100;EditorUtility.SetDirty(session.rider.definition);
            foreach(var definition in session.scenarios)
            {
                if(definition.kind==ScenarioKind.Cruise)continue;
                Undo.RecordObject(definition,"Shorten hazard warning");
                definition.triggerTimeToArrival=definition.kind==ScenarioKind.CarCutIn?1.1f:1f;
                EditorUtility.SetDirty(definition);
            }
            foreach(var actor in new[]{session.car,session.scooter})
            {
                var model=actor.transform.GetChild(0);
                Undo.RecordObject(model,"Align hazard vehicle nose");actor.AlignModelForward();
                PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Gameplay updated: forward-facing hazards, 100 km/h, 1.1 / 1.0 second hazard triggers.");
        }
        [MenuItem("Taiwan Rider/Prepare baked lighting")]
        public static void PrepareLighting()
        {
            route=UnityEngine.Object.FindAnyObjectByType<RoadRoute>();
            var importer=(ModelImporter)AssetImporter.GetAtPath(Root+"/Art/Models/GongguanBuildings.fbx");
            if(!importer.generateSecondaryUV){importer.generateSecondaryUV=true;importer.SaveAndReimport();}
            foreach(var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if(r.GetComponentInParent<ScooterController>() || r.GetComponentInParent<HazardActor>())continue;
                bool building=r.transform.root.name.StartsWith("GongguanBuildings");
                bool road=r.name=="Tingzhou metric road" || r.name.Contains("arcade paving");
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,building||road?StaticEditorFlags.BatchingStatic|StaticEditorFlags.ContributeGI:StaticEditorFlags.BatchingStatic);
                if(road){var mesh=r.GetComponent<MeshFilter>().sharedMesh;if(mesh.uv2.Length==0){Unwrapping.GenerateSecondaryUVSet(mesh);EditorUtility.SetDirty(mesh);}}
                if(building || road){r.receiveGI=ReceiveGI.Lightmaps;r.scaleInLightmap=1;}
            }
            string path=Root+"/Data/GongguanLighting.asset";
            var settings=AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if(!settings){settings=new LightingSettings();AssetDatabase.CreateAsset(settings,path);}
            settings.bakedGI=true;settings.realtimeGI=false;settings.lightmapper=LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution=4;settings.lightmapMaxSize=1024;settings.directSampleCount=32;settings.indirectSampleCount=64;settings.environmentSampleCount=64;settings.maxBounces=2;
            settings.mixedBakeMode=MixedLightingMode.IndirectOnly;
            settings.ao=true;settings.aoMaxDistance=1.2f;settings.aoExponentIndirect=1.2f;
            Lightmapping.lightingSettings=settings;RenderSettings.sun.lightmapBakeType=LightmapBakeType.Mixed;
            var existing=UnityEngine.Object.FindAnyObjectByType<LightProbeGroup>();
            var probes=existing?existing:Empty("Street light probes").AddComponent<LightProbeGroup>();
            var points=new List<Vector3>();for(float z=-104;z<=104;z+=12)foreach(float x in new[]{-8f,-3f,3f,8f})foreach(float y in new[]{1f,3f,6f})points.Add(route.Position(z,x,y));
            probes.probePositions=points.ToArray();EditorUtility.SetDirty(settings);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        [MenuItem("Taiwan Rider/Build Windows player")]
        public static void BuildWindows()
        {
            Ensure("Builds/RideReflex");
            PlayerSettings.productName="RideReflex";PlayerSettings.companyName="TaiwanRider";
            PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Root+"/Scenes/GongguanRide.unity"},locationPathName="Builds/RideReflex/RideReflex.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            var result=new {result=report.summary.result.ToString(),errors=report.summary.totalErrors,size=report.summary.totalSize,seconds=report.summary.totalTime.TotalSeconds};
            File.WriteAllText("Builds/build-summary.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Player build failed: "+report.summary.result);
            foreach(string file in new[]{"asset-manifest.json","THIRD_PARTY_NOTICES.txt"})if(File.Exists("Sources/"+file))File.Copy("Sources/"+file,"Builds/RideReflex/"+file,true);
            File.Copy(Root+"/Art/Fonts/OFL.txt","Builds/RideReflex/FONT_LICENSE.txt",true);
            Debug.Log("Windows player ready: Builds/RideReflex/RideReflex.exe");
        }
    }
}
