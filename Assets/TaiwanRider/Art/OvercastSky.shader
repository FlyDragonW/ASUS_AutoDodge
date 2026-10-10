Shader "TaiwanRider/OvercastSky"
{
 Properties { _Horizon("Horizon",Color)=(.65,.70,.70,1) _Zenith("Zenith",Color)=(.38,.44,.47,1) }
 SubShader {
  Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct v2f { float4 vertex:SV_POSITION; float3 direction:TEXCOORD0; };
   float4 _Horizon,_Zenith;
   v2f vert(appdata_base v) { v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.direction=v.vertex.xyz;return o; }
   float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
   float noise(float2 p) {float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 frag(v2f i):SV_Target {
    float3 d=normalize(i.direction);float h=saturate(d.y);
    float2 p=d.xz/(max(d.y,.12))*2;
    float n=noise(p)*.6+noise(p*2.13)*.27+noise(p*4.7)*.13;
    float3 color=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.65));
    color+=(n-.5)*.10*smoothstep(0,.3,h);return half4(color,1);
   }
   ENDHLSL
  }
 }
}
