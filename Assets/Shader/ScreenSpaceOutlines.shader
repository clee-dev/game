Shader "Custom/ScreenSpaceOutlines"
{
    Properties
    {
        [Header(Outline)]
        _OutlineColor     ("Outline Color",          Color)               = (0.05, 0.05, 0.05, 1)
        _OutlineThickness ("Outline Thickness (px)", Range(0.5, 5))       = 1.5
        _DepthThreshold   ("Depth Threshold",        Range(0.01, 1.0))    = 0.2
        [Header(Distance Fade)]
        _FadeStart        ("Fade Start (units)",     Float)               = 15
        _FadeEnd          ("Fade End (units)",       Float)               = 40
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ScreenSpaceOutlines"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D(_CameraDepthTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineThickness;
                float  _DepthThreshold;
                float  _FadeStart;
                float  _FadeEnd;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.texcoord   = uv;
                output.positionCS = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0, 1);
                return output;
            }

            float SampleLinearDepth(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_LinearClamp, uv).r;
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            float DepthEdge(float2 uv, float2 offset)
            {
                float center = SampleLinearDepth(uv);
                float d0 = SampleLinearDepth(uv + float2(-offset.x, -offset.y));
                float d1 = SampleLinearDepth(uv + float2( offset.x, -offset.y));
                float d2 = SampleLinearDepth(uv + float2(-offset.x,  offset.y));
                float d3 = SampleLinearDepth(uv + float2( offset.x,  offset.y));
                float gx = d3 - d0;
                float gy = d2 - d1;
                float gradient = sqrt(gx * gx + gy * gy);
                return gradient / max(center, 0.001);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 scene   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float2 offset = (1.0 / _ScreenParams.xy) * _OutlineThickness;
                float edge    = DepthEdge(input.texcoord, offset);
                float outline = step(_DepthThreshold, edge);

                float depth     = SampleLinearDepth(input.texcoord);
                float fadeRange = max(_FadeEnd - _FadeStart, 0.001);
                float fade      = 1.0 - saturate((depth - _FadeStart) / fadeRange);
                outline        *= fade;

                return lerp(scene, half4(_OutlineColor.rgb, 1.0), outline * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
