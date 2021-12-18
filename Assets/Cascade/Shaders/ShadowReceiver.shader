Shader "CustomShadow/Receiver"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Tags{ "LightMode" = "ForwardBase" }

            CGPROGRAM
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 worldPos : TEXCOORD0;
                float eyeZ : TEXCOORD1;
            };

            uniform float4x4 _gWorldToShadow;
            uniform sampler2D _gShadowMapTexture;
            uniform float4 _gShadowMapTexture_TexelSize;

            uniform float4 _gLightSplitsNear;
            uniform float4 _gLightSplitsFar;
            uniform float4x4 _gWorld2Shadow[4];

            uniform sampler2D _gShadowMapTexture0;
            uniform sampler2D _gShadowMapTexture1;
            uniform sampler2D _gShadowMapTexture2;
            uniform sampler2D _gShadowMapTexture3;

            uniform float _gShadowStrength;

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.eyeZ = o.pos.w;
                return o;
            }

            // Cascadeの重みを色として表示
            fixed4 GetCascadeWeights(float z)
            {
                fixed4 zNear = float4(z >= _gLightSplitsNear);
                fixed4 zFar = float4(z < _gLightSplitsFar);
                // 仮に2番目の視錐台の場合、(0,1,0,0)
                fixed4 weights = zNear * zFar;
                return weights;
            }

            // 影の計算
            float CulcShadow(float4x4 w2Shadow, float4 wPos, fixed cascadeWeight, sampler2D shadowMapTex)
            {
                // WVP行列に変換し、0~1の範囲に
                float4 shadowCoord = mul(w2Shadow, wPos);
                shadowCoord.xy /= shadowCoord.w;
                shadowCoord.xy = shadowCoord.xy * 0.5 + 0.5;

                float4 sampleDepth = tex2D(shadowMapTex, shadowCoord.xy);

                // 深度
                float depth = shadowCoord.z / shadowCoord.w;
                #if defined (SHADER_TARGET_GLSL)
                    depth = depth * 0.5 + 0.5;
                #elif defined (UNITY_REVERSED_Z)
                    depth = 1 - depth;
                #endif

                float shadow = sampleDepth < depth ? _gShadowStrength : 1;
                // どの分割した視錐台のものの影かを返す
                return shadow * cascadeWeight;
            }

            // ShadowTextureのサンプリング
            float4 SampleShadowTexture(float4 wPos, fixed4 cascadeWeights)
            {
                float cascadeShadow =
                    CulcShadow(_gWorld2Shadow[0], wPos, cascadeWeights[0], _gShadowMapTexture0) +
                    CulcShadow(_gWorld2Shadow[1], wPos, cascadeWeights[1], _gShadowMapTexture1) +
                    CulcShadow(_gWorld2Shadow[2], wPos, cascadeWeights[2], _gShadowMapTexture2) +
                    CulcShadow(_gWorld2Shadow[3], wPos, cascadeWeights[3], _gShadowMapTexture3);

                return cascadeShadow;// * cascadeWeights;
            }

            fixed4 frag (v2f i) : COLOR0
            {
                fixed4 weights = GetCascadeWeights(i.eyeZ);
                fixed4 col = SampleShadowTexture(i.worldPos, weights);
                return col;
            }

            #pragma vertex vert
            #pragma fragment frag
            #pragma fragmentoption ARB_precision_hint_fastest
            ENDCG
        }
    }
}
