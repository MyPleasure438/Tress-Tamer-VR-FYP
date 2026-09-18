Shader "Custom/Hair/Stubble Overlay"
{
    Properties
    {
        _StubbleTex ("Stubble Texture", 2D) = "white" {}
        _StubbleColor ("Stubble Color", Color) = (0.11, 0.065, 0.035, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StubbleOverlay"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_StubbleTex);
            SAMPLER(sampler_StubbleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _StubbleTex_ST;
                float4 _StubbleColor;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _StubbleTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half stubbleAlpha = SAMPLE_TEXTURE2D(_StubbleTex, sampler_StubbleTex, input.uv).a;
                half finalAlpha = stubbleAlpha * saturate(_Opacity);
                return half4(_StubbleColor.rgb, finalAlpha);
            }
            ENDHLSL
        }
    }
}
