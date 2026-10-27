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
    /// <summary>
    /// Exterior study of Shuiyuan Building, from the late-2025 official artwork gallery.
    /// OSM way 232744652 supplies the unchanged footprint. The 40.3 m display height uses
    /// the official artwork extent, NOT a measured structural height. No collision is added.
    /// Sources and per-elevation confidence are recorded in Sources/market-evidence.json.
    /// </summary>
    public static class ShuiyuanMarketSetup
    {
        public const string RootName = "Shuiyuan Market • late-2025 exterior study • OSM footprint";
        public const string FootprintId = "232744652";
        public const float AssumedDisplayHeight = 40.3f;
        const string DataPath = "Assets/TaiwanRider/Data/StreetSurvey.json";
        const string ArtPath = "Assets/TaiwanRider/Art/StreetSurvey/ShuiyuanMarket";
        const string FontPath = "Assets/TaiwanRider/Art/Fonts/NotoSansTC.otf";
        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public string name; public Vector3[] points; public int levels; }
        struct Portal
        {
            public float centre,width,bottom,top,depth;
            public bool parking;
        }
        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
        }
        static readonly Dictionary<string,Batch> batches = new Dictionary<string,Batch>();
        static readonly Dictionary<string,Material> materials = new Dictionary<string,Material>();
        static Vector3 origin,east,north;
        static float minU,maxU,minV,maxV;
        static Transform root;
        static Vector3 P(float u,float y,float v) => east*u+Vector3.up*y+north*v-origin;
        static Vector3 N(float x,float z) => east*x+north*z;
        static Color Hex(string code) { ColorUtility.TryParseHtmlString(code,out var c); return c; }
        static Batch G(string key)
        { if(!batches.TryGetValue(key,out var b))batches[key]=b=new Batch();return b; }
        static void Quad(string key,Vector3 a,Vector3 b,Vector3 c,Vector3 d,int colorIndex=-1)
        {
            var g=G(key);int n=g.vertices.Count;g.vertices.AddRange(new[]{a,b,c,d});
            if(colorIndex>=0)
            {
                var uv=new Vector2((colorIndex+.5f)/64f,.5f);
                g.uv.AddRange(new[]{uv,uv,uv,uv});
            }
            else g.uv.AddRange(new[]{Vector2.zero,new Vector2(Vector3.Distance(a,b),0),new Vector2(Vector3.Distance(a,b),Vector3.Distance(b,c)),new Vector2(0,Vector3.Distance(b,c))});
            g.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        static void Panel(string key,Vector3 centre,Vector3 right,Vector3 normal,float width,float height,int colorIndex=-1)
        {
            Vector3 a=right*(width/2),b=Vector3.up*(height/2);
            // right x up must point outwards; keep one consistent exterior winding.
            if(Vector3.Dot(Vector3.Cross(right,Vector3.up),normal)<0)right=-right;
            a=right*(width/2);Quad(key,centre-a-b,centre+a-b,centre+a+b,centre-a+b,colorIndex);
        }
        static void Box(string key,Vector3 centre,Vector3 right,Vector3 forward,Vector3 size)
        {
            if(Vector3.Dot(Vector3.Cross(right,Vector3.up),forward)<0)right=-right;
            Vector3 a=right*size.x*.5f,b=Vector3.up*size.y*.5f,c=forward*size.z*.5f;
            Quad(key,centre-a-b-c,centre-a+b-c,centre+a+b-c,centre+a-b-c);
            Quad(key,centre-a-b+c,centre+a-b+c,centre+a+b+c,centre-a+b+c);
            Quad(key,centre-a-b-c,centre+a-b-c,centre+a-b+c,centre-a-b+c);
            Quad(key,centre-a+b-c,centre-a+b+c,centre+a+b+c,centre+a+b-c);
            Quad(key,centre-a-b-c,centre-a-b+c,centre-a+b+c,centre-a+b-c);
            Quad(key,centre+a-b-c,centre+a+b-c,centre+a+b+c,centre+a-b+c);
        }
        static void BoxUV(string key,float u,float y,float v,float width,float height,float depth)
        { Box(key,P(u,y,v),east,north,new Vector3(width,height,depth)); }
        static Material M(string key,string color,float metallic=0,float smoothness=.22f)
        {
            string path=ArtPath+"/"+key+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=key};AssetDatabase.CreateAsset(material,path);}
            material.name=key;material.SetColor("_BaseColor",Hex(color));material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smoothness);
            materials[key]=material;EditorUtility.SetDirty(material);return material;
        }
        static Building ReadFootprint()
        {
            if(!File.Exists(DataPath))throw new FileNotFoundException("StreetSurvey.json must be imported before the market study.",DataPath);
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText(DataPath));
            var building=survey?.buildings?.FirstOrDefault(b=>b.id==FootprintId);
            if(building?.points==null||building.points.Length<4)throw new InvalidOperationException("Water source market OSM footprint missing.");
            foreach(var p in building.points)if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.z)||float.IsInfinity(p.z)||p.y!=0)throw new InvalidOperationException("Market footprint must contain finite ground-level points.");
            return building;
        }
        static void SetFrame(Vector3[] points)
        {
            Vector3 longest=Vector3.zero;
            for(int i=0;i<points.Length;i++)
            {
                Vector3 d=points[(i+1)%points.Length]-points[i];
                if(Mathf.Abs(d.z)>Mathf.Abs(d.x)&&d.sqrMagnitude>longest.sqrMagnitude)longest=d;
            }
            if(longest.sqrMagnitude<1)throw new InvalidOperationException("Market north-south reference edge is missing.");
            north=longest.normalized;if(north.z<0)north=-north;east=Vector3.Cross(Vector3.up,north).normalized;
            minU=points.Min(p=>Vector3.Dot(p,east));maxU=points.Max(p=>Vector3.Dot(p,east));
            minV=points.Min(p=>Vector3.Dot(p,north));maxV=points.Max(p=>Vector3.Dot(p,north));
            origin=east*((minU+maxU)/2)+north*((minV+maxV)/2);
        }
        static float Cross2(Vector3 a,Vector3 b,Vector3 c) => (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        static List<int> Triangulate(Vector3[] points)
        {
            var pending=Enumerable.Range(0,points.Length).ToList();var result=new List<int>();
            float signedArea=0;for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];signedArea+=a.x*b.z-b.x*a.z;}
            if(signedArea<0)pending.Reverse();
            int safety=points.Length*points.Length;
            while(pending.Count>3&&safety-->0)
            {
                bool clipped=false;
                for(int j=0;j<pending.Count;j++)
                {
                    int a=pending[(j+pending.Count-1)%pending.Count],b=pending[j],c=pending[(j+1)%pending.Count];
                    if(Cross2(points[a],points[b],points[c])<=.00001f)continue;
                    bool occupied=false;
                    foreach(int test in pending)
                    {
                        if(test==a||test==b||test==c)continue;
                        if(Cross2(points[a],points[b],points[test])>=-.00001f&&Cross2(points[b],points[c],points[test])>=-.00001f&&Cross2(points[c],points[a],points[test])>=-.00001f){occupied=true;break;}
                    }
                    if(occupied)continue;
                    result.AddRange(new[]{a,c,b});pending.RemoveAt(j);clipped=true;break;
                }
                if(!clipped)throw new InvalidOperationException("Cannot triangulate market footprint; preserve the source instead of filling its concavities.");
            }
            if(pending.Count!=3)throw new InvalidOperationException("Market footprint triangulation failed.");
            result.AddRange(new[]{pending[0],pending[2],pending[1]});return result;
        }
        static void Roof(Vector3[] points,float y,string key)
        {
            var g=G(key);int n=g.vertices.Count;
            foreach(var p in points){g.vertices.Add(p-origin+Vector3.up*y);g.uv.Add(new Vector2(p.x,p.z));}
            g.indices.AddRange(Triangulate(points).Select(i=>i+n));
        }
        static void FootprintShell(Vector3[] points,float bottom,float top,string key)
        {
            float signedArea=0;for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];signedArea+=a.x*b.z-b.x*a.z;}
            for(int i=0;i<points.Length;i++)
            {
                Vector3 a=points[i]-origin,b=points[(i+1)%points.Length]-origin;
                if(signedArea<0){var t=a;a=b;b=t;}
                // Cut only the three photographed-style access openings into the existing
                // west wall plane. No polygon vertex or footprint placement is changed.
                if(key=="Cobalt"&&bottom<0&&Mathf.Abs(Vector3.Dot(a+origin,east)-minU)<.12f&&Mathf.Abs(Vector3.Dot(b+origin,east)-minU)<.12f)
                { WestGroundWall(a,b,bottom,top);continue; }
                Quad(key,a+Vector3.up*bottom,a+Vector3.up*top,b+Vector3.up*top,b+Vector3.up*bottom);
            }
        }
        static Portal[] WestPortals()
        {
            float low=minV+10.2f,high=maxV-10.4f,mid=(low+high)/2;
            return new[]{
                new Portal{centre=low+4.4f,width=4.3f,bottom=.76f,top=3.665f,depth=1.35f},
                new Portal{centre=high-4.4f,width=4.3f,bottom=.76f,top=3.665f,depth=1.35f},
                new Portal{centre=mid-4.5f,width=5.5f,bottom=-.04f,top=2.815f,depth=3.4f,parking=true}
            };
        }
        static void WestGroundWall(Vector3 a,Vector3 b,float bottom,float top)
        {
            float av=Vector3.Dot(a+origin,north),bv=Vector3.Dot(b+origin,north);
            var portals=WestPortals();var cuts=new List<float>{0,1};
            foreach(var p in portals)foreach(float v in new[]{p.centre-p.width/2,p.centre+p.width/2})
            {float t=(v-av)/(bv-av);if(t>0&&t<1)cuts.Add(t);}
            cuts.Sort();
            for(int i=1;i<cuts.Count;i++)
            {
                float t0=cuts[i-1],t1=cuts[i];if(t1-t0<.00001f)continue;
                Vector3 p=Vector3.Lerp(a,b,t0),q=Vector3.Lerp(a,b,t1);float centreV=Mathf.Lerp(av,bv,(t0+t1)/2);
                int opening=Array.FindIndex(portals,portal=>Mathf.Abs(centreV-portal.centre)<portal.width/2);
                void Strip(float y0,float y1)
                {if(y1-y0>.0001f)Quad("Cobalt",p+Vector3.up*y0,p+Vector3.up*y1,q+Vector3.up*y1,q+Vector3.up*y0);}
                if(opening<0)Strip(bottom,top);
                else{Strip(bottom,portals[opening].bottom);Strip(portals[opening].top,top);}
            }
        }
        static void RecessedPortal(Portal portal,float faceU)
        {
            float a=portal.centre-portal.width/2,b=portal.centre+portal.width/2;
            float floor=portal.parking?0f:portal.bottom,roof=portal.top,backU=faceU+portal.depth;
            float floorFrontU=portal.parking?faceU:faceU-.26f; // Meet the unchanged top stair without an open gap.
            // Simple access recesses only: visible floors, side returns and soffits. No invented
            // shop signs, furniture or unseen stalls. Floor heights keep the existing step tops.
            Quad("Concrete",P(floorFrontU,floor,a),P(floorFrontU,floor,b),P(backU,floor,b),P(backU,floor,a));
            Quad("CanopyWhite",P(faceU,floor,a),P(backU,floor,a),P(backU,roof,a),P(faceU,roof,a));
            Quad("CanopyWhite",P(backU,floor,b),P(faceU,floor,b),P(faceU,roof,b),P(backU,roof,b));
            Quad("Concrete",P(faceU,roof,a),P(backU,roof,a),P(backU,roof,b),P(faceU,roof,b));
            Panel(portal.parking?"Recess":"EntranceGlass",P(backU,(floor+roof)/2,portal.centre),north,-east,portal.width,roof-floor);
            foreach(float v in new[]{a+.035f,b-.035f})BoxUV("WindowFrameFine",faceU-.025f,(floor+roof)/2,v,.085f,roof-floor,.07f);
            foreach(float y in new[]{floor+.035f,roof-.035f})BoxUV("WindowFrameFine",faceU-.025f,y,portal.centre,.085f,.07f,portal.width);
            if(portal.parking)return;
            for(int i=0;i<=4;i++)BoxUV("WindowFrameFine",backU-.025f,(floor+roof)/2,a+portal.width*i/4,.05f,roof-floor,.055f);
            foreach(float y in new[]{floor+.045f,roof-.045f})BoxUV("WindowFrameFine",backU-.025f,y,portal.centre,.05f,.085f,portal.width);
        }
        static void Materials()
        {
            M("Cobalt", "#275FB1",0,.23f);M("TowerBlue","#3979C2",0,.24f);
            M("Navy","#193C88",0,.22f);M("PaleBlue","#74A9D6",0,.23f);
            M("WindowGlass","#233841",.12f,.57f);M("WindowFrameFine","#6D7981",.45f,.36f);
            M("EntranceGlass","#758D91",.12f,.42f);
            M("Concrete","#A2AAA9",0,.18f);M("Roof","#697678",0,.15f);
            M("RoofWhite","#D7DDD9",0,.19f);M("Recess","#26313C",0,.13f);
            M("CanopyWhite","#C8D7E3",0,.25f);M("ParkingYellow","#DBBA58",0,.23f);
            M("PaleGreenRoof","#8CADA1",.16f,.3f);
            var panel=M("ArtPanels","#FFFFFF",.10f,.29f);
            string path=ArtPath+"/OriginalPanelPalette.asset";
            var palette=AssetDatabase.LoadAssetAtPath<Texture2D>(path);bool exists=palette;
            if(!palette)palette=new Texture2D(64,1,TextureFormat.RGB24,false);
            var colors=new Color[64];
            // Original approximate palette guided by the photographed color families.
            // This is not the artist's original 57-color specification or a sampled photo texture.
            var stops=new[]{Hex("#4665B1"),Hex("#A7C6DA"),Hex("#E2E4DF"),Hex("#D9ABC0"),Hex("#C579A1"),Hex("#B84D85"),Hex("#8B448B")};
            for(int i=0;i<64;i++){float v=Mathf.Clamp01(i/56f)*(stops.Length-1);int a=Mathf.Min(stops.Length-2,(int)v);colors[i]=Color.Lerp(stops[a],stops[a+1],v-a);}
            palette.SetPixels(colors);palette.Apply();palette.name="OriginalPanelPalette";palette.filterMode=FilterMode.Point;palette.wrapMode=TextureWrapMode.Clamp;
            if(exists)EditorUtility.SetDirty(palette);else AssetDatabase.CreateAsset(palette,path);
            panel.SetTexture("_BaseMap",palette);panel.SetTextureScale("_BaseMap",Vector2.one);EditorUtility.SetDirty(panel);
        }
        static void WindowBand(Vector3 centre,Vector3 right,Vector3 normal,float width,float height,int panes)
        {
            Panel("WindowGlass",centre, right,normal,width,height);
            Vector3 outward=normal*.022f;
            for(int i=0;i<=panes;i++)
            {
                Vector3 p=centre+right*(-width/2+width*i/panes)+outward;
                Box("WindowFrameFine",p,right,normal,new Vector3(.036f,height,.045f));
            }
            foreach(float s in new[]{-1f,1f})Box("Navy",centre+Vector3.up*(s*(height/2+.04f)),right,normal,new Vector3(width+.08f,.08f,.11f));
        }
        static void FoldedCell(Vector3 centre,Vector3 right,Vector3 normal,float width,float height,int color,float projection=.18f)
        {
            if(Vector3.Dot(Vector3.Cross(right,Vector3.up),normal)<0)right=-right;
            Vector3 half=right*(width/2),up=Vector3.up*(height/2),peak=normal*projection;
            Vector3 a=centre-half,b=centre+peak,c=centre+half;
            void Face(Vector3 start,Vector3 end,int index)
            {
                if(Vector3.Dot(Vector3.Cross(end-start,Vector3.up),normal)>0)Quad("ArtPanels",start-up,end-up,end+up,start+up,index);
                else Quad("ArtPanels",end-up,start-up,start+up,end+up,index);
            }
            Face(a,b,Mathf.Clamp(color-3,0,56));Face(b,c,Mathf.Clamp(color+4,0,56));
            // End caps keep the angled plate from reading as a paper-thin open seam.
            void Cap(Vector3 p,Vector3 q,Vector3 r)
            {
                var g=G("ArtPanels");int n=g.vertices.Count;g.vertices.AddRange(new[]{p,q,r});
                var uv=new Vector2((color+.5f)/64f,.5f);g.uv.AddRange(new[]{uv,uv,uv});g.indices.AddRange(new[]{n,n+1,n+2});
            }
            Cap(a+up,b+up,c+up);Cap(c-up,b-up,a-up);
        }
        static int BackColor(int column,int floor)
        {
            // A deterministic study of the visible fragmented bands, not random store decoration
            // and not a claim that each individual artwork panel has been reproduced exactly.
            string[] rows={"32641523546213524361","23526415342615324153","41356231425362531426","15432645231624356132","36214532465124352613","25143624532165314263","42531645236124361532","16243521456324135246","34625143612534265124"};
            int k=rows[floor%rows.Length][column%20]-'0';
            return new[]{0,9,17,24,32,41,49}[k];
        }
        static void TingzhouElevation()
        {
            float low=minV+10.2f,high=maxV-10.4f,width=high-low,mid=(low+high)/2,u=minU-.07f;
            Vector3 right=north,normal=-east;
            for(int floor=0;floor<9;floor++)
            {
                float y=7.25f+floor*3.35f;
                WindowBand(P(u,y+.74f,mid),right,normal,width,1.40f,40);
                for(int c=0;c<40;c++)
                {
                    float v=low+width*(c+.5f)/40;
                    bool drop=((c*3+floor*5)%13==0)||((c+floor*3)%17==1);
                    float height=drop?2.67f:1.84f;
                    FoldedCell(P(u-.065f,y+3.32f-height/2,v),right,normal,width/40-.035f,height,BackColor(c,floor),.13f);
                }
            }
            BoxUV("Navy",u+.02f,37.7f,mid,.19f,.8f,width+.18f);
            // The two broad corner piers stand behind the projecting central elevation.
            foreach(var corner in new[]{new Vector3(minU+2.60f,0,minV+4.7f),new Vector3(minU+2.63f,0,maxV-4.62f)})
            {
                BoxUV("TowerBlue",corner.x-.08f,21.9f,corner.z,.19f,31.8f,9.0f);
                float outer=corner.z<mid?corner.z-3.91f:corner.z+3.91f;
                BoxUV("Navy",corner.x-.195f,21.9f,outer,.16f,31.8f,1.14f);
                BoxUV("PaleBlue",corner.x-.193f,21.9f,outer+(corner.z<mid?1.15f:-1.15f),.16f,31.8f,.66f);
            }
            PodiumWest(low,high,u);
        }
        static void PodiumWest(float low,float high,float u)
        {
            float width=high-low,mid=(low+high)/2;
            // Two-level market base: continuous blue balcony, alternating pale railing panels.
            BoxUV("Navy",u-.05f,6.46f,mid,.28f,.48f,width);
            BoxUV("TowerBlue",u-.04f,3.70f,mid,.25f,.43f,width);
            for(int i=0;i<36;i++)
            {
                float v=low+width*(i+.5f)/36;
                FoldedCell(P(u-.13f,4.45f,v),north,-east,width/36-.08f,1.08f,i%5==0?26:i%3==0?16:7,.065f);
            }
            foreach(var portal in WestPortals())RecessedPortal(portal,u-.025f);
            // Door/parking positions and metric sizes are proportional approximations from the
            // credited front-on view. No business occupancy is inferred from the dark openings.
            foreach(float v in new[]{low+4.4f,high-4.4f})
            {
                for(int j=0;j<4;j++)BoxUV("Concrete",u-.45f-j*.29f,.06f+(3-j)*.10f,v,.33f,.20f+(3-j)*.20f,4.7f);
                BoxUV("CanopyWhite",u-.35f,3.76f,v,.9f,.14f,5.0f);
            }
            float parking=mid-4.5f;
            BoxUV("ParkingYellow",u-.20f,3.04f,parking,.17f,.45f,5.55f);
            foreach(float v in new[]{parking-2.83f,parking+2.83f})BoxUV("Concrete",u-.2f,1.5925f,v,.32f,3.265f,.27f);
            BoxUV("CanopyWhite",u-.85f,1.29f,low-2.5f,1.45f,2.66f,1.5f);
            BoxUV("PaleGreenRoof",u-.85f,2.67f,low-2.5f,1.7f,.16f,1.75f);
            Panel("WindowGlass",P(u-1.585f,1.64f,low-2.5f),north,-east,1.20f,1.13f);
            Label("水源市場",P(u-.34f,5.55f,mid),-east,4.9f,.88f);
        }
        static void LaneElevation()
        {
            float centreU=(minU+maxU)/2;
            float centralV=maxV-4.95f;
            Vector3 normal=north,right=east;
            float centralWidth=20.2f;
            for(int floor=0;floor<9;floor++)
            {
                float y=7.25f+floor*3.35f;
                WindowBand(P(centreU,y+.77f,centralV)+normal*.07f,right,normal,centralWidth,1.38f,24);
                for(int group=0;group<3;group++)
                {
                    float groupU=centreU+(group-1)*6.47f;
                    for(int c=0;c<7;c++)
                    {
                        float u=groupU+(c-3)*.70f;
                        int color=24+((floor+group+c)%4)*6;
                        FoldedCell(P(u,y+2.45f,centralV)+normal*.11f,right,normal,.66f,1.77f,color,.24f);
                    }
                }
            }
            // North end piers from the actual outline, not a newly invented rectangle.
            foreach(float u in new[]{minU+7.32f,maxU-7.27f})
            {
                float v=maxV-.05f;
                BoxUV("TowerBlue",u,21.8f,v,9.05f,32.05f,.16f);
                BoxUV("Navy",u+(u<centreU?-3.7f:3.7f),21.8f,v+.10f,1.40f,32.05f,.15f);
            }
            for(int i=0;i<4;i++)
            {
                // The north/lane face is photo-supported in its upper massing; lower bays stay
                // neutral because no same-date, legible shop inventory is available.
                float u=centreU+(i-1.5f)*4.75f;
                Panel("Recess",P(u,2.1f,centralV)+normal*.09f,right,normal,4.3f,3.1f);
                BoxUV("Concrete",u,3.78f,centralV,4.42f,.20f,.32f);
            }
        }
        static void RooseveltElevation()
        {
            float low=minV+10.1f,high=maxV-10.35f,mid=(low+high)/2,width=high-low,u=maxU+.08f;
            for(int floor=0;floor<10;floor++)
            {
                float y=7.0f+floor*3.07f;
                Panel("Recess",P(u,y+.13f,mid),north,east,width,.23f);
                BoxUV(floor%2==0?"PaleBlue":"CanopyWhite",u-.01f,y+1.67f,mid,.16f,2.78f,width);
            }
            // The official record specifically gives the front folded artwork as 13 x 13 m.
            // Its exact elevation is estimated; it is not repeated on the Tingzhou facade.
            const float artWidth=13f,artHeight=13f,artY=24f;
            for(int x=0;x<40;x++)for(int y=0;y<32;y++)
            {
                float horizontal=(x+.5f)/40f*2-1,vertical=(y+.5f)/32f*2-1;
                float rectangle=Mathf.Max(Mathf.Abs(horizontal),Mathf.Abs(vertical));
                int color=Mathf.Clamp(19+Mathf.RoundToInt(rectangle*27)+((x+y/4)%3)*2,0,56);
                FoldedCell(P(u+.12f,artY-artHeight/2+(y+.5f)*artHeight/32,mid-artWidth/2+(x+.5f)*artWidth/40),north,east,artWidth/40-.012f,artHeight/32-.015f,color,.16f);
            }
            foreach(var corner in new[]{new Vector3(maxU-2.68f,0,minV+4.75f),new Vector3(maxU-2.62f,0,maxV-4.62f)})
            {
                BoxUV("TowerBlue",corner.x+.08f,21.9f,corner.z,.20f,31.8f,9.0f);
                BoxUV("Navy",corner.x+.19f,21.9f,corner.z+(corner.z<mid?-3.85f:3.85f),.15f,31.8f,1.15f);
            }
            BoxUV("Navy",u,6.46f,mid,.27f,.50f,width);
            Label("水源市場",P(u+.19f,5.55f,mid),east,4.9f,.88f);
        }
        static void Rooftop(Vector3[] points)
        {
            Roof(points,38.18f,"Roof");FootprintShell(points,37.8f,38.22f,"Navy");
            float u=(minU+maxU)/2,v=(minV+maxV)/2;
            BoxUV("RoofWhite",u,38.70f,v,14f,1.03f,19f);
            // Shallow, chamfered central roof volume observed above the blue parapet.
            Vector3 a=P(u-7,39.20f,v-9.5f),b=P(u+7,39.20f,v-9.5f),c=P(u+7,39.20f,v+9.5f),d=P(u-7,39.20f,v+9.5f);
            Vector3 at=P(u-5.9f,40.3f,v-7.8f),bt=P(u+5.9f,40.3f,v-7.8f),ct=P(u+5.9f,40.3f,v+7.8f),dt=P(u-5.9f,40.3f,v+7.8f);
            Quad("RoofWhite",a,at,bt,b);Quad("RoofWhite",b,bt,ct,c);Quad("RoofWhite",c,ct,dt,d);Quad("RoofWhite",d,dt,at,a);Quad("RoofWhite",at,dt,ct,bt);
        }
        static void Label(string text,Vector3 centre,Vector3 normal,float width,float height)
        {
            // A mesh label uses the existing Noto font. Its atlas is frozen after all labels
            // below are built, so later editor font requests cannot scramble these UVs.
            var font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC font missing.");
            font.RequestCharactersInTexture(text,64,FontStyle.Normal);
            var g=G("Lettering");Vector3 right=Vector3.Cross(Vector3.up,-normal).normalized;
            float advance=0,minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            foreach(char c in text)if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal)){advance+=ch.advance;minY=Mathf.Min(minY,ch.minY);maxY=Mathf.Max(maxY,ch.maxY);}
            float sx=width/advance,sy=height/(maxY-minY),pen=-advance/2,cy=(minY+maxY)/2;
            foreach(char c in text)
            {
                if(!font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))continue;
                Vector3 At(float x,float y)=>centre+right*x*sx+Vector3.up*(y-cy)*sy;
                int n=g.vertices.Count;g.vertices.AddRange(new[]{At(pen+ch.minX,ch.minY),At(pen+ch.minX,ch.maxY),At(pen+ch.maxX,ch.maxY),At(pen+ch.maxX,ch.minY)});
                g.uv.AddRange(new[]{ch.uvBottomLeft,ch.uvTopLeft,ch.uvTopRight,ch.uvBottomRight});g.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});pen+=ch.advance;
            }
        }
        static void FreezeLettering()
        {
            var font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);var source=font.material.mainTexture;
            var copy=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);
            string path=ArtPath+"/MarketLetteringAtlas.png";
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency=true;importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("StreetLabelDepth shader missing.");
            string matPath=ArtPath+"/Lettering.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!mat){mat=new Material(shader){name="Lettering"};AssetDatabase.CreateAsset(mat,matPath);}mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);
            materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes()
        {
            foreach(var pair in batches)
            {
                string path=ArtPath+"/"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool exists=mesh;if(!mesh)mesh=new Mesh();else mesh.Clear();
                mesh.name=pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(pair.Value.vertices);mesh.SetUVs(0,pair.Value.uv);mesh.SetTriangles(pair.Value.indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                if(pair.Key=="Lettering")mesh.SetColors(Enumerable.Repeat(new Color32(230,238,242,255),mesh.vertexCount).ToArray());
                mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Market exterior • "+pair.Key){layer=2};go.transform.SetParent(root,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();var material=materials[pair.Key];renderer.sharedMaterial=material;
                bool thin=pair.Key=="ArtPanels"||pair.Key=="WindowFrameFine"||pair.Key=="WindowGlass"||pair.Key=="EntranceGlass"||pair.Key=="Lettering";
                renderer.shadowCastingMode=thin?ShadowCastingMode.Off:ShadowCastingMode.On;renderer.receiveShadows=!thin;
                if(material.HasProperty("_ReceiveShadows"))
                {
                    material.SetFloat("_ReceiveShadows",thin?0f:1f);
                    if(thin)material.EnableKeyword("_RECEIVE_SHADOWS_OFF");else material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                    EditorUtility.SetDirty(material);
                }
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
        /// <summary>Builds only this independent visual landmark. Does not hide existing content or save the scene.</summary>
        public static GameObject Build(Transform parent=null)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build the market outside Play mode.");
            var building=ReadFootprint();SetFrame(building.points);
            Triangulate(building.points); // Validate the unchanged concave polygon before scene mutation.
            Directory.CreateDirectory(ArtPath);AssetDatabase.Refresh();batches.Clear();materials.Clear();Materials();
            var old=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName)?.gameObject:SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);
            var result=new GameObject(RootName){layer=2};root=result.transform;root.SetPositionAndRotation(origin,Quaternion.identity);root.SetParent(parent,true);
            Undo.RegisterCreatedObjectUndo(result,"Build source-based water source market exterior");
            FootprintShell(building.points,-.04f,38.18f,"Cobalt");
            TingzhouElevation();LaneElevation();RooseveltElevation();Rooftop(building.points);FreezeLettering();SaveMeshes();
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(result.scene);
            Debug.Log("Shuiyuan study built: exact OSM footprint "+building.points.Length+" points; OSM levels "+building.levels+"; display height 40.3 m is an artwork-supported estimate. Late-2025 photographed color families, approximate panel arrangement; no added colliders. Scene not saved.");
            return result;
        }
        [MenuItem("Taiwan Rider/Street Survey/Build Shuiyuan market exterior")]
        public static void Apply() => Build();
    }
}
