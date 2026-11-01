Shader "GravityPuzzle/Bomb Defuse Split Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _Color("Tint", Color) = (1,1,1,1)
        _DefuseFade("Defuse Fade", Range(0,1)) = 0
        _ClipMinLocal("Clip Min Local", Vector) = (-100,-100,0,0)
        _ClipMaxLocal("Clip Max Local", Vector) = (100,100,0,0)
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
                float2 localPosition : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _DefuseFade;
                float4 _ClipMinLocal;
                float4 _ClipMaxLocal;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                output.color = input.color * _Color;
                output.localPosition = input.positionOS.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.localPosition.x - _ClipMinLocal.x);
                clip(_ClipMaxLocal.x - input.localPosition.x);
                clip(input.localPosition.y - _ClipMinLocal.y);
                clip(_ClipMaxLocal.y - input.localPosition.y);

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                color.a *= saturate(1 - _DefuseFade);
                clip(color.a - .003);
                return color;
            }
            ENDHLSL
        }
    }
}
