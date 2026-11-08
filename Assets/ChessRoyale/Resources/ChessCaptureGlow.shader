Shader "ChessRoyale/CaptureGlow"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Particle ("Soft Spark", Float) = 0
        _Smoke ("Smoke Texture", Float) = 0
        _BaseMap ("Particle Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Particle;
                half _Smoke;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = _BaseColor;
                if (_Smoke > 0.5h)
                {
                    color *= input.color * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                }
                else if (_Particle > 0.5h)
                {
                    color *= input.color;
                    half radius = length(input.uv * 2.0h - 1.0h);
                    color.a *= saturate(1.0h - radius) * saturate(1.0h - radius);
                    color.rgb *= 2.0h;
                }
                return color;
            }
            ENDHLSL
        }
    }
}
