Shader "GravityPuzzle/Fall Trail"
{
    // Motion streak behind moving pieces. Colour and opacity come from mesh
    // vertex colours written by FallTrailPresentation. Output is premultiplied,
    // so the blend mode only switches the destination factor:
    //   Alpha Blend: covers the background like a translucent paint stroke.
    //   Additive:    adds light on top of the background, reads as a glow.
    Properties
    {
        [Enum(Alpha Blend, 10, Additive, 1)] _DstBlend("Blend Mode", Float) = 1
        _Intensity("Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One [_DstBlend]
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
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half _DstBlend;
                half _Intensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = input.color;
                col.rgb *= col.a * _Intensity;
                return col;
            }
            ENDHLSL
        }
    }
}
