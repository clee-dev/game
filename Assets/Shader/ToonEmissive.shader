Shader "Custom/ToonEmissive"
{
    Properties
    {
        [Header(Base)]
        _BaseColor        ("Base Color",           Color)          = (1, 1, 1, 1)
        _BaseMap          ("Texture",              2D)             = "white" {}

        [Header(Toon Shading)]
        _ShadowColor      ("Shadow Color",         Color)          = (0.4, 0.45, 0.65, 1)
        _ShadowThreshold  ("Shadow Threshold",     Range(0, 1))    = 0.4
        _ShadowSmooth     ("Shadow Softness",      Range(0, 0.2))  = 0.02

        [Header(Rim Light)]
        _RimColor         ("Rim Color",            Color)          = (1, 1, 1, 1)
        _RimThreshold     ("Rim Threshold",        Range(0, 1))    = 0.75
        _RimStrength      ("Rim Strength",         Range(0, 1))    = 0.35

        [Header(Outline)]
        _OutlineColor     ("Outline Color",        Color)          = (0.05, 0.05, 0.05, 1)
        _OutlineWidth     ("Outline Width",        Range(0, 0.1))  = 0.025

        [Header(Emission)]
        [HDR] _EmissionColor ("Emission Color",   Color)          = (0, 0, 0, 0)
        _EmissionMap      ("Emission Map",         2D)             = "white" {}
        _EmissionStrength ("Emission Strength",    Float)          = 1.0
        _EmissionPulseSpeed ("Pulse Speed (0=off)",Float)          = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ----------------------------------------------------------------
        // PASS 1 -- Inverse hull outline (identical to ToonLit)
        // ----------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Cull Front

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;        float4 _BaseMap_ST;
                float4 _ShadowColor;      float  _ShadowThreshold;    float _ShadowSmooth;
                float4 _RimColor;         float  _RimThreshold;       float _RimStrength;
                float4 _OutlineColor;     float  _OutlineWidth;
                float4 _EmissionColor;    float4 _EmissionMap_ST;
                float  _EmissionStrength; float  _EmissionPulseSpeed;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; };
            struct Varyings   { float4 pos : SV_POSITION; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos    = TransformObjectToWorld(IN.pos.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normal);
                worldPos          += worldNormal * _OutlineWidth;
                OUT.pos            = TransformWorldToHClip(worldPos);
                return OUT;
            }
            half4 OutlineFrag(Varyings IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // PASS 2 -- Toon lit + emission
        // ----------------------------------------------------------------
        Pass
        {
            Name "ToonEmissive"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;        float4 _BaseMap_ST;
                float4 _ShadowColor;      float  _ShadowThreshold;    float _ShadowSmooth;
                float4 _RimColor;         float  _RimThreshold;       float _RimStrength;
                float4 _OutlineColor;     float  _OutlineWidth;
                float4 _EmissionColor;    float4 _EmissionMap_ST;
                float  _EmissionStrength; float  _EmissionPulseSpeed;
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
                half4 baseColor  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

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

                // Emission -- added after lighting, unaffected by shadows or NdotL
                half4 emitTex    = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv);
                float pulse      = _EmissionPulseSpeed > 0.001
                                   ? (sin(_Time.y * _EmissionPulseSpeed) * 0.5 + 0.5)
                                   : 1.0;
                color           += emitTex.rgb * _EmissionColor.rgb * _EmissionStrength * pulse;

                return half4(color, baseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}