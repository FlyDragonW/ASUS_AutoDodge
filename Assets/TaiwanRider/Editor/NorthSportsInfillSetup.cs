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
    // See Sources/north-sports-infill-2025.json; January2025 filming is author-reported.
    // A visibly continuous sports frontage is retained without attributing a new business or address.
    public static class NorthSportsInfillSetup
    {
        public const string RootName = "North sports infill • filmed January 2025";
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
        [Serializable] sealed class Building { public string id; public Edge[] roadFrontEdges; }
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
            M("Wall","#B2B4AF",0,.17f);M("Floor","#B2B0A6",0,.18f);M("Inside","#D5D0C1",0,.19f);
            M("Frame","#313634",.14f,.30f);M("Steel","#969E99",.43f,.40f);M("GlassDark","#293531",.06f,.43f);
            M("Paper","#F0EDDE",0,.22f);M("Ink","#202524",0,.2f);M("Wood","#9B876B",0,.18f);
            M("Red","#B62F33",0,.23f);M("Yellow","#D5C983",0,.20f);M("Orange","#D7772D",0,.23f);M("Cream","#D7D0A9",0,.23f);M("BrandBlue","#234773",0,.24f);
            M("ClothBlue","#315789",0,.12f);M("ClothNavy","#283945",0,.12f);M("ClothLime","#ADC44C",0,.12f);
            M("ClothRose","#B97E82",0,.12f);M("Rubber","#D5D4C9",0,.17f);M("Light","#EFEBD7",0,.22f);
            materials["Light"].EnableKeyword("_EMISSION");materials["Light"].SetColor("_EmissionColor",Hex("#ACA990").linear*.40f);
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.09f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.078f,fontHeight,ink,w-.15f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.01f),new Vector3(x,y,z+.01f),r,20); }
        static void Frame(Action draw,Vector3 origin,Quaternion rotation)
        {
            var counts=geometry.ToDictionary(p=>p.Key,p=>p.Value.vertices.Count);int labelStart=lettering.Count;
            draw();
            foreach(var batch in geometry){int from=counts.TryGetValue(batch.Key,out var n)?n:0;
                for(int i=from;i<batch.Value.vertices.Count;i++)batch.Value.vertices[i]=origin+rotation*batch.Value.vertices[i];}
            for(int i=labelStart;i<lettering.Count;i++){lettering[i].position=origin+rotation*lettering[i].position;lettering[i].yaw+=rotation.eulerAngles.y;}
        }
        static void BirdMark(float x,float y,float z,float scale)
        {
            // Two tapered curved wings, each tessellated as a strip rather than a concave fan.
            // Source03:16 resolves both wings. Image-right is negative localX on this facade.
            var g=G("Paper");
            Vector3 P(Vector2 v)=>new Vector3(x-v.x*scale,y+v.y*scale,z);
            void Tri(Vector3 a,Vector3 b,Vector3 c){
                float facing=Vector3.Cross(b-a,c-a).z;
                if(Mathf.Abs(facing)<.0000001f)return;
                if(facing<0f){var swap=b;b=c;c=swap;}
                g.Quad(a,b,c,c);
            }
            void Wing(Vector2[] lower,Vector2[] upper){
                for(int i=0;i<lower.Length-1;i++){
                    var a=P(lower[i]);var b=P(upper[i]);var c=P(upper[i+1]);var d=P(lower[i+1]);
                    Tri(a,b,c);Tri(a,c,d);
                }
            }
            Wing(new[]{new Vector2(-.09f,-.09f),new Vector2(-.015f,-.05f),new Vector2(.065f,-.011f),new Vector2(.155f,.045f),new Vector2(.26f,.126f),new Vector2(.46f,.285f)},
                 new[]{new Vector2(-.04f,0),new Vector2(.035f,.02f),new Vector2(.13f,.063f),new Vector2(.24f,.125f),new Vector2(.36f,.20f),new Vector2(.46f,.285f)});
            Wing(new[]{new Vector2(-.09f,-.09f),new Vector2(-.155f,-.022f),new Vector2(-.25f,.026f),new Vector2(-.345f,.072f),new Vector2(-.42f,.20f)},
                 new[]{new Vector2(-.04f,0),new Vector2(-.105f,.08f),new Vector2(-.21f,.105f),new Vector2(-.33f,.14f),new Vector2(-.42f,.20f)});
        }
        static void Garment(float x,float y,float z,int index)
        {
            string mat=index%6==0?"ClothLime":index%6==1?"ClothRose":index%6==2?"ClothBlue":index%6==3?"Paper":index%6==4?"ClothNavy":"Ink";
            Frame(()=>{
                Rod("Steel",new Vector3(0,.16f,0),new Vector3(-.18f,.06f,0),.007f,6);
                Rod("Steel",new Vector3(0,.16f,0),new Vector3(.18f,.06f,0),.007f,6);
                // One closed shirt extrusion, tessellated into non-overlapping front/back regions.
                // The former torso/sleeve boxes and adjacent shirts shared coplanar coloured faces.
                const float t=.018f;
                var g=G(mat);
                Vector2 a0=new Vector2(-.14f,.045f),b0=new Vector2(.14f,.045f);
                Vector2 c0=new Vector2(.1465625f,-.235f),d0=new Vector2(.155f,-.595f);
                Vector2 e0=new Vector2(-.155f,-.595f),f0=new Vector2(-.1465625f,-.235f);
                Vector2 lt=new Vector2(-.24f,-.03f),lb=new Vector2(-.31f,-.28f);
                Vector2 rt=new Vector2(.24f,-.03f),rb=new Vector2(.31f,-.28f);
                Vector3 V(Vector2 p,float depth)=>new Vector3(p.x,p.y,depth);
                void Face(Vector2 a,Vector2 b,Vector2 c,Vector2 d){
                    g.Quad(V(a,t),V(b,t),V(c,t),V(d,t));
                    g.Quad(V(d,-t),V(c,-t),V(b,-t),V(a,-t));
                }
                Face(f0,c0,b0,a0);Face(e0,d0,c0,f0);
                Face(a0,lt,lb,f0);Face(b0,c0,rb,rt);
                Vector2[] outline={a0,lt,lb,f0,e0,d0,c0,rb,rt,b0};
                for(int i=0;i<outline.Length;i++){
                    var a=outline[i];var b=outline[(i+1)%outline.Length];
                    g.Quad(V(a,-t),V(b,-t),V(b,t),V(a,t));
                }
            },new Vector3(x,y,z),Quaternion.Euler(0,58f,0));
        }
        static void ClothingRail(float x,float z,float w,int variation)
        {
            float y=1.69f;
            Rod("Steel",new Vector3(x-w*.5f,y,z),new Vector3(x+w*.5f,y,z),.018f,8);
            foreach(float side in new[]{-1f,1f}){
                Rod("Frame",new Vector3(x+side*w*.5f,.20f,z),new Vector3(x+side*w*.5f,y,z),.019f,8);
                Rod("Frame",new Vector3(x+side*w*.5f,.17f,z-.27f),new Vector3(x+side*w*.5f,.17f,z+.27f),.020f,8);
                foreach(float dz in new[]{-.23f,.23f})Rod("Ink",new Vector3(x+side*w*.5f-.018f,.13f,z+dz),new Vector3(x+side*w*.5f+.018f,.13f,z+dz),.039f,10);
            }
            int count=Mathf.Max(4,Mathf.FloorToInt(w/.26f));
            for(int i=0;i<count;i++)Garment(x-w*.39f+i*w*.78f/(count-1),y-.18f,z,variation+i);
        }
        static void Shoe(float x,float y,float z,int index)
        {
            string upper=index%3==0?"ClothBlue":index%3==1?"Paper":"Ink";
            B("Rubber",x,y+.025f,z,.17f,.05f,.33f);B(upper,x,y+.092f,z+.018f,.15f,.09f,.29f);
            B(upper,x,y+.15f,z-.067f,.13f,.085f,.12f);
            for(int i=0;i<3;i++)B("Paper",x,y+.144f,z+.018f+i*.028f,.069f,.009f,.012f);
        }
        static void SupportedBlade(float x,float y,string color,string text,float w,float h,bool bird=false)
        {
            // Two metal supports attach the blade directly to the observed fascia/pier.
            foreach(float dy in new[]{-.27f,.27f})B("Frame",x,y+dy,.36f,.055f,.055f,.74f);
            B("Paper",x,y,.79f,.105f,h+.08f,w+.08f);
            foreach(float side in new[]{-1f,1f})Frame(()=>{
                B(color,0,0,.010f,w,h,.018f);
                if(bird){BirdMark(0,.12f,.025f,1.24f);L(text,0,-.23f,.026f,.16f,"#E9EBDD",w-.10f);}
                else L(text,0,.035f,.026f,.235f,"#F0EEE2",w-.12f);
            },new Vector3(x+side*.059f,y,.79f),Quaternion.Euler(0,side*90f,0));
        }
        static void SportsFacade()
        {
            // Local +X is north; only this8m southern portion is reconstructed.
            B("Floor",0,.075f,-1.35f,width,.12f,3.06f);
            B("Inside",0,1.65f,-2.90f,width,3.30f,.10f);
            B("Inside",0,3.28f,-1.35f,width,.13f,3.06f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.07f),1.65f,-1.34f,.14f,3.30f,3.08f);
            B("Frame",0,.15f,.13f,width,.07f,.11f);
            B("Wall",0,3.47f,.04f,width,.26f,.31f);
            B("Frame",0,4.70f,.01f,width,.12f,.29f);
            // Image03:13/16 establishes the order. The cropped farther-north board is omitted.
            float[] xx={3.17f,1.65f,.35f,-.74f,-1.99f,-3.29f};
            float[] ww={1.66f,1.38f,1.18f,1.00f,1.50f,1.08f};
            string[] colors={"BrandBlue","Orange","BrandBlue","Orange","Ink","BrandBlue"};
            string[] texts={"Mizuno","Timberland","asics","","adidas","SKECHERS"};
            for(int i=0;i<xx.Length;i++){
                B(colors[i],xx[i],4.09f,.06f,ww[i],1.13f,.20f);
                if(i==0){BirdMark(xx[i],4.23f,.167f,1.36f);L(texts[i],xx[i],3.87f,.168f,.21f,"#EDEDE3",ww[i]-.16f);}
                else if(!string.IsNullOrEmpty(texts[i]))L(texts[i],xx[i],4.09f,.168f,i==5?.19f:.235f,"#EFEDE3",ww[i]-.16f);
            }
            // Orange intervening logo is too cropped to transcribe; its observed colour remains.
            B("Paper",0,3.31f,.18f,width,.12f,.57f);
            foreach(float z in new[]{-.015f,.35f})B("Light",0,3.239f,z,width-.22f,.025f,.043f);
            B("StoreGlass",.52f,1.70f,-.44f,6.70f,2.79f,.024f);
            foreach(float x in new[]{-2.83f,-.61f,1.61f,3.87f})B("Steel",x,1.70f,-.415f,.045f,2.85f,.07f);
            foreach(float y in new[]{.275f,3.125f})B("Steel",.52f,y,-.415f,6.74f,.045f,.07f);
            B("Wall",-3.40f,1.66f,-.15f,.97f,3.07f,.19f);
            Panel("Yellow",-3.44f,1.98f,-.041f,.79f,1.12f,"團體訂購\n球衣","#2C332B",.21f);
            // Original clothes/shoes are anonymous silhouettes arranged as the observed retail display.
            ClothingRail(2.43f,.045f,2.10f,0);
            ClothingRail(.15f,.065f,2.18f,3);
            ClothingRail(-1.98f,.028f,1.44f,5);
            ClothingRail(.70f,-1.84f,3.16f,2);
            for(int row=0;row<4;row++){
                float y=.41f+row*.53f;
                B("Paper",.55f,y,-2.64f,6.22f,.037f,.40f);
                for(int col=0;col<13;col++)Shoe(-2.24f+col*.46f,y+.028f,-2.54f,row+col);
            }
            B("Frame",3.82f,3.10f,-.015f,.19f,.78f,.09f);
            SupportedBlade(3.82f,3.10f,"Red","Coleman",1.08f,.61f);
            SupportedBlade(-3.61f,4.00f,"BrandBlue","Mizuno",.98f,.97f,true);
            // Blue portable notice has no guessed price or retailer name.
            B("BrandBlue",-3.42f,.91f,.27f,.46f,1.12f,.09f);
            B("Paper",-3.42f,1.25f,.321f,.35f,.27f,.012f);
            for(int i=0;i<3;i++)B("Paper",-3.42f,.84f-i*.14f,.321f,.29f,.028f,.009f);
            B("Steel",-3.42f,.285f,.27f,.035f,.30f,.035f);
            B("BrandBlue",-3.42f,.15f,.27f,.59f,.07f,.42f);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build north sports infill in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id=="NS2501").ToArray();
            if(entries.Length!=1)throw new InvalidOperationException("Merge NS2501 reservation before building north sports infill.");
            var entry=entries[0];if(entry.buildingId!="580823989"||entry.edgeIndex!=3||Mathf.Abs(entry.width-8f)>.01f)throw new InvalidOperationException("NS2501 requires580823989 edge3 southern8m.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var edge=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==3&&e.roadId=="316698035");
            var along=(edge.b-edge.a).normalized;var normal=new Vector3(along.z,0,-along.x).normalized;
            if(Vector3.Distance(entry.center,edge.b-along*4.12f)>.02f)throw new InvalidOperationException("NS2501 must leave.12m at south host end.");
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build observed north sports facade");
            var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(normal,Vector3.up);
            Art="Assets/TaiwanRider/Art/StreetSurvey/NorthSportsInfill/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
            materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;SportsFacade();BakeLettering();
            var vertices=geometry.Values.SelectMany(g=>g.vertices).ToArray();
            if(vertices.Any(v=>Mathf.Abs(v.x)>4.025f||v.y<0f||v.y>4.85f||v.z>1.38f||v.z< -2.98f))throw new InvalidOperationException("NS2501 exceeds its agreed slice/depth/height.");
            if(vertices.Max(v=>v.y)>Mathf.Max(entry.height,entry.visualHeight)+.015f)throw new InvalidOperationException("NS2501 exceeds its reservation height.");
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
            string path=Art+"/NorthSportsInfillLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/NorthSportsInfill_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="NorthSportsInfill_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("North sports batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
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
