Shader "GravityPuzzle/Bomb Defuse Split Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _Color("Tint", Color) = (1,1,1,1)
        _SplitSide("Split Side", Float) = 0
        _DefuseFade("Defuse Fade", Range(0,1)) = 0
        _SplitLineLocal("Split Line Local", Float) = 0
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
                float localX : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _SplitSide;
                float _DefuseFade;
                float _SplitLineLocal;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                output.color = input.color * _Color;
                output.localX = input.positionOS.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float isRightHalf = step(.5, _SplitSide);
                float splitDistance = lerp(
                    _SplitLineLocal - input.localX,
                    input.localX - _SplitLineLocal,
                    isRightHalf);
                clip(splitDistance);

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                color.a *= saturate(1 - _DefuseFade);
                clip(color.a - .003);
                return color;
            }
            ENDHLSL
        }
    }
}
