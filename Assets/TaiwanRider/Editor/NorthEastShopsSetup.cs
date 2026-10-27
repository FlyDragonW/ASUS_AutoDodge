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
    // See Sources/north-east-2025-shops.json; publication dates are separate from capture dates.
    public static class NorthEastShopsSetup
    {
        public const string RootName = "North-east shops • observed January 2025";
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
        static readonly Vector3 EstimatedOutward = new Vector3(-.993948217f,0,-.109849634f);
        static void Materials()
        {
            M("Wall","#A9AA9F",0,.15f);M("Floor","#AAA99E",0,.16f);
            M("Frame","#3F4540",.20f,.27f);M("Steel","#A0A6A0",.47f,.36f);
            M("Inside","#CCC8B8",0,.20f);M("Paper","#E4E3D3",0,.24f);M("Ink","#242D2F",0,.18f);
            M("Wood","#8A6240",0,.18f);M("Light","#E9E7D1",0,.25f);M("Red","#AD3833",0,.22f);
            M("Gold","#C2A870",.13f,.25f);M("Blue","#345778",0,.21f);
            M("PosterBlue","#7DABB6",0,.19f);M("Cream","#D6CBAC",0,.20f);
            M("Purple","#733F83",0,.22f);M("Yellow","#DBC540",0,.22f);
            M("WarmGlass","#927C59",.10f,.39f);
            foreach(string key in new[]{"Light","Paper","Yellow"}) {
                var m=materials[key];m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Hex("#ACA58A")*(key=="Light"?.26f:.08f));
            }
        }
        static void Panel(string mat,float x,float y,float z,float w,float h,string text,string ink,float fontHeight)
        { B(mat,x,y,z,w,h,.075f);L(text,x,y,z+.046f,fontHeight,ink,w-.14f); }
        static void Circle(string mat,float x,float y,float z,float r)
        { Rod(mat,new Vector3(x,y,z-.012f),new Vector3(x,y,z+.012f),r,24); }
        static void Shell(float w,float h=3.12f)
        {
            B("Floor",0,.05f,-1.17f,w,.10f,2.78f);B("Inside",0,h*.5f,-2.52f,w,h,.12f);
            B("Inside",0,h,-1.17f,w,.13f,2.78f);
            foreach(float side in new[]{-1f,1f})B("Wall",side*(w*.5f-.055f),h*.5f,-1.18f,.11f,h,2.73f);
            for(int i=0;i<3;i++)B("Light",(i-1)*w*.26f,h-.10f,-1.12f,.045f,.025f,1.85f);
            for(int i=0;i<=Mathf.FloorToInt(w/.50f);i++)B("Steel",-w*.5f+i*.50f,.104f,-1.14f,.008f,.003f,2.65f);
        }
        static void GlassFront(float x,float w,float height=2.72f,bool doubleDoor=true)
        {
            B("StoreGlass",x,height*.5f+.13f,-.11f,w,height,.023f);
            foreach(float dx in new[]{-w*.5f,w*.5f,0f})B("Steel",x+dx,height*.5f+.13f,-.073f,.034f,height+.07f,.06f);
            foreach(float y in new[]{.13f,height+.13f})B("Steel",x,y,-.073f,w,.044f,.06f);
            if(doubleDoor)foreach(float dx in new[]{-.11f,.11f})B("Steel",x+dx,1.23f,.005f,.018f,.36f,.04f);
        }
        static void ProjectingSign(float x,float y,string mat,string text,string ink,float h,float depth,float fontHeight)
        {
            B("Frame",x,y,.20f,.12f,h+.1f,.55f);B(mat,x,y,.59f,.18f,h,depth);
            bool verticalLatin=!text.Contains("\n");
            foreach(float side in new[]{-1f,1f})Label(text,new Vector3(x+side*.099f,y,.59f),fontHeight,ink,verticalLatin?h-.18f:depth-.09f,side*90f,verticalLatin?90:0);
        }

        // Original simplified elephant silhouette: visible identity cue, not a copied bitmap logo.
        static void Elephant(float x,float y,float z,float size,string key="Paper",float yaw=0f)
        {
            var q=Quaternion.Euler(0,yaw,0);
            Vector3 P(float dx,float dy)=>new Vector3(x,y,z)+q*new Vector3(dx*size,dy*size,0);
            void R(float ax,float ay,float bx,float by,float r)=>Rod(key,P(ax,ay),P(bx,by),r*size,8);
            var front=q*Vector3.forward;
            Rod(key,P(-.03f,.02f)-front*.009f,P(-.03f,.02f)+front*.009f,.145f*size,16);
            Rod(key,P(-.18f,.04f)-front*.015f,P(-.18f,.04f)+front*.015f,.096f*size,12);
            R(-.245f,.015f,-.26f,-.12f,.037f);R(-.26f,-.12f,-.19f,-.15f,.033f);
            R(-.09f,-.06f,-.10f,-.19f,.033f);R(.07f,-.06f,.08f,-.19f,.033f);
            R(.105f,.055f,.20f,-.03f,.012f);
        }
        static void Poster(float x,float y,float z,float w,float h,string body="Paper")
        {
            B(body,x,y,z,w,h,.018f);
            B("Red",x,y+h*.37f,z+.014f,w*.86f,h*.12f,.008f);
            // Text below optical resolution remains neutral lines; no invented offers or prices.
            for(int i=0;i<4;i++)B("Cream",x,y+h*.16f-i*h*.11f,z+.014f,w*.77f,.018f,.008f);
        }
        static void Lotto()
        {
            Shell(width,3.05f);
            Panel("Yellow",0,3.55f,.05f,width,.83f,"台灣彩券","#625D2E",.46f);
            B("Paper",0,3.07f,.20f,width,.13f,.60f);
            for(int i=0;i<2;i++)B("Light",0,2.985f,.07f+i*.29f,width-.20f,.022f,.035f);
            float glazedWidth=width-.89f,cx=.35f;
            B("StoreGlass",cx,1.90f,-.10f,glazedWidth,1.77f,.023f);
            foreach(float x in new[]{cx-glazedWidth*.5f,cx,cx+glazedWidth*.5f})B("Steel",x,1.72f,-.069f,.034f,2.32f,.052f);
            foreach(float y in new[]{1.00f,2.80f})B("Steel",cx,y,-.069f,glazedWidth+.035f,.044f,.052f);
            Panel("Yellow",cx,.54f,-.023f,glazedWidth,.75f,"刮刮樂","#575C25",.36f);
            GlassFront(-width*.5f+.38f,.60f,2.61f);
            // Flat screen and paper notices behind the observed service window.
            B("Ink",.73f,2.29f,-.23f,1.15f,.63f,.07f);B("PosterBlue",.73f,2.29f,-.186f,1.08f,.54f,.014f);
            B("Yellow",.78f,2.25f,-.175f,.87f,.12f,.009f);
            Poster(-.12f,1.90f,-.18f,.49f,1.02f);Poster(.69f,1.43f,-.17f,.53f,.29f);
            B("Cream",.08f,1.005f,-.01f,width-.54f,.06f,.34f);
            B("Paper",0,.84f,-1.06f,width-.24f,.10f,.55f);
            // Two small red stools are visible beneath the exterior writing ledge.
            B("Cream",-.77f,1.00f,.14f,1.35f,.052f,.38f);
            foreach(float x in new[]{-1.14f,-.59f}) {
                B("Red",x,.51f,.46f,.32f,.046f,.29f);
                foreach(float dx in new[]{-.115f,.115f})foreach(float dz in new[]{-.105f,.105f})
                    Rod("Frame",new Vector3(x+dx,.09f,.46f+dz),new Vector3(x+dx,.49f,.46f+dz),.012f,6);
                B("Red",x,.66f,.57f,.32f,.028f,.023f);
                foreach(float dx in new[]{-.13f,.13f})B("Red",x+dx,.585f,.57f,.021f,.17f,.023f);
            }
            float bx=-width*.5f+.14f;
            B("Yellow",bx,3.20f,.63f,.17f,.43f,.89f);
            foreach(float side in new[]{-1f,1f}) {
                Box("Paper",new Vector3(bx+side*.090f,3.20f,.63f),new Vector3(.017f,.31f,.81f));
                Label("運動彩券",new Vector3(bx+side*.101f,3.20f,.63f),.15f,"#8B403D",.77f,side*90f);
            }
        }
        static void ThaiKitchen()
        {
            Shell(width,2.85f);
            B("Wood",0,3.18f,.035f,width,.68f,.18f);
            for(int i=0;i<5;i++)B("Ink",0,2.90f+i*.135f,.134f,width-.035f,.008f,.008f);
            L("泰國小館",.28f,3.20f,.147f,.39f,"#DBC78B",width-1.05f);
            Circle("Ink",-width*.5f+.43f,3.20f,.145f,.24f);
            Elephant(-width*.5f+.44f,3.21f,.174f,.97f);
            foreach(float side in new[]{-1f,1f})B("Wood",side*(width*.5f-.10f),1.45f,-.20f,.18f,2.83f,.30f);
            B("WarmGlass",0,1.52f,-1.65f,width-.29f,2.40f,.045f);
            GlassFront(.13f,width-.58f,2.56f);
            foreach(float x in new[]{-width*.28f,width*.25f})B("Wood",x,1.4f,-.066f,.055f,2.59f,.072f);
            B("Wood",0,.64f,-1.12f,width*.59f,.96f,.49f);
            B("Cream",0,1.145f,-1.08f,width*.66f,.060f,.59f);
            for(int i=0;i<5;i++) {
                float x=-width*.39f+i*width*.195f;
                B("Ink",x,2.70f,-.63f,.11f,.07f,.11f);B("Gold",x,2.655f,-.63f,.073f,.018f,.073f);
            }
            Poster(-.76f,1.76f,-.165f,.67f,1.10f,"Gold");
            B("Ink",width*.5f-.43f,1.04f,.11f,.51f,1.63f,.07f);
            for(int i=0;i<5;i++)B("Paper",width*.5f-.43f,1.56f-i*.19f,.151f,.35f,.014f,.008f);
            // Narrow purple menu side panel and two outdoor AC boxes are actually visible in the frame.
            float mx=-width*.5f+.28f;
            B("Purple",mx,2.10f,.08f,.48f,1.29f,.08f);
            for(int r=0;r<2;r++)for(int c=0;c<2;c++) {
                float x=mx-.10f+c*.20f,y=1.85f+r*.32f;
                B("Paper",x,y,.128f,.17f,.23f,.01f);Circle("Cream",x,y,.14f,.067f);
            }
            foreach(var p in new[]{new Vector2(-.58f,4.29f),new Vector2(.61f,3.98f)}) {
                B("Cream",p.x,p.y,.08f,.73f,.56f,.35f);Circle("Frame",p.x-.10f,p.y,.273f,.16f);
                for(int i=0;i<6;i++)B("Steel",p.x+.23f,p.y-.17f+i*.067f,.263f,.14f,.012f,.012f);
                B("Steel",p.x,p.y-.335f,.15f,.81f,.045f,.42f);
            }
            float sx=-width*.5f+.17f;
            B("Frame",sx,5.20f,.20f,.11f,2.95f,.55f);B("Purple",sx,5.20f,.62f,.18f,2.80f,.74f);
            foreach(float side in new[]{-1f,1f}) {
                Label("泰\n國\n小\n館",new Vector3(sx+side*.099f,5.02f,.62f),.31f,"#E7DED2",.64f,side*90f);
                Elephant(sx+side*.11f,6.29f,.62f,.80f,"Paper",side*90f);
            }
        }
        static void Spectacles(float x,float y,float z,float size)
        {
            foreach(float side in new[]{-1f,1f}) {
                Circle("Ink",x+side*.054f*size,y,z,.045f*size);
                Circle("PosterBlue",x+side*.054f*size,y,z+.012f,.033f*size);
            }
            B("Steel",x,y,z+.014f,.040f*size,.010f*size,.011f);
        }
        static void JingZhan()
        {
            Shell(width,2.95f);
            B("Ink",0,3.49f,.065f,width,.82f,.17f);
            // The campaign photography is represented only by observed colour regions, not copied faces/pixels.
            B("PosterBlue",width*.315f,3.49f,.159f,width*.35f,.68f,.014f);
            B("Paper",width*.32f,3.24f,.170f,width*.27f,.10f,.008f);
            L("YOU'RE ON",-.23f,3.51f,.163f,.31f,"#E0E3D8",width*.43f);
            Panel("Red",-width*.5f+.39f,3.50f,.159f,.65f,.46f,"Ray-Ban","#E8E5DA",.16f);
            B("Paper",0,2.99f,.04f,width,.12f,.35f);
            GlassFront(width*.20f,width*.53f,2.72f);GlassFront(-width*.29f,width*.40f,2.72f);
            B("Paper",width*.20f,.43f,-.031f,width*.53f,.63f,.09f);
            Panel("Blue",width*.20f,.46f,.023f,width*.45f,.39f,"精展眼鏡","#DADFD7",.17f);
            B("Paper",width*.20f,.79f,-.28f,width*.52f,.065f,.46f);
            // Bright rear display panels and rows of original generic frames match the visible shop depth.
            B("Paper",0,1.61f,-2.39f,width-.27f,2.44f,.075f);
            for(int row=0;row<5;row++) {
                float y=.72f+row*.37f;B("Cream",0,y-.05f,-2.29f,width-.40f,.023f,.19f);
                for(int col=0;col<7;col++)Spectacles(-width*.39f+col*width*.13f,y,-2.22f,1.1f);
            }
            for(int i=0;i<4;i++)Spectacles(width*.01f+i*width*.117f,.88f,-.16f,1.2f);
            Poster(-width*.34f,1.76f,-.20f,.44f,1.09f);
            ProjectingSign(width*.5f-.19f,5.27f,"Blue","精\n展\n眼\n鏡\n公\n司","#D3DAD4",2.76f,.74f,.25f);
        }
        static void ValidateBounds(string id,float reservedWidth,float visualHeight)
        {
            // Fail before creating assets if a modeled prop spills into an adjoining reserved bay.
            foreach(var pair in geometry)foreach(var p in pair.Value.vertices)
                if(Mathf.Abs(p.x)>reservedWidth*.5f+.012f || p.y<-.01f || p.y>visualHeight+.012f || p.z < -2.66f || p.z > 1.12f)
                    throw new InvalidOperationException(id+" geometry exceeds its declared local bounds: "+pair.Key+" "+p);
        }
        public static GameObject Build(Transform parent)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build north-east shops in Edit mode.");
            if(!parent)throw new ArgumentNullException(nameof(parent));
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("NotoSansTC is required.");
            var entries=SurveyStreetscapeSetup.LoadAdditionalFrontages().Where(e=>e.id!=null&&e.id.StartsWith("NE25",StringComparison.Ordinal)).OrderBy(e=>e.id).ToArray();
            var required=new[]{"NE2501","NE2502","NE2503"};
            if(entries.Length!=3 || !entries.Select(e=>e.id).SequenceEqual(required))throw new InvalidOperationException("Expected the three north-east January 2025 reservations NE2501–NE2503.");
            var survey=JsonUtility.FromJson<Survey>(File.ReadAllText("Assets/TaiwanRider/Data/StreetSurvey.json"));
            var old=parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName);if(old)Undo.DestroyObjectImmediate(old.gameObject);
            var root=new GameObject(RootName);root.layer=2;root.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(root,"Build January 2025 north-east shops");
            foreach(var entry in entries) {
                var go=new GameObject(entry.sceneName);go.layer=2;go.transform.SetParent(root.transform,false);go.transform.localPosition=entry.center;
                Vector3 normal=EstimatedOutward;
                if(!string.IsNullOrEmpty(entry.buildingId))normal=survey.buildings.First(b=>b.id==entry.buildingId).roadFrontEdges.First(e=>e.edgeIndex==entry.edgeIndex&&e.roadId=="316698035").outward;
                go.transform.localRotation=Quaternion.LookRotation(normal.normalized,Vector3.up);
                Art="Assets/TaiwanRider/Art/StreetSurvey/NorthEastShops/"+entry.id;Directory.CreateDirectory(Art);AssetDatabase.Refresh();
                materials.Clear();geometry.Clear();lettering.Clear();Materials();StoreGlass();width=entry.width;
                switch(entry.id) {case "NE2501":Lotto();break;case "NE2502":ThaiKitchen();break;case "NE2503":JingZhan();break;}
                BakeLettering();ValidateBounds(entry.id,entry.width,entry.visualHeight>0?entry.visualHeight:entry.height);SaveMeshes(go.transform);
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
            string path=Art+"/NorthEastShopLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"StoreGlass","Frame","Ink","Grout","GlassDark","Paper","Steel","Tile","TileWarm","Wood","Sign","Light","Blue","Red","Green","Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib","Gold","Purple","Yellow","WarmGlass","PosterBlue","Cream"};
            foreach(var pair in geometry)
            {
                if(pair.Value.vertices.Count==0)continue;
                var g=pair.Value;string path=Art+"/NorthEastShop_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="NorthEastShop_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("North-east shop batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=fineSurfaces.Contains(pair.Key);var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
