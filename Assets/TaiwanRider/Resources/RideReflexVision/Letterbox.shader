Shader "Hidden/RideReflex/Letterbox"
{
    Properties { _MainTex ("RGB camera", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float y = (i.uv.y - 0.21875) / 0.5625;
                if (y < 0 || y > 1) return float4(114.0/255.0, 114.0/255.0, 114.0/255.0, 1);
                float3 rgb = tex2D(_MainTex, float2(i.uv.x, y)).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = LinearToGammaSpace(rgb);
                #endif
                return float4(rgb, 1);
            }
            ENDCG
        }
    }
}
