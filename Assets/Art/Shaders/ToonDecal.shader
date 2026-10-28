// FaceSlapper 通用贴花着色器（URP）：
// 形状由贴图 Alpha 决定，与面片形状无关——任何 Quad/平面都能当贴花用。
// 特性：
// - 无光照、透明混合、不写深度、深度偏移防 Z-Fighting
// - 两种模式：硬边裁剪（Alpha Test，默认）/ 柔边混合（Alpha Blend）
// - 剔除模式可在材质面板上调整
Shader "FaceSlapper/ToonDecal"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Decal Map (形状由 Alpha 决定)", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_ALPHABLEND_ON)] _AlphaBlend("柔边混合 (Alpha Blend)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex DecalVert
            #pragma fragment DecalFrag
            #pragma shader_feature_local _ALPHABLEND_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Cutoff;
                float _AlphaBlend;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings DecalVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 DecalFrag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // 默认硬边裁剪：形状完全由贴图 Alpha 决定，与面片形状无关。
                // 柔边模式：不裁剪，直接 Alpha 混合。
                #ifndef _ALPHABLEND_ON
                    clip(color.a - _Cutoff);
                #endif

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
