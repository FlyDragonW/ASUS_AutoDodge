using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaiwanRider.Editor
{
    // Original visible facade studies aligned to OSM edges; dimensions remain estimated.
    // See Sources/under-armour-2025.json; January2025 filming is author-reported.
    // Only the northern observed part of the Under Armour corner is retained; present-day pet branding is not historical evidence.
    public static class UnderArmourCornerSetup
    {
        public const string RootName = "Under Armour corner • filmed January 2025";
        static string Art;
        const string FontPath = "Assets/TaiwanRider/Art/Fonts/NotoSansTC.otf";
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Geometry> geometry = new Dictionary<string, Geometry>();
        static readonly List<Lettering> lettering = new List<Lettering>();
        static Font font;

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
            public readonly List<Color32> colors = new List<Color32>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count; Vector3 normal = Vector3.Cross(b-a,c-a).normalized;
                foreach (Vector3 p in new[] { a,b,c,d })
                {
                    vertices.Add(p);
                    uv.Add(Mathf.Abs(normal.y)>.7f ? new Vector2(p.x,p.z) : Mathf.Abs(normal.x)>.7f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y));
                }
                indices.AddRange(new[] { n,n+1,n+2,n,n+2,n+3 });
            }
        }
        sealed class Lettering
        {
            public string text; public Vector3 position; public float height, width, yaw, roll; public Color color;
        }
        static Color Hex(string code) { ColorUtility.TryParseHtmlString(code,out var c); return c; }
        static Geometry G(string key)
        { if(!geometry.TryGetValue(key,out var value)) geometry[key]=value=new Geometry(); return value; }
        static Material M(string key,string color,float metallic=0f,float smoothness=.24f)
        {
            string path=Art+"/"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path); }
            m.name=key;m.enableInstancing=true;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            materials[key]=m;EditorUtility.SetDirty(m);return m;
        }
        static void Box(string key,Vector3 p,Vector3 size,Quaternion? orientation=null)
        {
            var q=orientation??Quaternion.identity;var g=G(key);Vector3 h=size*.5f;
            Vector3 P(float x,float y,float z)=>p+q*Vector3.Scale(new Vector3(x,y,z),h);
            g.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));
            g.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            g.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
            g.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            g.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));
            g.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Rod(string key,Vector3 a,Vector3 b,float radius,int sides=8)
        {
            var g=G(key);var q=Quaternion.FromToRotation(Vector3.up,b-a);
            for(int i=0;i<sides;i++)
            {
                float t=i*2*Mathf.PI/sides,t1=(i+1)*2*Mathf.PI/sides;
                Vector3 d=q*new Vector3(Mathf.Cos(t)*radius,0,Mathf.Sin(t)*radius),e=q*new Vector3(Mathf.Cos(t1)*radius,0,Mathf.Sin(t1)*radius);
                g.Quad(a+d,b+d,b+e,a+e);g.Quad(b,b+e,b+d,b);g.Quad(a,a+d,a+e,a);
            }
        }
        static void Label(string text,Vector3 p,float height,string color,float width=0f,float yaw=0f,float roll=0f)
        { lettering.Add(new Lettering{text=text,position=p,height=height,width=width,yaw=yaw,roll=roll,color=Hex(color)}); }
        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public Vector3[] points; public Edge[] roadFrontEdges; }
        [Serializable] sealed class Edge { public string roadId; public int edgeIndex; public Vector3 a,b,outward; public float lengthMetres; }
        static void B(string key,float x,float y,float z,float w,float h,float d) => Box(key,new Vector3(x,y,z),new Vector3(w,h,d));
        static void L(string text,float x,float y,float z,float height,string color,float width) => Label(text,new Vector3(x,y,z),height,color,width);
        static void StoreGlass()
        {
            var glass=M("StoreGlass","#AEC1B7",.08f,.72f);
            var color=glass.GetColor("_BaseColor");color.a=.22f;glass.SetColor("_BaseColor",color);
            glass.SetFloat("_Surface",1);glass.SetFloat("_Blend",0);glass.SetFloat("_BlendModePreserveSpecular",1);
            glass.SetFloat("_SrcBlend",(float)BlendMode.One);glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);glass.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite",0);glass.SetOverrideTag("RenderType","Transparent");glass.renderQueue=(int)RenderQueue.Transparent;
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.SetShaderPassEnabled("DepthOnly",false);glass.SetShaderPassEnabled("ShadowCaster",false);
            EditorUtility.SetDirty(glass);
        }

        static float width;
        static void Materials()
        {
            M("Black","#252927",.08f,.26f);M("Frame","#353C39",.28f,.37f);
            M("Floor","#8A8F85",0,.23f);M("Inside","#C9CDC3",0,.24f);
            M("Steel","#A2AAA4",.45f,.41f);M("Paper","#EAECE0",0,.23f);
            M("GlassDark","#273D38",.08f,.58f);M("Cloth","#72796E",0,.16f);
            M("Wood","#B2A58B",0,.23f);M("Light","#F0F0DB",0,.27f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#D4D9B7").linear*.30f);
        }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;
            draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=rotation.eulerAngles.y;}
        }
        static void Mark(float x,float y,float z,float scale)
        {
            // Original coarse geometry for the visible paired curved UA emblem; no source pixels.
            var g=G("Light");const int segments=20;
            foreach(float side in new[]{-1f,1f})for(int i=0;i<segments;i++){
                float a=i*Mathf.PI/segments,b=(i+1)*Mathf.PI/segments;
                Vector3 P(float t,float edge)=>new Vector3(x+.51f*Mathf.Cos(t)*scale,y+side*(.28f-.25f*Mathf.Sin(t)+edge*.055f)*scale,z);
                var p=P(a,-1);var q=P(b,-1);var r=P(b,1);var s=P(a,1);
                if(Vector3.Cross(q-p,r-p).z<0)g.Quad(p,s,r,q);else g.Quad(p,q,r,s);
            }
        }
        static void Glazing(float x,float y,float z,float w,float h)
        {
            B("StoreGlass",x,y,z-.025f,w,h,.024f);
            foreach(float dx in new[]{-w*.5f,w*.5f})B("Frame",x+dx,y,z+.014f,.048f,h+.046f,.065f);
            foreach(float yy in new[]{y-h*.5f,y+h*.5f})B("Frame",x,yy,z+.014f,w,.052f,.065f);
        }
        static void DisplayForm(float x,float y,float z,bool dark)
        {
            // Source shows sportswear/mannequin silhouettes; garment type and exact pose remain approximations.
            string garment=dark?"Cloth":"Paper";
            B("Steel",x,y+.04f,z,.44f,.045f,.32f);
            Rod("Steel",new Vector3(x,y+.06f,z),new Vector3(x,y+.82f,z),.022f,8);
            B(garment,x,y+1.03f,z,.34f,.56f,.18f);
            foreach(float side in new[]{-1f,1f}){
                Box(garment,new Vector3(x+side*.23f,y+1.10f,z),new Vector3(.115f,.40f,.14f),Quaternion.Euler(0,0,side*13f));
                B(dark?"Black":"Cloth",x+side*.092f,y+.49f,z,.13f,.53f,.145f);
            }
            Rod("Paper",new Vector3(x,y+1.37f,z),new Vector3(x,y+1.53f,z),.077f,10);
        }
        static void WindowBay(float x,float w,bool door,bool display,bool main,float interiorJoin)
        {
            // Both faces share one shallow interior corner. A full-width independent
            // return backboard would enter the adjacent main display window.
            float low=x-w*.5f,high=x+w*.5f;
            if(main)low=Mathf.Max(low,interiorJoin);else high=Mathf.Min(high,interiorJoin);
            float innerWidth=high-low,innerX=(low+high)*.5f;
            B("Inside",innerX,1.52f,-1.06f,innerWidth,2.68f,.07f);
            B("Wood",innerX,.135f,-.48f,innerWidth,.13f,1.05f);
            B("Light",innerX,2.82f,-.37f,innerWidth*.86f,.035f,.09f);
            // Narrow shelf marks give only the density visible in the source, not invented product labels.
            if(display){
                B("Light",innerX,.28f,-.56f,innerWidth*.76f,.19f,.44f);
                DisplayForm(innerX-innerWidth*.20f,.38f,-.55f,false);DisplayForm(innerX+innerWidth*.21f,.38f,-.57f,true);
            }else for(int row=0;row<5;row++){
                float y=.39f+row*.41f;B("Frame",innerX,y,-.94f,innerWidth*.86f,.035f,.22f);
                for(int col=0;col<4;col++)B((col+row)%3==0?"Paper":"Cloth",innerX-innerWidth*.32f+col*innerWidth*.21f,y+.093f,-.91f,innerWidth*.15f,.14f,.11f);
            }
            Glazing(x,1.49f,-.025f,w,2.63f);
            if(door){
                B("Frame",x,1.49f,.024f,.04f,2.65f,.065f);
                foreach(float dx in new[]{-.095f,.095f})B("Steel",x+dx,1.32f,.074f,.025f,.42f,.032f);
            }
        }
        static void TwoLevelFace(float w,bool main,float upperInteriorJoin,float lowerInteriorJoin)
        {
            // Black piers and solid spandrel are directly visible. This is a two-level crop only.
            B("Black",0,3.48f,-.025f,w,1.07f,.22f);
            B("Frame",0,3.997f,.090f,w,.055f,.045f);
            B("Black",0,6.78f,-.025f,w,.80f,.21f);
            B("Frame",0,7.18f,-.025f,w,.04f,.25f);
            foreach(float x in new[]{-w*.5f+.11f,w*.5f-.11f})B("Black",x,1.48f,-.06f,.22f,2.97f,.21f);
            if(main){
                L("UNDER ARMOUR",-.39f,3.47f,.097f,.43f,"#F1F2E7",w*.70f);
                Mark(w*.395f,3.47f,.103f,.80f);
                // Main front has separate lit display and entrance openings, divided by substantial black piers.
                float bay=w*.285f;
                WindowBay(-w*.287f,bay,false,true,main,lowerInteriorJoin);WindowBay(w*.220f,bay,true,false,main,lowerInteriorJoin);
                // Fill only the exact solid intervals around these two openings. Keep the
                // northern pier within the reserved crop so it cannot cross the real return.
                B("Black",-w*.46475f,1.48f,-.06f,w*.0705f,2.97f,.21f);
                B("Black",-w*.0335f,1.48f,-.06f,w*.222f,2.97f,.21f);
                B("Black",w*.43125f,1.48f,-.06f,w*.1375f,2.97f,.21f);
            }else{
                L("UNDER ARMOUR",.05f,3.47f,.097f,.35f,"#F1F2E7",w*.84f);
                WindowBay(w*.23f,w*.40f,true,false,main,lowerInteriorJoin);WindowBay(-w*.27f,w*.31f,false,true,main,lowerInteriorJoin);
                B("Black",0,1.48f,-.06f,.36f,2.97f,.21f);
            }
            int panes=main?4:3;float pane=(w-.30f)/panes;
            for(int i=0;i<panes;i++){
                float x=-w*.5f+.15f+pane*(i+.5f);
                float low=x-(pane-.04f)*.5f,high=x+(pane-.04f)*.5f;
                if(main)low=Mathf.Max(low,upperInteriorJoin);else high=Mathf.Min(high,upperInteriorJoin);
                if(high>low)B(i==0?"GlassDark":"Inside",(low+high)*.5f,5.25f,-1.17f,high-low,2.35f,.045f);
                B("Light",x,6.338f,-.63f,pane*.64f,.026f,.085f);
                Glazing(x,5.25f,-.005f,pane-.032f,2.33f);
                if(i>0&&(main||i<panes-1)){B("Frame",x+.08f,4.55f,-.92f,pane*.66f,.045f,.28f);B("Paper",x-.12f,4.72f,-.91f,.18f,.30f,.12f);}
            }
            // Unreadable narrow upper graphics remain anonymous geometric strokes.
            for(int i=0;i<(main?11:7);i++)B("Frame",-w*.32f+i*w*.060f,6.86f,.083f,w*.034f,.016f,.008f);
        }
        static void Slab(string mat,Vector3 north,Vector3 back,float y,float thickness)
        {
            // A trapezoid follows the real oblique return, avoiding a rectangular slab through its windows.
            Vector3[] p={new Vector3(north.x,y+thickness*.5f,north.z+.055f),new Vector3(width*.5f,y+thickness*.5f,.055f),
                new Vector3(width*.5f,y+thickness*.5f,back.z),new Vector3(back.x,y+thickness*.5f,back.z)};
            var low=p.Select(v=>new Vector3(v.x,y-thickness*.5f,v.z)).ToArray();var g=G(mat);
            g.Quad(p[0],p[1],p[2],p[3]);g.Quad(low[3],low[2],low[1],low[0]);
            for(int i=0;i<4;i++){int j=(i+1)%4;g.Quad(low[i],low[j],p[j],p[i]);}
        }
        static void Corner(Vector3 northCorner,Vector3 returnEnd,Vector3 origin,Vector3 outward)
        {
            const float returnLength=6.2f;
            var inverse=Quaternion.Inverse(Quaternion.LookRotation(outward,Vector3.up));
            Vector3 dir=(returnEnd-northCorner).normalized,sideOut=new Vector3(dir.z,0,-dir.x);
            Vector3 sideCenter=inverse*(northCorner+dir*(returnLength*.5f)+sideOut*.035f-origin);
            Vector3 north=inverse*(northCorner-origin),back=inverse*(northCorner+dir*returnLength-origin);
            Slab("Floor",north,back,.055f,.11f);Slab("Inside",north,back,4.06f,.12f);Slab("Inside",north,back,7.13f,.14f);
            // One common ceiling replaces two overlapping per-face ceilings. Clip both
            // rear planes to their actual intersection, preserving the clear glass corner.
            Slab("Inside",north,back,6.365f,.045f);
            Vector3 sideNormal=inverse*sideOut,sideAxis=Vector3.Cross(Vector3.up,sideNormal);
            Vector2 InteriorJoin(float depth){
                Vector3 p=sideCenter-sideNormal*depth;
                float sideX=(-depth-p.z)/sideAxis.z;
                return new Vector2(p.x+sideAxis.x*sideX,sideX);
            }
            Vector2 upperJoin=InteriorJoin(1.17f),lowerJoin=InteriorJoin(1.06f);
            TwoLevelFace(width,true,upperJoin.x,lowerJoin.x);
            Frame(()=>TwoLevelFace(returnLength,false,upperJoin.y,lowerJoin.y),sideCenter,Quaternion.LookRotation(sideNormal,Vector3.up));
            // Modest shared corner post closes only the real miter; it does not make another storefront.
            B("Black",north.x,1.48f,north.z,.19f,2.97f,.19f);
            B("Frame",north.x,5.29f,north.z,.105f,2.51f,.105f);
            // A shallow unlit rear closure exists solely behind the observed crop, not an invented occupied floor.
            float backWidth=width*.5f-back.x;
            B("Black",back.x+backWidth*.5f,3.55f,back.z-.03f,backWidth,7.10f,.06f);
            B("Black",width*.5f-.03f,3.55f,back.z*.5f,.06f,7.10f,-back.z);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build Under Armour corner in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="UA2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge UA2501 reservation before building.");
            var entry=entries[0];if(entry.buildingId!="366231798"||entry.edgeIndex!=0)throw new InvalidOperationException("UA2501 must use the real southern plaza edge 0, not POYA edge 4.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var host=survey.buildings.Single(b=>b.id==entry.buildingId);var edge=host.roadFrontEdges.Single(e=>e.edgeIndex==0&&e.roadId=="316698035");
            Vector3 tangent=(edge.b-edge.a).normalized,normal=new Vector3(tangent.z,0,-tangent.x);
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build observed Under Armour corner");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/UnderArmourCorner/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;Corner(host.points[1],host.points[2],entry.center,normal);BakeLettering();
            float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException("UA2501 exceeds its visualHeight.");
            SaveMeshes(go.transform);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
        }
        static void BakeLettering()
        {
            font.RequestCharactersInTexture(string.Concat(lettering.Select(l=>l.text)),64,FontStyle.Normal);var g=G("Lettering");
            foreach(var item in lettering)
            {
                var lines=item.text.Split('\n');const float lineHeight=73f;float bottom=float.PositiveInfinity,top=float.NegativeInfinity,longest=0;
                for(int line=0;line<lines.Length;line++)
                {
                    float advance=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal)){bottom=Mathf.Min(bottom,ch.minY-line*lineHeight);top=Mathf.Max(top,ch.maxY-line*lineHeight);advance+=ch.advance;}
                    longest=Mathf.Max(longest,advance);
                }
                if(float.IsInfinity(bottom))continue;float scale=item.height/64f;if(item.width>0&&longest*scale>item.width)scale=item.width/longest;
                var rotation=Quaternion.Euler(0,180f+item.yaw,0)*Quaternion.Euler(0,0,item.roll);float middle=(bottom+top)/2;
                for(int line=0;line<lines.Length;line++)
                {
                    float width=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))width+=ch.advance;float pen=-width/2;
                    foreach(char c in lines[line])
                    {
                        if(!font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))continue;
                        if(!char.IsWhiteSpace(c))
                        {
                            Vector3 P(float px,float py)=>item.position+rotation*new Vector3(px*scale,(py-line*lineHeight-middle)*scale,0);
                            int n=g.vertices.Count;g.vertices.Add(P(pen+ch.minX,ch.minY));g.vertices.Add(P(pen+ch.minX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.minY));
                            // StreetLabelDepth outputs vertex RGB directly, so sRGB swatches
                            // need conversion in a Linear project to avoid grey/washed-out ink.
                            Color ink=QualitySettings.activeColorSpace==ColorSpace.Linear?item.color.linear:item.color;
                            g.uv.AddRange(new[]{ch.uvBottomLeft,ch.uvTopLeft,ch.uvTopRight,ch.uvBottomRight});for(int i=0;i<4;i++)g.colors.Add(ink);g.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                        }
                        pen+=ch.advance;
                    }
                }
            }
            var atlas=font.material.mainTexture;if(!atlas)throw new InvalidOperationException("Noto font atlas is missing.");
            var rt=RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var copy=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
            string path=Art+"/UnderArmourCornerLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"BlueGrey","PaleBlue","Magenta","Yellow","Grout","Tile","Shutter","ShutterRib","WhiteTile","WoodDark","CaneGreen","CaneDark","WishGreen","ToyBlue","ToyPink","StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/UnderArmourCorner_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="UnderArmourCorner_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Under Armour corner batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=pair.Key!="Inside"&&pair.Key!="Floor";var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
