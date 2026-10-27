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
    // See Sources/north-west-infill-2025.json. Visual frontage extension only; driving scope is unchanged.
    public static class NorthWestInfillSetup
    {
        public const string RootName = "Northwest infill • filmed January 2025";
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
            public string text; public Vector3 position; public float height, width, yaw, roll, slant; public Color color;
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
        [Serializable] sealed class Building { public string id; public Edge[] roadFrontEdges; public Vector3[] points; }
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
            M("Wall","#A8A398",0,.16f);M("Floor","#AAA797",0,.14f);M("Inside","#CECBBC",0,.18f);
            M("Frame","#303934",.18f,.24f);M("Steel","#929C96",.45f,.36f);M("GlassDark","#2E3E39",.08f,.43f);
            M("Paper","#E8E4D5",0,.20f);M("Ink","#202B24",0,.18f);M("Wood","#A18461",0,.19f);
            M("Light","#ECE5CC",0,.23f);M("Red","#A84239",0,.20f);M("Cream","#CBB992",0,.18f);
            M("LanYellow","#D3BB38",0,.21f);M("LanBlue","#24468B",0,.22f);M("Orange","#B96135",0,.22f);
            M("Navy","#263946",0,.20f);M("Gold","#C6BD7F",0,.20f);M("Shutter","#657570",.23f,.24f);M("ShutterRib","#40544C",.26f,.23f);
            M("NarrowSignBlue","#A5B4D0",0,.20f);
            M("GarmentBlue","#657C83",0,.14f);M("GarmentRose","#886368",0,.14f);M("GarmentOlive","#848276",0,.14f);
            var m=materials["Light"];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex("#AAAB94")*.30f);
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        {B(mat,x,y,z,w,h,.077f);if(!string.IsNullOrEmpty(text))L(text,x,y,z+.045f,fontHeight,ink,w-.15f);}
        static void Oval(string mat,float x,float y,float z,float rx,float ry)
        {
            var g=G(mat);Vector3 mid=new Vector3(x,y,z+.018f),back=new Vector3(x,y,z-.018f);
            for(int i=0;i<32;i++){
                float a=i*2*Mathf.PI/32,b=(i+1)*2*Mathf.PI/32;
                var p=new Vector3(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry,z+.018f);var q=new Vector3(x+Mathf.Cos(b)*rx,y+Mathf.Sin(b)*ry,z+.018f);
                g.Quad(mid,p,q,mid);g.Quad(back,q-Vector3.forward*.036f,p-Vector3.forward*.036f,back);g.Quad(p,p-Vector3.forward*.036f,q-Vector3.forward*.036f,q);
            }
        }
        static void Shell(float h=3.08f,float depth=2.14f)
        {
            B("Floor",0,.045f,-depth*.45f,width,.09f,depth+.38f);B("Inside",0,h*.5f,-depth,width,h,.09f);
            B("Inside",0,h,-depth*.45f,width,.10f,depth+.38f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(width*.5f-.047f),h*.5f,-depth*.49f,.094f,h,depth);
        }
        static void Glass(float x,float w,float h=2.73f)
        {
            B("StoreGlass",x,h*.5f+.13f,-.10f,w,h,.022f);
            foreach(float dx in new[]{-w*.5f,0f,w*.5f})B("Steel",x+dx,h*.5f+.13f,-.066f,.032f,h+.07f,.063f);
            foreach(float y in new[]{.13f,h+.13f})B("Steel",x,y,-.066f,w,.041f,.063f);
        }
        static void Menu(float x,float y,float z,float w,float h)
        {
            Panel("Paper",x,y,z,w,h,"","#FFFFFF",.1f);
            for(int row=0;row<7;row++)for(int col=0;col<3;col++)B(row%3==0?"Red":"Ink",x-w*.32f+col*w*.31f,y+h*.37f-row*h*.12f,z+.045f,w*.20f,.014f,.006f);
        }
        static void Counter(float x,float w,string mat,float z=-.68f)
        {
            B(mat,x,.66f,z,w,1.16f,.62f);B("Paper",x,1.29f,z+.04f,w+.07f,.075f,.78f);
            for(int i=0;i<5;i++)Rod("Paper",new Vector3(x-w*.31f+i*.14f,1.334f,z+.13f),new Vector3(x-w*.31f+i*.14f,1.50f,z+.13f),.043f,10);
            B("GlassDark",x+w*.30f,1.48f,z+.08f,.27f,.27f,.18f);
        }
        static void VerticalTextSign(float x,float y,string mat,string text,string ink,float height,float depth,float fontHeight)
        {
            B("Frame",x,y,.17f,.10f,height+.08f,.56f);B(mat,x,y,.56f,.18f,height,depth);
            foreach(float side in new[]{-1f,1f})Label(text,new Vector3(x+side*.098f,y,.56f),fontHeight,ink,depth-.08f,side*90f);
        }
        static void JinFaTea()
        {
            Shell(3.10f,2.12f);
            Panel("Paper",0,3.67f,.08f,width,.87f,"","#FFFFFF",.1f);
            L("進發家",width*.03f,3.67f,.126f,.50f,"#303B2E",width*.65f);
            B("Red",-width*.365f,3.61f,.127f,.17f,.22f,.012f);
            // Seal characters are unresolved; the small red block is intentionally unlettered.
            foreach(float side in new[]{-1f,1f})B("Cream",side*(width*.5f-.10f),1.56f,-.025f,.20f,3.10f,.29f);
            B("Wood",0,3.14f,.025f,width,.14f,.25f);
            Counter(-width*.09f,width*.68f,"Paper");
            Glass(width*.34f,width*.22f,2.77f);
            Menu(-width*.25f,2.10f,-1.63f,width*.28f,.99f);
            B("Paper",width*.12f,1.83f,-1.93f,width*.30f,2.02f,.13f);
            for(int i=0;i<4;i++)B("Light",(i-1.5f)*width*.20f,3.01f,-.51f,.057f,.025f,.57f);
            // January tea-shop pixels supersede the current mobile-phone POI category.
        }
        static void FiftyLan()
        {
            Shell(3.10f,1.97f);
            Panel("LanYellow",0,3.67f,.08f,width,.85f,"","#FFFFFF",.1f);
            Oval("LanBlue",0,3.67f,.143f,width*.27f,.29f);L("50嵐",0,3.67f,.167f,.40f,"#EFEACE",width*.47f);
            foreach(float side in new[]{-1f,1f})B("Cream",side*(width*.5f-.125f),1.58f,.008f,.25f,3.15f,.35f);
            B("GlassDark",0,1.92f,-1.90f,width-.40f,2.01f,.05f);
            Counter(0,width-.58f,"Cream",-.71f);Menu(width*.23f,2.20f,-1.11f,width*.24f,.83f);
            B("Light",0,3.0f,-.34f,width*.62f,.022f,.047f);
            float sx=width*.5f-.17f;
            B("Frame",sx,5.84f,.18f,.12f,2.75f,.52f);B("LanYellow",sx,5.84f,.56f,.18f,2.71f,.65f);
            foreach(float side in new[]{-1f,1f}){
                Rod("LanBlue",new Vector3(sx+side*.093f,6.67f,.56f),new Vector3(sx+side*.106f,6.67f,.56f),.22f,24);
                Label("50嵐",new Vector3(sx+side*.114f,6.67f,.56f),.105f,"#F1EBCE",.36f,side*90f);
            }
            // January 03:18 has a separate pale-blue narrow blade between these two shops.
            // Its owner/wording is unresolved. Attach geometry to this existing tall-sign envelope
            // at viewer-right (negative local X), without attributing another business name.
            float adjacentX=-width*.5f+.13f;
            B("Frame",adjacentX,5.79f,.18f,.09f,2.40f,.48f);
            B("Navy",adjacentX,5.79f,.55f,.14f,2.38f,.52f);
            foreach(float side in new[]{-1f,1f}){
                B("NarrowSignBlue",adjacentX+side*.075f,5.79f,.55f,.009f,2.29f,.46f);
                for(int i=0;i<12;i++)B("Navy",adjacentX+side*.083f,6.78f-i*.173f,.55f+(i%2==0?-.016f:.016f),.006f,.028f,.14f+(i%3)*.017f);
            }
        }
        static void RiXinCombined()
        {
            Shell(3.10f,.81f);
            Panel("Navy",0,3.62f,.07f,width,.78f,"日新水電衛生材料有限公司","#CFC791",.31f);
            foreach(float x in new[]{-width*.5f+.08f,0f,width*.5f-.08f})B("Wall",x,1.53f,.04f,.16f,3.06f,.29f);
            // One photographed business across adjoining closed bays; numbers118/120 are not separately measured.
            float bay=width*.5f-.22f;
            foreach(float side in new[]{-1f,1f}){
                float x=side*width*.25f;B("Shutter",x,1.49f,-.035f,bay,2.78f,.075f);
                for(int row=0;row<31;row++)B("ShutterRib",x,.14f+row*.087f,.011f,bay-.02f,.016f,.017f);
                B("Frame",x,2.94f,.004f,bay+.06f,.13f,.13f);
            }
        }
        static void Garment(float x,float y,float z,int index)
        {
            string cloth=index%4==0?"GarmentBlue":index%4==1?"GarmentRose":index%4==2?"GarmentOlive":"Paper";
            Rod("Steel",new Vector3(x,y+.09f,z),new Vector3(x-.14f,y,z),.007f,6);Rod("Steel",new Vector3(x,y+.09f,z),new Vector3(x+.14f,y,z),.007f,6);
            float length=.56f+(index%3)*.075f;
            B(cloth,x,y-length*.5f,z,.26f,length,.038f);
            Box(cloth,new Vector3(x-.165f,y-.07f,z),new Vector3(.13f,.20f,.037f),Quaternion.Euler(0,0,-30));
            Box(cloth,new Vector3(x+.165f,y-.07f,z),new Vector3(.13f,.20f,.037f),Quaternion.Euler(0,0,30));
        }
        static void Rack(float x,float z,float w,int count)
        {
            float half=w*.5f;
            foreach(float side in new[]{-1f,1f}){
                Rod("Steel",new Vector3(x+side*half,.13f,z),new Vector3(x+side*half,1.73f,z),.014f,8);
                Rod("Steel",new Vector3(x+side*half,.12f,z-.22f),new Vector3(x+side*half,.12f,z+.22f),.017f,8);
            }
            Rod("Steel",new Vector3(x-half,1.73f,z),new Vector3(x+half,1.73f,z),.016f,8);
            for(int i=0;i<count;i++)Garment(x-half+.20f+i*(w-.40f)/Mathf.Max(1,count-1),1.59f,z,i);
        }
        static void GoGoJanuary()
        {
            Shell(3.06f,2.11f);
            Panel("Ink",0,3.67f,.07f,width,.92f,"","#FFFFFF",.1f);
            lettering.Add(new Lettering{text="GoGo",position=new Vector3(0,3.66f,.118f),height=.57f,width=width*.65f,color=Hex("#E9E7D9"),slant=.18f});
            // Small slogan under the italic mark is not readable; no laterShopee branding is introduced.
            B("Orange",0,3.08f,.24f,width,.18f,.95f);
            for(int i=0;i<7;i++)Rod("Light",new Vector3(-width*.41f+i*width*.82f/6,2.978f,.54f),new Vector3(-width*.41f+i*width*.82f/6,2.995f,.54f),.037f,12);
            Glass(0,width-.24f,2.72f);B("Steel",0,1.42f,-.034f,.018f,.42f,.027f);
            Rack(-width*.255f,.31f,width*.42f,7);Rack(width*.255f,.31f,width*.42f,7);
            Rack(0,-1.78f,width*.73f,10);
        }
        static void HeJiaRamen()
        {
            Shell(3.10f,1.98f);
            Panel("Paper",0,3.65f,.08f,width,.85f,"何家拉麵","#303A33",.48f);
            B("Paper",0,3.11f,.25f,width,.13f,.84f);
            for(int i=0;i<6;i++)Rod("Light",new Vector3(-width*.40f+i*width*.80f/5,3.033f,.48f),new Vector3(-width*.40f+i*width*.80f/5,3.044f,.48f),.028f,10);
            Glass(0,width-.35f,2.71f);B("Steel",.10f,1.34f,-.01f,.018f,.43f,.029f);
            Panel("Paper",-width*.14f,2.72f,.28f,width*.52f,.31f,"","#FFFFFF",.1f);
            Oval("Red",-width*.31f,2.72f,.326f,.088f,.088f);for(int i=0;i<3;i++)B("Frame",-width*.08f+i*.15f,2.72f,.326f,.065f,.025f,.007f);
            B("Wood",0,.77f,-1.54f,width*.57f,.059f,.48f);B("GlassDark",0,.41f,-1.62f,width*.64f,.56f,.20f);
            Menu(width*.29f,1.90f,-1.30f,width*.23f,1.04f);
            VerticalTextSign(width*.5f-.16f,5.72f,"Paper","何\n家\n拉\n麵","#354138",2.69f,.67f,.31f);
        }
        static void GarmentSliver()
        {
            Shell(3.06f,1.02f);B("GlassDark",0,1.43f,-.94f,width-.16f,2.66f,.058f);
            B("Frame",0,3.33f,-.045f,width,.46f,.14f);
            Rack(0,.045f,width-.22f,4);
            B("Paper",width*.5f-.15f,4.32f,.13f,.22f,1.24f,.10f);
            // NoQureshi lettering: the source only resolves a narrow garment strip and a pale sign.
        }
        static Vector3 NormalFor(string id,Survey combined)
        {
            string building=(id=="NX2501"||id=="NX2502")?"1287937531":id=="NX2506"?"751353368":"1287937530";
            int index=id=="NX2506"?1:2;var source=combined.buildings.First(b=>b.id==building);
            var edge=source.roadFrontEdges?.FirstOrDefault(e=>e.edgeIndex==index&&e.roadId=="316698035");if(edge!=null)return edge.outward;
            // Backdrop geometry is equally real OSM evidence, but has no precomputed edge metadata.
            var a=source.points[index];var b=source.points[(index+1)%source.points.Length];var delta=(b-a).normalized;
            Vector3 normal=new Vector3(delta.z,0,-delta.x).normalized;
            var centroid=source.points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/source.points.Length;
            return Vector3.Dot(normal,(a+b)*.5f-centroid)>=0?normal:-normal;
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build shop studies in Edit mode.");if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("NX25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            if(entries.Length!=6||!new[]{"NX2501","NX2502","NX2503","NX2504","NX2505","NX2506"}.SequenceEqual(entries.Select(e=>e.id)))throw new InvalidOperationException("Merge six NX25 reservations before building northwestern infill.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));var backdrop=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetBackdrop.json"));
            var combined=new Survey{buildings=survey.buildings.Concat(backdrop.buildings).ToArray()};
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January 2025 northwest infill");
            foreach(var entry in entries){
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;go.transform.localRotation=Quaternion.LookRotation(NormalFor(entry.id,combined),Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/NorthWestInfill/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id){case "NX2501":JinFaTea();break;case "NX2502":FiftyLan();break;case "NX2503":RiXinCombined();break;case "NX2504":GoGoJanuary();break;case "NX2505":HeJiaRamen();break;case "NX2506":GarmentSliver();break;}
                if(lettering.Count>0)BakeLettering();float top=geometry.Values.SelectMany(g=>g.vertices).Max(v=>v.y);
                if(top>Mathf.Max(entry.height,entry.visualHeight)+.025f)throw new InvalidOperationException(entry.id+" exceeds visualHeight.");SaveMeshes(go.transform);
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
                            Vector3 P(float px,float py)=>item.position+rotation*new Vector3((px+(py-middle)*item.slant)*scale,(py-line*lineHeight-middle)*scale,0);
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
            string path=Art+"/NorthWestInfillLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"Navy","LanBlue","LanYellow","Orange","Cream","Shutter","ShutterRib","GarmentBlue","GarmentRose","GarmentOlive","Gold","WoodDark","CaneGreen","CaneDark","WishGreen","ToyBlue","ToyPink","StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/NorthWestInfill_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="NorthWestInfill_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Northwest infill batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=fineSurfaces.Contains(pair.Key)||pair.Key=="NarrowSignBlue";var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
