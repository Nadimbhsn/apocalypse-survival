// Unlit shader for the Kenney 3D props in the 2D runner. The scene has no real lights,
// so shape comes from a fixed fake directional light on the world normal. Each layer
// (near / mid / far ...) gets its own material with a fog amount that blends toward the
// current sky color (global _SurvivalFogColor, set every frame by SurvivalDirector), a
// desaturation that pulls Kenney's bright palette toward the game's ashen mood, and a
// tint. _Flash (set per renderer through a MaterialPropertyBlock) whitens hit zombies.
// Lives in Resources so Shader.Find keeps it in player builds.
Shader "Survival/KenneyProp"
{
    Properties
    {
        _MainTex ("Colormap", 2D) = "white" {}
        _Tint ("Tint", Float) = 1
        _Fog ("Fog", Range(0, 1)) = 0
        _Desat ("Desaturate", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half _Tint;
            half _Fog;
            half _Desat;
            half _Flash;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _SurvivalFogColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 lightDir = normalize(float3(-0.4, 0.8, -0.45));
                half diffuse = saturate(dot(normalize(i.normalWS), lightDir));
                c *= (0.55h + 0.45h * diffuse) * _Tint;
                half lum = dot(c, half3(0.3h, 0.59h, 0.11h));
                c = lerp(c, lum.xxx, _Desat);
                c = lerp(c, _SurvivalFogColor.rgb, _Fog);
                c = lerp(c, half3(1.0h, 1.0h, 1.0h), _Flash);
                return half4(c, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
