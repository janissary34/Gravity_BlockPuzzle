Shader "GravityPuzzle/Box Shutter"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Opening("Opening", Range(0,1)) = 0
        _ContentUvRect("Content UV Rect", Vector) = (0.02,0.02,0.96,0.96)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False" }
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

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Opening;
                float4 _ContentUvRect;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // The rectangular mesh is independent of the U-shaped Tunnel.
                // Sample only the shutter's slatted interior: its rounded asset
                // perimeter must never create a second visible frame.
                float panelY = input.uv.y - _Opening;
                clip(panelY - 0.000001);
                float2 sourceUv = _ContentUvRect.xy + float2(input.uv.x, panelY) * _ContentUvRect.zw;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sourceUv);
                return color * input.color;
            }
            ENDHLSL
        }
    }
}
