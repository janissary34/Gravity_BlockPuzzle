Shader "GravityPuzzle/Shredder Dissolve Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _Color("Tint", Color) = (1,1,1,1)
        _ShredLine("Shred Line", Float) = 0
        _EdgeAmplitude("Edge Amplitude", Float) = .075
        _EdgeSoftness("Edge Softness", Float) = .035
        _NoiseFrequency("Noise Frequency", Float) = 12
        _ShredTint("Shred Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _ShredLine;
                float _EdgeAmplitude;
                float _EdgeSoftness;
                float _NoiseFrequency;
                float4 _ShredTint;
            CBUFFER_END

            float Hash(float value)
            {
                return frac(sin(value * 91.3458) * 47453.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color * _ShredTint;
                float tooth = Hash(floor(input.positionWS.x * _NoiseFrequency));
                float wave = sin(input.positionWS.x * _NoiseFrequency * 2.17) * .35;
                float cutterEdge = _ShredLine + ((tooth - .5) + wave) * _EdgeAmplitude;
                float remaining = smoothstep(cutterEdge - _EdgeSoftness, cutterEdge + _EdgeSoftness, input.positionWS.y);
                color.a *= remaining;
                clip(color.a - .003);
                return color;
            }
            ENDHLSL
        }
    }
}
