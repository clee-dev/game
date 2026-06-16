// ToonLit.shader
// URP cel shader with built-in outline. Apply to any material.
//
// How to use:
//   1. Right-click in Project > Create > Shader > Unlit Shader
//   2. Rename it ToonLit, open it, replace ALL contents with this file
//   3. Create a Material using this shader
//   4. Assign to your meshes
//
// The outline uses the "inverse hull" technique -- renders back faces
// pushed outward in the normal direction. Works great on characters
// and props. For outlines between separate objects, see a screen-space
// outline RendererFeature (a separate step).

Shader "Custom/ToonLit"
{
    Properties
    {
        [Header(Base)]
        _BaseColor      ("Base Color",  Color)   = (1, 1, 1, 1)
        _BaseMap        ("Texture",     2D)      = "white" {}

        [Header(Toon Shading)]
        _ShadowColor    ("Shadow Color",    Color)          = (0.4, 0.45, 0.65, 1)
        _ShadowThreshold("Shadow Threshold",Range(0, 1))    = 0.4
        _ShadowSmooth   ("Shadow Softness", Range(0, 0.2))  = 0.02

        [Header(Rim Light)]
        _RimColor       ("Rim Color",       Color)          = (1, 1, 1, 1)
        _RimThreshold   ("Rim Threshold",   Range(0, 1))    = 0.75
        _RimStrength    ("Rim Strength",    Range(0, 1))    = 0.35

        [Header(Outline)]
        _OutlineColor   ("Outline Color",   Color)          = (0.05, 0.05, 0.05, 1)
        _OutlineWidth   ("Outline Width",   Range(0, 0.1))  = 0.025
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ----------------------------------------------------------------
        // PASS 1 -- Inverse hull outline
        // Renders back faces pushed outward along normals in a flat dark color.
        // ----------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Cull Front

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // All material properties must be declared in CBUFFER for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowColor;
                float  _ShadowThreshold;
                float  _ShadowSmooth;
                float4 _RimColor;
                float  _RimThreshold;
                float  _RimStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; };
            struct Varyings   { float4 pos : SV_POSITION; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                // Push vertex outward along its world-space normal
                float3 worldPos    = TransformObjectToWorld(IN.pos.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normal);
                worldPos          += worldNormal * _OutlineWidth;
                OUT.pos            = TransformWorldToHClip(worldPos);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // PASS 2 -- Toon lit
        // Cel-shaded lighting with hard shadow cutoff and rim light.
        // ----------------------------------------------------------------
        Pass
        {
            Name "ToonLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowColor;
                float  _ShadowThreshold;
                float  _ShadowSmooth;
                float4 _RimColor;
                float  _RimThreshold;
                float  _RimStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 pos    : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos       : SV_POSITION;
                float3 worldPos  : TEXCOORD0;
                float3 normal    : TEXCOORD1;
                float2 uv        : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings ToonVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.pos.xyz);
                OUT.pos        = posInputs.positionCS;
                OUT.worldPos   = posInputs.positionWS;
                OUT.normal     = TransformObjectToWorldNormal(IN.normal);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            half4 ToonFrag(Varyings IN) : SV_Target
            {
                half4 texColor  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseColor = texColor * _BaseColor;

                // Main directional light + shadows
                Light  mainLight = GetMainLight(IN.shadowCoord);
                half3  lightDir  = normalize(mainLight.direction);
                half3  normal    = normalize(IN.normal);
                half3  viewDir   = normalize(GetWorldSpaceViewDir(IN.worldPos));

                // Cel shading -- smoothstep gives a slightly soft edge on the shadow line
                half NdotL   = dot(normal, lightDir);
                half toonDiff = smoothstep(
                    _ShadowThreshold - _ShadowSmooth,
                    _ShadowThreshold + _ShadowSmooth,
                    NdotL * mainLight.shadowAttenuation
                );

                // Rim light -- brightens edges facing away from the camera
                half rim = smoothstep(_RimThreshold, 1.0h, 1.0h - saturate(dot(normal, viewDir)));

                // Combine lit and shadow colors
                half3 litColor    = baseColor.rgb * mainLight.color;
                half3 shadowColor = baseColor.rgb * _ShadowColor.rgb;
                half3 color       = lerp(shadowColor, litColor, toonDiff);

                // Add rim
                color += _RimColor.rgb * rim * _RimStrength * toonDiff;

                return half4(color, baseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
