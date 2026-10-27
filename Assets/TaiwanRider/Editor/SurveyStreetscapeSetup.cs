using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    /// <summary>OSM plan geometry with explicitly estimated architectural infill.
    /// The footprint dataset is evidence, not an as-built 2025 photogrammetric survey.
    /// Storefront photography and the market have separate builders/provenance.</summary>
    public static class SurveyStreetscapeSetup
    {
        public const string RootName="Gongguan surveyed street • 2025 visual reconstruction";
        public const string CollisionRootName="Surveyed footprint collisions";
        public const string DataPath="Assets/TaiwanRider/Data/StreetSurvey.json";
        const string Art="Assets/TaiwanRider/Art/StreetSurvey";
        [Serializable] public class Survey { public Building[] buildings; public Road[] roads; }
        [Serializable] public class Building
        { public string id,name,buildingType; public Vector3[] points; public int levels,minimumLevels; public float heightMetres; public bool hasMeasuredHeight,isRoofOnly; public Edge[] roadFrontEdges; }
        [Serializable] public class Edge
        { public string roadId; public int edgeIndex; public Vector3 a,b,midpoint,outward; public float lengthMetres,distanceToCenterlineMetres; public bool unobstructed; }
        [Serializable] public class Road { public string id,name,highway; public Vector3[] points; }
        [Serializable] class Placement { public string name; public Vector3 before,after; public string reason; }
        [Serializable] class Report { public int buildingCount,knownLevels,unknownLevels,wallColliders,backdropBuildings,additionalStorefronts; public string limitations; public Placement[] parking; }
        [Serializable] public class FrontageReservations { public FrontageReservation[] groups; public VisualFrontageCut[] visualCuts; }
        [Serializable] public class VisualFrontageCut
        {
            public string shopId,buildingId;
            public int edgeIndex;
            public Vector3 center;
            public float width,height;
        }
        [Serializable] public class FrontageReservation
        {
            public string id,buildingId,sceneName,sourceFile;
            public int edgeIndex;
            public Vector3 center;
            public float width,height,visualHeight;
        }
        public const string ReservationsPath="Assets/TaiwanRider/Data/StorefrontReservations.json";
        static FrontageReservation[] extraFrontages=Array.Empty<FrontageReservation>();
        public static FrontageReservation[] LoadAdditionalFrontages() => File.Exists(ReservationsPath)
            ? JsonUtility.FromJson<FrontageReservations>(File.ReadAllText(ReservationsPath)).groups ?? Array.Empty<FrontageReservation>()
            : Array.Empty<FrontageReservation>();
        public static VisualFrontageCut[] LoadVisualCuts() => File.Exists(ReservationsPath)
            ? JsonUtility.FromJson<FrontageReservations>(File.ReadAllText(ReservationsPath)).visualCuts ?? Array.Empty<VisualFrontageCut>()
            : Array.Empty<VisualFrontageCut>();
        public static Building[] LoadFacadeBuildings()
        {
            const string backdropPath="Assets/TaiwanRider/Data/StreetBackdrop.json";
            var nearby=File.Exists(backdropPath)?JsonUtility.FromJson<Survey>(File.ReadAllText(backdropPath)).buildings:Array.Empty<Building>();
            return Load().buildings.Concat(nearby??Array.Empty<Building>()).GroupBy(b=>b.id).Select(g=>g.First()).ToArray();
        }
        public struct FrontageSpan { public float from,to,height; }
        static Survey survey;
        static Transform root,collisionRoot;
        static readonly Dictionary<string,Material> mats=new Dictionary<string,Material>();
        static readonly Dictionary<string,Geo> batches=new Dictionary<string,Geo>();
        static int meshCounter;
        sealed class Geo
        {
            public List<Vector3> v=new List<Vector3>(); public List<Vector2> uv=new List<Vector2>(); public List<int> t=new List<int>();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int n=v.Count;v.AddRange(new[]{a,b,c});var normal=Vector3.Cross(b-a,c-a).normalized;
                foreach(var p in new[]{a,b,c})uv.Add(Mathf.Abs(normal.y)>.7f?new Vector2(p.x,p.z):Mathf.Abs(normal.x)>.7f?new Vector2(p.z,p.y):new Vector2(p.x,p.y));
                t.AddRange(new[]{n,n+1,n+2});
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d) { Triangle(a,b,c);Triangle(a,c,d); }
        }
        public static Survey Load() => JsonUtility.FromJson<Survey>(File.ReadAllText(DataPath));
        static Geo G(string key) { if(!batches.TryGetValue(key,out var g))batches[key]=g=new Geo();return g; }
        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex,out var c);return c; }
        static void Material(string key,string color,float smooth=.18f,float metal=0)
        {
            string p=Art+"/"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,p);}
            m.SetColor("_BaseColor",C(color));m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",metal);m.enableInstancing=true;
            if(key=="Steel"||key=="DarkSteel"||key=="Shutter")
            {m.SetFloat("_ReceiveShadows",0);m.EnableKeyword("_RECEIVE_SHADOWS_OFF");}
            mats[key]=m;EditorUtility.SetDirty(m);
        }
        static void Materials()
        {
            Material("TileGrey","#92968C");Material("TileCream","#B2AE99");Material("TileRose","#9D8D82");Material("TileGreen","#8C9990");
            Material("Concrete","#858981");Material("Joint","#505A53");Material("Pavement","#83877E");Material("Roof","#7C827A");
            Material("GlassDark","#354A48",.54f,.18f);Material("GlassLight","#63746D",.48f,.12f);Material("Curtain","#B0AD99");
            Material("Steel","#9DA6A0",.36f,.45f);Material("DarkSteel","#48544D",.28f,.4f);Material("Shutter","#91988E",.27f,.3f);
            Material("AC","#BCBBA9",.21f,.18f);Material("Asphalt","#515851",.12f);Material("Paint","#C4C1A9",.11f);Material("CurbRed","#984F46",.12f);Material("Yellow","#BAA64A",.12f);
            const int n=256;var tex=new Texture2D(n,n,TextureFormat.RGB24,true);var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                bool grout=x%32<2||y%16<2;
                float a=grout?.66f:.94f+Mathf.PerlinNoise(x*.071f,y*.16f)*.06f;
                a-=Mathf.PerlinNoise(x*.011f,y*.009f)*.06f;
                pixels[y*n+x]=new Color(a,a,a);
            }
            tex.SetPixels(pixels);tex.Apply();string path=Art+"/OriginalCeramicTile.png";File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;importer.SaveAndReimport();
            foreach(string key in new[]{"TileGrey","TileCream","TileRose","TileGreen"})
            {mats[key].SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mats[key].SetTextureScale("_BaseMap",new Vector2(.7f,.7f));}
            var pavement=new Texture2D(n,n,TextureFormat.RGB24,true);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                bool joint=x%64<1||y%64<1;float grain=Mathf.PerlinNoise(x*.49f,y*.49f)*.055f;
                float damp=Mathf.PerlinNoise(x*.025f,y*.025f)*.11f;float a=joint?.71f:.93f+grain-damp;
                pixels[y*n+x]=new Color(a,a,a);
            }
            pavement.SetPixels(pixels);pavement.Apply();path=Art+"/OriginalPavingGrain.png";File.WriteAllBytes(path,pavement.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pavement);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;importer.SaveAndReimport();
            mats["Pavement"].SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mats["Pavement"].SetTextureScale("_BaseMap",new Vector2(.6f,.6f));
        }
        static void Box(string key,Vector3 p,Vector3 size,Quaternion? orientation=null)
        {
            var q=orientation??Quaternion.identity;var h=size*.5f;var g=G(key);
            Vector3 P(float x,float y,float z)=>p+q*Vector3.Scale(new Vector3(x,y,z),h);
            g.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));g.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            g.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));g.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            g.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));g.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Flush(Transform parent,string prefix)
        {
            foreach(var item in batches.OrderBy(i=>i.Key))
            {
                if(item.Value.v.Count==0)continue;
                string p=Art+"/"+prefix+"_"+item.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,p);}mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(item.Value.v);mesh.SetUVs(0,item.Value.uv);mesh.SetTriangles(item.Value.t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);
                var obj=new GameObject(item.Key+" • estimated architectural infill"){layer=2};obj.transform.SetParent(parent,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var r=obj.AddComponent<MeshRenderer>();r.sharedMaterial=mats[item.Key];
                if(item.Key=="Steel"||item.Key=="DarkSteel"||item.Key=="Shutter")
                {r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
                GameObjectUtility.SetStaticEditorFlags(obj,StaticEditorFlags.BatchingStatic);meshCounter++;
            }
            batches.Clear();
        }
        // Ear clipping handles concave OSM polygons. No convex-hull replacement or centroid fan.
        static float Cross(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        static bool InTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)=>Cross(a,b,p)>=-.00001f&&Cross(b,c,p)>=-.00001f&&Cross(c,a,p)>=-.00001f;
        static void Roof(Vector3[] points,float height)
        {
            var ids=Enumerable.Range(0,points.Length).ToList();int guard=points.Length*points.Length;
            while(ids.Count>2&&guard-->0)
            {
                bool cut=false;
                for(int i=0;i<ids.Count;i++)
                {
                    int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];
                    if(Cross(points[a],points[b],points[c])<=.00001f)continue;
                    if(ids.Any(j=>j!=a&&j!=b&&j!=c&&InTriangle(points[j],points[a],points[b],points[c])))continue;
                    G("Roof").Triangle(points[a]+Vector3.up*height,points[c]+Vector3.up*height,points[b]+Vector3.up*height);
                    ids.RemoveAt(i);cut=true;break;
                }
                if(!cut)throw new InvalidOperationException("Cannot triangulate a surveyed building footprint; refusing a false convex roof.");
            }
        }
        public static bool Inside(Vector3 p,Vector3[] polygon)
        {
            bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {var a=polygon[i];var b=polygon[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;}return inside;
        }
        static bool Overlap(Vector3[] a,Vector3[] b)
        {
            if(a.Any(p=>Inside(p,b))||b.Any(p=>Inside(p,a)))return true;
            for(int i=0;i<a.Length;i++)for(int j=0;j<b.Length;j++)
            {var a0=a[i];var a1=a[(i+1)%a.Length];var b0=b[j];var b1=b[(j+1)%b.Length];
                if(Cross(a0,a1,b0)*Cross(a0,a1,b1)<-.000001f&&Cross(b0,b1,a0)*Cross(b0,b1,a1)<-.000001f)return true;}
            return false;
        }
        static Vector3[][] VehiclePolygons(Transform vehicle,Vector3 position,Quaternion rotation)
        {
            var change=rotation*Quaternion.Inverse(vehicle.rotation);
            return vehicle.GetComponentsInChildren<BoxCollider>().Where(c=>c.enabled).Select(box=>
            {
                var corners=new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
                return corners.Select(v=>position+change*(box.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f+new Vector3(.08f,0,.08f),v))-vehicle.position)).ToArray();
            }).ToArray();
        }
        public static float EstimatedHeight(Building b)
        { if(b.hasMeasuredHeight&&b.heightMetres>0)return b.heightMetres;return 3.7f+Mathf.Max(0,(b.levels>0?b.levels:Mathf.Max(3,b.minimumLevels))-1)*3.15f; }
        public static FrontageSpan[] FacadeSpans(Building b,int edge,FrontageReservation[] additional)
        {
            var a=b.points[edge];var delta=b.points[(edge+1)%b.points.Length]-a;float length=delta.magnitude;
            var direction=delta.normalized;var spans=new List<FrontageSpan>();
            bool tingzhou=(b.roadFrontEdges??Array.Empty<Edge>()).Any(e=>e.edgeIndex==edge&&e.roadId=="316698035");
            if(tingzhou && (b.id=="317180219"||b.id=="1287937528"||b.id=="438606733"))
            {
                float width=b.id=="317180219"?11f:b.id=="1287937528"?8.1f:13.25f;
                float height=b.id=="317180219"?6.9f:b.id=="1287937528"?3.93f:7.54f;
                float centreAlong=length*.5f;
                if(b.id!="317180219")
                {
                    float targetZ=b.id=="1287937528"?59.85f:12.31f;
                    centreAlong=Mathf.Clamp((targetZ-a.z)/direction.z,width*.5f+.03f,length-width*.5f-.03f);
                }
                spans.Add(new FrontageSpan{from=centreAlong-width*.5f,to=centreAlong+width*.5f,height=height});
            }
            foreach(var item in additional.Where(r=>r.buildingId==b.id&&r.edgeIndex==edge))
            {
                float along=Vector3.Dot(item.center-a,direction);
                if(item.width<=0||item.height<=0||along-item.width*.5f<-.06f||along+item.width*.5f>length+.06f)
                    throw new InvalidOperationException("Invalid storefront reservation: "+item.id+" on "+b.id);
                spans.Add(new FrontageSpan{from=Mathf.Max(0,along-item.width*.5f),to=Mathf.Min(length,along+item.width*.5f),height=item.height});
            }
            // A photographed shop can straddle an OSM footprint gap. Only its documented
            // overlap removes generic facade panels; this never creates a new footprint.
            foreach(var item in LoadVisualCuts().Where(r=>r.buildingId==b.id&&r.edgeIndex==edge))
            {
                float along=Vector3.Dot(item.center-a,direction);
                if(!additional.Any(r=>r.id==item.shopId)||item.width<=0||item.height<=0||along-item.width*.5f<-.06f||along+item.width*.5f>length+.06f)
                    throw new InvalidOperationException("Invalid partial facade cut: "+item.shopId+" on "+b.id);
                spans.Add(new FrontageSpan{from=Mathf.Max(0,along-item.width*.5f),to=Mathf.Min(length,along+item.width*.5f),height=item.height});
            }
            return spans.OrderBy(s=>s.from).ToArray();
        }
        static float ReservedHeight(FrontageSpan[] spans,float from,float to) => spans
            .Where(s=>s.to>from+.001f&&s.from<to-.001f).Select(s=>s.height).DefaultIfEmpty(0).Max();
        static void WallCollider(Transform parent,Vector3 a,Vector3 b,float height,Vector3 inset)
        {
            var go=new GameObject("Ground wall • OSM edge with estimated arcade setback");go.transform.SetParent(parent,false);
            go.transform.position=(a+b)*.5f+inset+Vector3.up*(height*.5f);go.transform.rotation=Quaternion.LookRotation(b-a);
            go.AddComponent<BoxCollider>().size=new Vector3(.18f,height,Vector3.Distance(a,b));
        }
        static void Window(Vector3 p,Quaternion q,float width,float height,int seed)
        {
            Vector3 P(float x,float y,float z)=>p+q*new Vector3(x,y,z);
            Box(seed%5==0?"GlassLight":"GlassDark",p,new Vector3(width,height,.04f),q);
            if(seed%7==0)Box("Curtain",P(-width*.20f,0,.025f),new Vector3(width*.33f,height-.04f,.022f),q);
            foreach(float x in new[]{-width*.5f,0,width*.5f})Box("Steel",P(x,0,.065f),new Vector3(.042f,height+.065f,.055f),q);
            foreach(float y in new[]{-height*.5f,height*.5f})Box("Steel",P(0,y,.065f),new Vector3(width+.07f,.047f,.065f),q);
            Box("Concrete",P(0,-height*.5f-.065f,.15f),new Vector3(width+.16f,.10f,.36f),q);
            if(seed%4==0)
            {
                for(int j=0;j<7;j++)Box("Steel",P(-width*.5f+width*j/6,0,.29f),new Vector3(.023f,height,.025f),q);
                Box("Steel",P(0,height*.1f,.29f),new Vector3(width,.03f,.032f),q);
            }
            if(seed%3==0)
            {
                Box("AC",P(width*.23f,-height*.5f-.35f,.28f),new Vector3(.72f,.44f,.42f),q);
                Box("DarkSteel",P(width*.23f,-height*.5f-.35f,.505f),new Vector3(.54f,.31f,.018f),q);
                for(int j=0;j<6;j++)Box("Steel",P(width*.23f,-height*.5f-.47f+j*.048f,.52f),new Vector3(.54f,.018f,.019f),q);
            }
        }
        static void BuildBuilding(Building b,int index)
        {
            var group=new GameObject("OSM "+b.id+" • "+b.name+" • "+(b.levels>0?b.levels+" mapped levels":"estimated height; floors unknown")){layer=2};group.transform.SetParent(root,false);
            var collision=new GameObject("OSM "+b.id+" footprint walls");collision.transform.SetParent(collisionRoot,false);
            if(b.isRoofOnly) { group.name+=" • open roof; no enclosed mass"; return; }
            float height=b.id=="232744652"?38.2f:EstimatedHeight(b);
            string wall=new[]{"TileGrey","TileCream","TileRose","TileGreen"}[index%4];
            for(int i=0;i<b.points.Length;i++)
            {
                Vector3 a=b.points[i],z=b.points[(i+1)%b.points.Length],delta=z-a;float length=delta.magnitude;
                Vector3 outward=new Vector3(delta.z,0,-delta.x).normalized;var q=Quaternion.LookRotation(outward);Vector3 centre=(a+z)*.5f;
                bool front=b.roadFrontEdges.Any(e=>e.edgeIndex==i&&e.unobstructed&&e.distanceToCenterlineMetres<27);
                var spans=FacadeSpans(b,i,extraFrontages);float setback=front?1.8f:0;
                WallCollider(collision.transform,a,z,3.2f,-outward*setback);
                if(b.id=="232744652")continue; // landmark builder owns all visible market geometry
                // Split the facade into intervals, so adjacent shops can reserve independent
                // widths and heights without opening an entire building or overlapping a neighbour.
                var cuts=new[]{0f,length}.Concat(spans.SelectMany(s=>new[]{s.from,s.to})).Distinct().OrderBy(v=>v).ToArray();
                for(int part=1;part<cuts.Length;part++)
                {
                    float start=cuts[part-1],end=cuts[part],spanLength=end-start;if(spanLength<.002f)continue;
                    float crop=ReservedHeight(spans,start,end),lower=crop>0?crop:3.3f;
                    Vector3 piece=a+delta.normalized*((start+end)*.5f);
                    if(height>lower+.001f)Box(wall,piece+Vector3.up*((height+lower)*.5f),new Vector3(spanLength,height-lower,.18f),q);
                    if(crop>0)continue;
                    Box(wall,piece-outward*setback+Vector3.up*1.66f,new Vector3(spanLength,3.32f,.18f),q);
                    if(front&&spanLength>2.7f)
                    {
                        int bays=Mathf.Max(1,Mathf.FloorToInt(spanLength/3.3f));float bay=spanLength/bays;
                        for(int j=0;j<bays;j++)
                        {
                            var p=a+delta.normalized*(start+(j+.5f)*bay)-outward*(setback-.11f)+Vector3.up*1.44f;
                            Box("Shutter",p,new Vector3(bay-.45f,2.4f,.045f),q);
                            for(int rib=0;rib<19;rib++)Box("Steel",p+Vector3.up*(-1.1f+rib*.12f)+outward*.035f,new Vector3(bay-.48f,.018f,.034f),q);
                        }
                    }
                    if(front)
                    {
                        Box("Concrete",piece-outward*.6f+Vector3.up*3.25f,new Vector3(spanLength,.23f,2.0f),q);
                        int columns=Mathf.Max(1,Mathf.RoundToInt(spanLength/4.2f));
                        for(int j=0;j<=columns;j++)
                        {
                            float along=start+spanLength*j/columns;
                            // Do not let a half-column protrude across a shop's reserved edge.
                            if(ReservedHeight(spans,along-.16f,along+.16f)>0)continue;
                            var p=a+delta.normalized*along-outward*.3f+Vector3.up*1.67f;Box(wall,p,new Vector3(.3f,3.15f,.38f),q);
                        }
                    }
                }
                Box("Concrete",centre+Vector3.up*(height+.18f),new Vector3(length+.04f,.36f,.24f),q);
                int floors=b.levels>0?b.levels:Mathf.Max(3,b.minimumLevels);
                int windows=Mathf.FloorToInt(length/2.65f);float unit=length/Mathf.Max(1,windows);
                for(int floor=1;floor<floors;floor++)
                {
                    float cy=3.7f+(floor-.5f)*3.15f;
                    for(int w=0;w<windows;w++)
                    {
                        float along=(w+.5f)*unit,width=Mathf.Min(1.8f,unit-.55f);
                        if(cy-1.42f<ReservedHeight(spans,along-width*.5f-.1f,along+width*.5f+.1f))continue;
                        Window(a+delta.normalized*along+outward*.12f+Vector3.up*cy,q,width,1.65f,index*37+i*19+floor*11+w);
                    }
                    float bandY=3.7f+floor*3.15f-.12f;
                    for(int part=1;part<cuts.Length;part++)
                        if(bandY-.05f>=ReservedHeight(spans,cuts[part-1],cuts[part]))
                            Box("Concrete",a+delta.normalized*((cuts[part-1]+cuts[part])*.5f)+Vector3.up*bandY+outward*.1f,new Vector3(cuts[part]-cuts[part-1],.10f,.16f),q);
                }
            }
            if(b.id=="232744652")return;
            Roof(b.points,height);Flush(group.transform,"B"+b.id);
        }
        static void RoadStrip(Vector3[] points,float width,string name,float y,Transform parent,string assetKey)
        {
            for(int i=1;i<points.Length;i++)
            {
                Vector3 a=points[i-1]+Vector3.up*y,b=points[i]+Vector3.up*y,n=Vector3.Cross(Vector3.up,b-a).normalized*width*.5f;
                G("Asphalt").Quad(a-n,b-n,b+n,a+n);
            }
            var go=new GameObject(name){layer=2};go.transform.SetParent(parent,false);Flush(go.transform,assetKey);
        }
        static List<Vector3> ClipHalfPlane(List<Vector3> input,Vector3 a,Vector3 b,bool keepInside)
        {
            var result=new List<Vector3>();if(input.Count==0)return result;
            for(int i=0;i<input.Count;i++)
            {
                var p=input[i];var q=input[(i+1)%input.Count];float cp=Cross(a,b,p),cq=Cross(a,b,q);
                bool ip=keepInside?cp>=0:cp<=0,iq=keepInside?cq>=0:cq<=0;
                if(ip)result.Add(p);if(ip!=iq)result.Add(Vector3.Lerp(p,q,cp/(cp-cq)));
            }
            return result;
        }
        static void DistrictGroundWithBookstoreOpening()
        {
            // Exact transformed opening from the independently authored entrance module.
            // Only the visual surface is cut: riding does not include entering the basement.
            var hole=new[]{new Vector3(-32.435847f,0,-100.866439f),new Vector3(-33.296207f,0,-104.197112f),new Vector3(-31.185491f,0,-104.742340f),new Vector3(-30.325130f,0,-101.411668f)};
            if(Cross(hole[0],hole[1],hole[2])<0)Array.Reverse(hole);
            var remaining=new List<Vector3>{new Vector3(-160,0,-260),new Vector3(160,0,-260),new Vector3(160,0,260),new Vector3(-160,0,260)};
            for(int i=0;i<hole.Length;i++)
            {
                var outside=ClipHalfPlane(remaining,hole[i],hole[(i+1)%hole.Length],false);
                for(int t=1;t+1<outside.Count;t++)G("Pavement").Triangle(outside[0]-Vector3.up*.0225f,outside[t+1]-Vector3.up*.0225f,outside[t]-Vector3.up*.0225f);
                remaining=ClipHalfPlane(remaining,hole[i],hole[(i+1)%hole.Length],true);
            }
        }
        static void StreetGround(RoadRoute route)
        {
            // Ground at y=0 is separate from visible architecture so no hidden walls block radar.
            var ground=new GameObject("Survey district ground support");ground.transform.SetParent(root,false);ground.transform.position=new Vector3(0,-.16f,0);
            ground.AddComponent<BoxCollider>().size=new Vector3(320,.28f,520);
            DistrictGroundWithBookstoreOpening();Flush(root,"DistrictGround");
            var street=GameObject.Find("Street geometry");
            var originalRoad=street?street.transform.Find("Tingzhou metric road"):null;
            if(originalRoad&&originalRoad.GetComponent<Renderer>())mats["Asphalt"]=originalRoad.GetComponent<Renderer>().sharedMaterial;
            if(street)foreach(Transform t in street.transform)
            {
                if(t.name.StartsWith("Building_")&&t.name.EndsWith(" collision"))t.gameObject.SetActive(false);
                if(t.name.Contains("arcade paving")||t.name=="Lane 90")t.gameObject.SetActive(false);
                if((t.name=="Stone curb"||t.name=="Red curb marking")&&Mathf.Abs(t.position.z)<7)t.gameObject.SetActive(false);
                // Old parking bay, signpost and cable locations were procedural. Retain paved road markings.
                if(t.name.Contains("Overhead service cable"))t.gameObject.SetActive(false);
            }
            foreach(var r in survey.roads.Where(r=>r.id!="316698035"))RoadStrip(r.points,r.id=="59683808"?7f:9f,r.name+" • mapped centreline; width estimated",.016f,root,"Road"+r.id);
            // Raise paving only outside the existing riding surface. The unmeasured widths remain explicit.
            for(int side=-1;side<=1;side+=2)for(float z=-120;z<120;z+=1.5f)
            {
                if(Mathf.Abs(z)<6)continue;
                var a=route.Position(z,side*6.3f,.095f);var b=route.Position(z+1.5f,side*6.3f,.095f);
                var c=route.Position(z+1.5f,side*10.7f,.095f);var d=route.Position(z,side*10.7f,.095f);
                if(side>0)G("Pavement").Quad(a,b,c,d);else G("Pavement").Quad(a,d,c,b);
            }
            Flush(root,"Paving");
        }
        static void Backdrop()
        {
            const string path="Assets/TaiwanRider/Data/StreetBackdrop.json";if(!File.Exists(path))return;
            var data=JsonUtility.FromJson<Survey>(File.ReadAllText(path));
            var context=new GameObject("Surrounding mapped context • facade and height estimates"){layer=2};context.transform.SetParent(root,false);
            foreach(var b in data.buildings)
            {
                if(survey.buildings.Any(existing=>existing.id==b.id))continue;
                float h=b.isRoofOnly?3.3f:EstimatedHeight(b);
                for(int i=0;i<b.points.Length;i++)
                {
                    Vector3 a=b.points[i],c=b.points[(i+1)%b.points.Length],d=c-a;float width=d.magnitude;
                    var normal=new Vector3(d.z,0,-d.x).normalized;var q=Quaternion.LookRotation(normal);
                    var spans=FacadeSpans(b,i,extraFrontages);
                    var cuts=new[]{0f,width}.Concat(spans.SelectMany(s=>new[]{s.from,s.to})).Distinct().OrderBy(v=>v).ToArray();
                    if(!b.isRoofOnly)for(int part=1;part<cuts.Length;part++){
                        float from=cuts[part-1],to=cuts[part],crop=ReservedHeight(spans,from,to);
                        if(to-from>.002f&&h>crop)Box("TileGrey",a+d.normalized*((from+to)*.5f)+Vector3.up*((h+crop)*.5f),new Vector3(to-from,h-crop,.16f),q);
                    }
                    int count=Mathf.FloorToInt(width/3.4f);
                    if(!b.isRoofOnly)for(float y=5.2f;y<h-1;y+=3.15f)for(int j=0;j<count;j++)
                    {
                        float along=width*(j+.5f)/count;
                        if(y-.75f>=ReservedHeight(spans,along-.75f,along+.75f))
                            Box("GlassDark",Vector3.Lerp(a,c,(j+.5f)/count)+normal*.12f+Vector3.up*y,new Vector3(1.5f,1.5f,.035f),q);
                    }
                }
                Roof(b.points,h);
            }
            Flush(context.transform,"BackdropBuildings");
            foreach(var road in data.roads??new Road[0])
            {
                if(survey.roads.Any(r=>r.id==road.id))continue;
                float width=road.highway=="primary"||road.highway=="secondary"?18f:road.highway=="tertiary"?10f:6.5f;
                RoadStrip(road.points,width,road.name+" • surrounding mapped road; estimated width",.007f,context.transform,"ContextRoad"+road.id);
            }
            var main=survey.roads.First(r=>r.id=="316698035");
            for(int i=1;i<main.points.Length;i++)foreach(int side in new[]{-1,1})
            {
                Vector3 a=main.points[i-1],b=main.points[i];float low=side<0?-215:120,high=side<0?-120:215;
                if(Mathf.Max(a.z,b.z)<low||Mathf.Min(a.z,b.z)>high)continue;
                if(Mathf.Abs(b.z-a.z)<.001f)continue;
                float t0=Mathf.Clamp01((low-a.z)/(b.z-a.z)),t1=Mathf.Clamp01((high-a.z)/(b.z-a.z));
                var start=Vector3.Lerp(a,b,Mathf.Min(t0,t1));var end=Vector3.Lerp(a,b,Mathf.Max(t0,t1));
                var n=Vector3.Cross(Vector3.up,end-start).normalized;
                G("Asphalt").Quad(start-n*6.2f,end-n*6.2f,end+n*6.2f,start+n*6.2f);
                foreach(float offset in new[]{-.12f,.12f})
                {
                    var p=start+n*offset+Vector3.up*.012f;var q=end+n*offset+Vector3.up*.012f;
                    G("Yellow").Quad(p-n*.035f,q-n*.035f,q+n*.035f,p+n*.035f);
                }
            }
            Flush(context.transform,"MainRoadContext");
        }
        static Placement[] Parking(RoadRoute route)
        {
            var parking=GameObject.Find("Parked scooters");var changes=new List<Placement>();if(!parking)return changes.ToArray();
            foreach(Transform t in parking.transform)
            {
                Vector3 before=t.position;float side=Mathf.Sign(before.x-route.CenterX(before.z));bool car=t.name.ToLowerInvariant().Contains("sedan");
                // All parked models and their local colliders move together; movingTraffic is untouched.
                bool Hits(Vector3 p,Quaternion q)=>VehiclePolygons(t,p,q).Any(shape=>survey.buildings.Where(b=>!b.isRoofOnly).Any(b=>Overlap(shape,b.points)));
                if(Hits(before,t.rotation)||Mathf.Abs(before.z)<10)
                {
                    bool placed=false;
                    for(int delta=0;delta<70&&!placed;delta++)foreach(int direction in new[]{1,-1})
                    {
                        float z=before.z+delta*direction*1.8f;if(Mathf.Abs(z)<12||Mathf.Abs(z)>110)continue;
                        var p=route.Position(z,side*(car?4.7f:5f),before.y);
                        var q=route.Heading(z)*Quaternion.Inverse(route.Heading(before.z))*t.rotation;
                        var polygons=VehiclePolygons(t,p,q);
                        if(Hits(p,q)||parking.transform.Cast<Transform>().Any(other=>other!=t&&Vector3.Distance(other.position,p)<7&&VehiclePolygons(other,other.position,other.rotation).Any(otherPoly=>polygons.Any(poly=>Overlap(poly,otherPoly)))))continue;
                        t.position=p;t.rotation=q;placed=true;break;
                    }
                    if(!placed)throw new InvalidOperationException("No valid roadside placement for "+t.name);
                }
                if(Vector3.Distance(before,t.position)>.01f)changes.Add(new Placement{name=t.name,before=before,after=t.position,reason="Clear mapped footprint and junction"});
            }
            var old=GameObject.Find("Refined parking markings");if(old)old.SetActive(false);
            foreach(Transform t in parking.transform)
            {
                bool car=t.name.ToLowerInvariant().Contains("sedan");var q=route.Heading(t.position.z);float width=car?2.1f:2.45f,length=car?5.1f:1.45f;
                foreach(float s in new[]{-1f,1f})
                {
                    Box("Paint",t.position+q*new Vector3(s*width*.5f,.022f,0),new Vector3(.045f,.013f,length),q);
                    Box("Paint",t.position+q*new Vector3(0,.022f,s*length*.5f),new Vector3(width,.013f,.045f),q);
                }
            }
            Flush(root,"AdjustedParkingPaint");return changes.ToArray();
        }
        public static Vector3 PhotoAnchor {get;private set;}
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!="Assets/TaiwanRider/Scenes/GongguanRide.unity")throw new InvalidOperationException("Open GongguanRide outside Play mode.");
            survey=Load();if(survey.buildings==null||survey.buildings.Length<20)throw new InvalidOperationException("Missing complete street survey.");
            extraFrontages=LoadAdditionalFrontages();
            var facadeBuildings=LoadFacadeBuildings();
            foreach(var reservation in extraFrontages.Where(r=>!string.IsNullOrEmpty(r.buildingId)))
            {
                var host=facadeBuildings.SingleOrDefault(b=>b.id==reservation.buildingId);
                if(host==null||reservation.edgeIndex<0||reservation.edgeIndex>=host.points.Length)
                    throw new InvalidOperationException("Missing storefront host edge: "+reservation.id);
                FacadeSpans(host,reservation.edgeIndex,extraFrontages); // validate before mutating scene
            }
            foreach(var cut in LoadVisualCuts())
            {
                var host=facadeBuildings.SingleOrDefault(b=>b.id==cut.buildingId);
                if(host==null||cut.edgeIndex<0||cut.edgeIndex>=host.points.Length)
                    throw new InvalidOperationException("Missing partial facade cut host: "+cut.shopId);
                FacadeSpans(host,cut.edgeIndex,extraFrontages);
            }
            var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();if(!route)throw new InvalidOperationException("Missing ride route.");
            Directory.CreateDirectory(Art);AssetDatabase.Refresh();batches.Clear();mats.Clear();meshCounter=0;Materials();
            foreach(string name in new[]{RootName,CollisionRootName}) {var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==name);if(old)UnityEngine.Object.DestroyImmediate(old);}
            foreach(string name in new[]{"GongguanBuildings",IntersectionDetailSetup.RootName,StreetLifeSetup.RootName,SiteMatchSetup.RootName,SiteLaneSetup.RootName})
            {var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==name);if(old)old.SetActive(false);}
            root=new GameObject(RootName).transform;root.gameObject.layer=2;collisionRoot=new GameObject(CollisionRootName).transform;
            Placement[] parking;
            AssetDatabase.StartAssetEditing();
            try
            {
                for(int i=0;i<survey.buildings.Length;i++)BuildBuilding(survey.buildings[i],i);
                StreetGround(route);parking=Parking(route);Backdrop();
            }
            finally{AssetDatabase.StopAssetEditing();}
            // Register the photographed three-shop group against an actual facade edge, not a business pin.
            var building=survey.buildings.Single(b=>b.id=="317180219");var edge=building.roadFrontEdges.Single(e=>e.roadId=="316698035");
            PhotoAnchor=edge.midpoint+edge.outward*.065f;SiteFacadeSetup.Build(root,PhotoAnchor,Quaternion.LookRotation(edge.outward));
            SurveyFacadeSetup.Build(root,Vector3.zero,Quaternion.identity);
            ShuiyuanMarketSetup.Build(root);
            NorthWestShopsSetup.Build(root);
            EastShopsSetup.Build(root);
            WalkShopsSetup.Build(root);
            NorthWestRetailSetup.Build(root);
            NorthEastShopsSetup.Build(root);
            MidEastShopsSetup.Build(root);
            MidConvenienceSetup.Build(root);
            MidWestRetailSetup.Build(root);
            SouthRetailSetup.Build(root);
            NorthEastInfillSetup.Build(root);
            MidWestInfillSetup.Build(root);
            NorthWestInfillSetup.Build(root);
            WestGapShopsSetup.Build(root);
            SouthContinuationSetup.Build(root);
            SportsContinuationSetup.Build(root);
            CinemaArcadeSetup.Build(root);
            LucyCornerSetup.Build(root);
            UnderArmourCornerSetup.Build(root);
            AnonymousGapSetup.Build(root);
            BrightNorthInfillSetup.Build(root);
            NorthSportsInfillSetup.Build(root);
            LaVillaFrontageSetup.Build(root);
            var report=new Report{buildingCount=survey.buildings.Length,knownLevels=survey.buildings.Count(b=>b.levels>0),unknownLevels=survey.buildings.Count(b=>b.levels==0),wallColliders=collisionRoot.GetComponentsInChildren<Collider>().Length,parking=parking,limitations="OSM plan geometry captured 2026-10-11, not independently verified as 2025 historical state. Heights without explicit metre data, arcade setbacks, all unphotographed windows and material colours are estimates. Missing OSM buildings remain unmapped. Ground width and parked vehicle placement are gameplay estimates, not photographic matches."};
            if(File.Exists("Assets/TaiwanRider/Data/StreetBackdrop.json"))report.backdropBuildings=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetBackdrop.json")).buildings.Length;
            report.additionalStorefronts=extraFrontages.Length;
            Directory.CreateDirectory("Validation/StreetSurvey");File.WriteAllText("Validation/StreetSurvey/implementation.json",JsonUtility.ToJson(report,true));
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);Debug.Log("Street survey built: "+survey.buildings.Length+" mapped footprints; "+meshCounter+" material batches. Height and unphotographed facade estimates remain labelled.");
        }
    }
}


