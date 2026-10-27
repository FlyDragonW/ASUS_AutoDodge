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
    // Original visible facade studies aligned to OSM edges; dimensions remain estimated.
    // See Sources/mid-west-infill-2025.json; publication dates are separate from capture dates.
    public static class MidWestInfillSetup
    {
        public const string RootName = "Mid-west infill | observed January 2025 facades";
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
            m.name=key;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
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


        const string ReservationsPath="Assets/TaiwanRider/Data/StorefrontReservations.json";
        [Serializable] sealed class Reservations { public Reservation[] groups; }
        [Serializable] sealed class Reservation
        { public string id,buildingId,sceneName; public int edgeIndex; public Vector3 center; public float width,height,visualHeight; }
        [Serializable] sealed class Survey { public Building[] buildings; }
        [Serializable] sealed class Building { public string id; public Vector3[] points; }
        static float width;
        static bool IsFinite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static void Materials()
        {
            M("Wall","#8D8A82",0,.17f);M("Floor","#ABA79D",0,.18f);M("Inside","#C6C5B7",0,.18f);
            M("Frame","#303435",.22f,.28f);M("Steel","#B2B7B5",.5f,.42f);M("Ink","#1B1C1D",0,.21f);
            M("WindowDark","#27332E",.04f,.35f);M("Paper","#EEEADC",0,.2f);M("Wood","#AC9476",0,.18f);
            M("Orange","#E68C31",0,.22f);M("Red","#AA252C",0,.18f);M("Yellow","#F1D82B",0,.19f);
            M("Blue","#138DA8",0,.2f);M("Green","#639477",0,.2f);M("Cream","#D3CE9E",0,.18f);
            M("Light","#F2EACF",0,.2f);M("LedRed","#F24934",0,.18f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#E5DDBB").linear*.55f);
            materials["LedRed"].EnableKeyword("_EMISSION");materials["LedRed"].SetColor("_EmissionColor",Hex("#F24934").linear*.9f);
            StoreGlass();
        }
        static void Shell(float depth)
        {
            // Rear walls and merchandise are shallow, generic view fill; not a surveyed floor plan.
            B("Floor",0,.04f,-depth*.5f+.035f,width,.16f,depth+.13f);
            B("Inside",0,1.46f,-depth,width,2.92f,.10f);
            B("Inside",0,2.94f,-depth*.5f,width,.12f,depth);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.055f),1.46f,-depth*.5f,.11f,2.92f,depth);
            foreach(float x in new[]{-width*.3f,width*.3f})B("Light",x,2.871f,-depth*.54f,.075f,.025f,depth*.72f);
        }
        static void Glass(float x,float w,float height=2.65f)
        {
            B("StoreGlass",x,height*.5f+.15f,-.12f,w,height,.024f);
            foreach(float xx in new[]{x-w*.5f,x+w*.5f})B("Frame",xx,height*.5f+.15f,-.09f,.035f,height+.04f,.055f);
            foreach(float y in new[]{.15f,height+.15f})B("Frame",x,y,-.09f,w,.038f,.055f);
        }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            float yaw=rotation.eulerAngles.y;
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=yaw;}
        }
        static void Porter()
        {
            Shell(2.60f);
            B("Ink",0,3.62f,-.01f,width,1.10f,.24f);
            B("Frame",0,4.175f,.015f,width,.045f,.26f);
            B("Frame",0,3.055f,.04f,width,.05f,.34f);
            L("P O R T E R",0,3.68f,.119f,.43f,"#EEEFE9",width-.65f);
            foreach(float x in new[]{-width*.5f+.065f,width*.5f-.065f})B("Frame",x,1.53f,-.025f,.13f,3.10f,.34f);
            // The night frame shows a dark broad display and a lit interior, not enough detail
            // to identify bag models or the exact door split. These are deliberately generic.
            Glass(width*.22f,width*.48f);Glass(-width*.30f,width*.34f);
            B("Ink",width*.22f,.35f,-.49f,width*.45f,.5f,.56f);
            B("WindowDark",0,1.50f,-2.525f,width-.22f,2.61f,.018f);
            for(int i=0;i<4;i++){
                float x=width*.22f-width*.17f+i*width*.113f;
                B("Wood",x,.79f,-.95f,.54f,.20f,.43f);
                B(i%2==0?"Ink":"Green",x,1.06f,-.95f,.35f,.38f,.18f);
                foreach(float side in new[]{-1f,1f})B("Steel",x+side*.10f,1.285f,-.95f,.025f,.09f,.025f);
                B("Steel",x,1.33f,-.95f,.22f,.02f,.025f);
            }
            B("Frame",-width*.085f,1.48f,-.09f,.04f,2.75f,.08f);
            B("Steel",-width*.085f,1.24f,-.032f,.022f,.43f,.043f);
        }
        static void Agang()
        {
            Shell(2.45f);
            B("Ink",0,3.57f,-.04f,width,1.24f,.24f);
            B("Orange",0,3.51f,.11f,width-.06f,.76f,.12f);
            L("\u963F\u525B",width*.30f,3.52f,.181f,.39f,"#69251C",1.08f);
            L("98",-width*.35f,3.56f,.183f,.53f,"#F8F4DF",.84f);
            B("Ink",.11f,3.51f,.183f,.72f,.32f,.014f);
            B("Frame",.11f,3.51f,.193f,.62f,.25f,.009f);
            // Remaining small fascia text is too blurred to transcribe faithfully.
            B("Cream",0,2.94f,.13f,width,.29f,.43f);
            B("Steel",0,2.766f,.20f,width,.04f,.36f);
            foreach(float x in new[]{-width*.5f+.08f,width*.5f-.08f})B("Cream",x,1.38f,.00f,.14f,2.79f,.23f);
            foreach(float x in new[]{-.73f,.56f})B("Light",x,2.765f,-.035f,.43f,.028f,.15f);
            // Traffic masks most ground-level detail: a shallow service counter and plain
            // seating silhouettes are view fill, not menu, layout or equipment claims.
            B("Steel",width*.29f,.62f,-.99f,.96f,1.05f,.55f);
            B("Paper",width*.29f,1.20f,-.99f,1.03f,.08f,.63f);
            B("Wood",-width*.20f,.78f,-1.90f,1.2f,.065f,.65f);
            foreach(float x in new[]{-1.03f,-.20f})foreach(float z in new[]{-1.61f,-2.14f})B("Frame",x,.36f,z,.036f,.72f,.038f);
            float sx=width*.5f-.14f;
            B("Ink",sx,5.28f,.25f,.16f,2.32f,.60f);B("Red",sx,5.30f,.51f,.18f,2.21f,.76f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                L("\u963F\n\u525B",0,5.38f,.010f,.49f,"#F4E5CE",.62f);
            },new Vector3(sx+side*.101f,0,.51f),Quaternion.Euler(0,side*90,0));
        }
        static void Disc(string key,float x,float y,float z,float radius)
        { Rod(key,new Vector3(x,y,z-.003f),new Vector3(x,y,z+.003f),radius,24); }
        static void LotteryMark(float x,float y,float z,float scale)
        {
            // Original reduced mark: colour and circular placement are observed, tiny artwork is not traced.
            Disc("Paper",x,y,z,.16f*scale);
            for(int i=0;i<5;i++){float a=(i*72f+18)*Mathf.Deg2Rad;
                Disc(i%2==0?"Red":"Blue",x+Mathf.Cos(a)*.085f*scale,y+Mathf.Sin(a)*.085f*scale,z+.007f,.058f*scale);}
        }
        static void LotteryBand(float w,float y,float h,float z,bool blade)
        {
            B("Yellow",0,y,z,w,h,.10f);
            var g=G("Blue");const int count=10;
            float Edge(float t)=>w*(.19f-.56f*t*t);
            for(int i=0;i<count;i++){
                float a=(float)i/count,b=(float)(i+1)/count,ya=y-h*.5f+a*h,yb=y-h*.5f+b*h;
                g.Quad(new Vector3(-w*.5f,ya,z+.058f),new Vector3(Edge(a),ya,z+.058f),new Vector3(Edge(b),yb,z+.058f),new Vector3(-w*.5f,yb,z+.058f));}
            if(!blade){
                LotteryMark(w*.33f,y,z+.072f,.64f);
                L("\u53F0\u7063\u5F69\u5238",w*.08f,y,z+.081f,.20f,"#6D6241",w*.41f);
                Disc("Red",-w*.30f,y+.06f,z+.070f,.115f);
                L("\u53F0\u7063\u904B\u5F69",-w*.28f,y-.17f,z+.081f,.14f,"#F7EEDE",w*.36f);
            }else{
                LotteryMark(0,y+h*.36f,z+.072f,.72f);
                L("\u53F0\u7063\n\u5F69\u5238",0,y+h*.16f,z+.081f,.20f,"#74634D",w*.77f);
                Disc("Red",0,y-h*.30f,z+.072f,.13f);
                L("\u53F0\u7063\u904B\u5F69",0,y-h*.40f,z+.081f,.105f,"#F8F1E5",w*.86f);
            }
        }
        static void Lottery()
        {
            Shell(2.60f);
            B("Wall",0,3.53f,-.05f,width,1.36f,.20f);
            LotteryBand(width-.025f,3.53f,.95f,.115f,false);
            B("Paper",0,2.96f,.115f,width,.12f,.19f);
            foreach(float x in new[]{-width*.5f+.09f,width*.5f-.09f})B("Paper",x,1.48f,.035f,.15f,2.96f,.29f);
            Glass(.56f,width*.52f,2.61f);Glass(-1.03f,width*.27f,2.61f);
            B("Ink",0,2.685f,-.01f,width-.39f,.23f,.07f);
            for(int i=0;i<28;i++)B("LedRed",-width*.39f+i*width*.78f/27,2.69f,.033f,.025f,.035f,.008f);
            B("Paper",.49f,.78f,-1.17f,1.62f,1.25f,.56f);
            B("Steel",.49f,1.43f,-1.17f,1.72f,.06f,.65f);
            B("Frame",.81f,1.68f,-1.28f,.34f,.32f,.045f);
            foreach(float x in new[]{-.95f,-.18f,.59f}){
                B("Paper",x,1.95f,-2.535f,.61f,.74f,.025f);
                for(int row=0;row<4;row++)B(row%2==0?"Blue":"Red",x,1.71f+row*.15f,-2.513f,.49f,.018f,.009f);
            }
            float sx=-width*.5f+.15f;
            B("Steel",sx,5.04f,.35f,.12f,1.99f,.72f);
            // The tall blade is image-right (negative local X), as in January04:35/40.
            foreach(float side in new[]{-1f,1f})Frame(()=>LotteryBand(.86f,5.02f,2.12f,0,true),
                new Vector3(sx+side*.070f,0,.57f),Quaternion.Euler(0,side*90,0));
        }
        static void ValidateGeometry(Reservation entry)
        {
            if(geometry.Values.All(g=>g.vertices.Count==0))throw new InvalidOperationException("No infill geometry: "+entry.id);
            foreach(var batch in geometry)foreach(var p in batch.Value.vertices)
                if(!IsFinite(p.x)||!IsFinite(p.y)||!IsFinite(p.z)||Mathf.Abs(p.x)>entry.width*.5f+.026f||p.y>entry.visualHeight+.026f||p.y<-.051f)
                    throw new InvalidOperationException("Infill geometry/glyph bounds exceed reservation: "+entry.id+" / "+batch.Key+" at "+p);
            // Checks run after font rasterisation, so actual glyph vertices count, not text anchors.
        }
        static Vector3 Outward(Survey survey,string id)
        {
            var b=survey.buildings.Single(v=>v.id==id);if(b.points==null||b.points.Length<4)throw new InvalidOperationException("Incomplete infill alignment host: "+id);
            var d=b.points[4%b.points.Length]-b.points[3];return new Vector3(d.z,0,-d.x).normalized;
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build infill facades in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            if(!File.Exists(ReservationsPath))throw new FileNotFoundException("Merge mid-west-infill-reservations.json first.",ReservationsPath);
            var all=JsonUtility.FromJson<Reservations>(File.ReadAllText(ReservationsPath));
            var ids=new[]{"MI2501","MI2502","MI2503"};var entries=(all?.groups??Array.Empty<Reservation>()).Where(e=>ids.Contains(e.id)).ToArray();
            if(entries.Length!=3||entries.Select(e=>e.id).Distinct().Count()!=3)throw new InvalidOperationException("MI2501/02/03 are required exactly once.");
            foreach(var e in entries)if(e.width<=0||e.height<=0||e.visualHeight<=0||!IsFinite(e.center.x)||!IsFinite(e.center.z))throw new InvalidOperationException("Invalid infill reservation: "+e.id);
            if(entries.Single(e=>e.id=="MI2501").buildingId!="767534280"||entries.Single(e=>e.id=="MI2501").edgeIndex!=3||entries.Where(e=>e.id!="MI2501").Any(e=>!string.IsNullOrEmpty(e.buildingId)))
                throw new InvalidOperationException("148 has a known frontage edge; 150/152 are explicitly unmapped estimated spans.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var northNormal=Outward(survey,"767534280");var southNormal=Outward(survey,"317180219");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build three January2025 infill facades");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
                go.transform.localRotation=Quaternion.LookRotation(entry.id=="MI2503"?southNormal:northNormal,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/MidWestInfill/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();width=entry.width;
                if(entry.id=="MI2501")Porter();else if(entry.id=="MI2502")Agang();else Lottery();
                BakeLettering();ValidateGeometry(entry);SaveMeshes(go.transform);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);return root;
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
            string path=Art+"/MidWestInfillLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/MidWestInfill_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="MidWestInfill_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Mid-west infill batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=pair.Key!="Wall" && pair.Key!="Inside" && pair.Key!="Floor";var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
