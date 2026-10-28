Shader "FaceSlapper/Meteor/ShockArc"
{
    Properties
    {
        [MainColor] _BaseColor("Outer Flame", Color) = (1, 0.12, 0.01, 1)
        _HotColor("Hot Core (HDR)", Color) = (4, 1.4, 0.25, 1)
        _Heat("Heat", Range(0, 1)) = 1
        _NoiseScale("Noise Scale", Float) = 5
        _FlowSpeed("Flow Speed", Float) = 2.5
        _Distortion("Vertex Distortion", Float) = 0.035
        _Emission("Emission", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShockArc"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShockArcVert
            #pragma fragment ShockArcFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HotColor;
                float _Heat;
                float _NoiseScale;
                float _FlowSpeed;
                float _Distortion;
                float _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float Noise21(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            Varyings ShockArcVert(Attributes input)
            {
                Varyings output;
                float wave = sin(input.uv.x * 37.0 + _Time.y * 9.0)
                    * sin(input.uv.y * 11.0 - _Time.y * 5.0);
                input.positionOS.xyz += input.normalOS * wave * _Distortion * _Heat;

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 ShockArcFrag(Varyings input) : SV_Target
            {
                float heat = saturate(_Heat);
                float2 flowUv = float2(
                    input.uv.x * _NoiseScale,
                    input.uv.y * _NoiseScale - _Time.y * _FlowSpeed);
                float noise = Noise21(flowUv) * 0.65
                    + Noise21(flowUv * 2.13 + 17.7) * 0.35;

                float innerFade = smoothstep(0.0, 0.16, input.uv.y);
                float outerFade = 1.0 - smoothstep(0.68, 1.0, input.uv.y);
                float edge = innerFade * outerFade;
                float breakup = smoothstep(0.18, 0.82, noise + edge * 0.2);

                float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(
                    normalize(input.normalWS),
                    viewDirection)), 2.0);

                float alpha = edge * breakup * lerp(0.65, 1.0, fresnel)
                    * heat * _BaseColor.a;
                float core = saturate((1.0 - input.uv.y) + noise * 0.35);
                half3 color = lerp(_BaseColor.rgb, _HotColor.rgb, core);
                color *= _Emission * lerp(0.5, 1.0, heat);

                clip(alpha - 0.003);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
