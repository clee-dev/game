Shader "Custom/ToonTransparent"
{
    Properties
    {
        [Header(Base)]
        _BaseColor        ("Base Color + Alpha",   Color)          = (1, 1, 1, 0.3)
        _BaseMap          ("Texture (RGBA)",       2D)             = "white" {}

        [Header(Toon Shading)]
        _ShadowColor      ("Shadow Color",         Color)          = (0.4, 0.45, 0.65, 1)
        _ShadowThreshold  ("Shadow Threshold",     Range(0, 1))    = 0.4
        _ShadowSmooth     ("Shadow Softness",      Range(0, 0.2))  = 0.02

        [Header(Rim Light)]
        _RimColor         ("Rim Color",            Color)          = (1, 1, 1, 1)
        _RimThreshold     ("Rim Threshold",        Range(0, 1))    = 0.75
        _RimStrength      ("Rim Strength",         Range(0, 1))    = 0.2

        [Header(Glass Fresnel)]
        _FresnelStrength  ("Fresnel Strength",     Range(0, 1))    = 0.5
        _FresnelPower     ("Fresnel Power",        Range(1, 8))    = 3.0
    }

    SubShader
    {
        // Transparent queue -- renders after all opaques
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // No outline pass -- inverse hull looks wrong on transparent surfaces.
        // ScreenSpaceOutlines RendererFeature still draws silhouette outlines.

        Pass
        {
            Name "ToonTransparent"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;      float4 _BaseMap_ST;
                float4 _ShadowColor;    float  _ShadowThreshold;  float _ShadowSmooth;
                float4 _RimColor;       float  _RimThreshold;     float _RimStrength;
                float  _FresnelStrength; float _FresnelPower;
            CBUFFER_END

            struct Attributes
            {
                float4 pos : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1; float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings ToonVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.pos.xyz);
                OUT.pos         = posInputs.positionCS;
                OUT.worldPos    = posInputs.positionWS;
                OUT.normal      = TransformObjectToWorldNormal(IN.normal);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            half4 ToonFrag(Varyings IN) : SV_Target
            {
                half4 texColor   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseColor  = texColor * _BaseColor;

                Light mainLight  = GetMainLight(IN.shadowCoord);
                half3 lightDir   = normalize(mainLight.direction);
                half3 normal     = normalize(IN.normal);
                half3 viewDir    = normalize(GetWorldSpaceViewDir(IN.worldPos));

                half NdotL       = dot(normal, lightDir);
                half toonDiff    = smoothstep(_ShadowThreshold - _ShadowSmooth,
                                              _ShadowThreshold + _ShadowSmooth,
                                              NdotL * mainLight.shadowAttenuation);
                half rim         = smoothstep(_RimThreshold, 1.0h, 1.0h - saturate(dot(normal, viewDir)));

                half3 color      = lerp(baseColor.rgb * _ShadowColor.rgb,
                                        baseColor.rgb * mainLight.color, toonDiff);
                color           += _RimColor.rgb * rim * _RimStrength * toonDiff;

                // Fresnel: edges facing away from camera become more opaque (mimics real glass)
                half NdotV       = saturate(dot(normal, viewDir));
                half fresnel     = pow(1.0h - NdotV, _FresnelPower) * _FresnelStrength;
                half alpha       = saturate(baseColor.a + fresnel);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}