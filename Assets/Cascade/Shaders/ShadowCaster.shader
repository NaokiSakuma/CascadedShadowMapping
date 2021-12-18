Shader "CustomShadow/Caster"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        CGINCLUDE
        #include "UnityCG.cginc"
        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 depth:TEXCOORD0;
        };

        uniform float _gShadowBias;
        v2f vert (appdata_full v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.pos.z += _gShadowBias;
            o.depth = o.pos.zw;
            return o;
        }

        fixed4 frag (v2f i) : COLOR
        {
            // 正規化デバイス空間に
            float depth = i.depth.x / i.depth.y;

            // プラットフォーム毎の違いを吸収
            // shaderの言語がGLSLならtrue
        #if defined (SHADER_TARGET_GLSL)
            // (-1, 1)-->(0, 1)
            depth = depth * 0.5 + 0.5;
            // 深度値が反転しているか
            // DirectX 11, DirectX 12, PS4, Xbox One, Metal: 逆方向
        #elif defined (UNITY_REVERSED_Z)
            //(1, 0)-->(0, 1)
            depth = 1 - depth;
        #endif
            return depth;
        }
        ENDCG

        Pass
        {
            Cull front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
