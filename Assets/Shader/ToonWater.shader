Shader "Custom/ToonWater"
{
    Properties
    {
        [Header(Water Color)]
        _ShallowColor     ("Shallow Color + Alpha",  Color)         = (0.4, 0.8, 0.9, 0.7)
        _DeepColor        ("Deep Color + Alpha",     Color)         = (0.1, 0.4, 0.6, 1.0)
        _DepthColorRange  ("Depth Color Range",      Float)         = 3.0

        [Header(Foam)]
        _FoamColor        ("Foam Color",             Color)         = (0.95, 0.98, 1.0, 1.0)
        _FoamDistance     ("Foam Distance (units)",  Float)         = 0.5

        [Header(Waves)]
        _WaveAmplitude    ("Wave Amplitude",         Float)         = 0.08
        _WaveSpeed        ("Wave Speed",             Float)         = 0.8
        _WaveFrequency    ("Wave Frequency",         Float)         = 0.4

        [Header(Surface Detail)]
        _NormalMap        ("Normal Map",             2D)            = "bump" {}
        _NormalScale      ("Normal Scale",           Range(0, 2))   = 0.6
        _NormalScrollSpeed("Normal Scroll Speed",    Float)         = 0.08

        [Header(Toon Shading)]
        _ShadowColor      ("Shadow Color",           Color)         = (0.3, 0.5, 0.7, 1)
        _ShadowThreshold  ("Shadow Threshold",       Range(0, 1))   = 0.4
        _ShadowSmooth     ("Shadow Softness",        Range(0, 0.2)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonWater"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex WaterVert
            #pragma fragment WaterFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap);           SAMPLER(sampler_NormalMap);
            TEXTURE2D(_CameraDepthTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;     float4 _DeepColor;        float  _DepthColorRange;
                float4 _FoamColor;        float  _FoamDistance;
                float  _WaveAmplitude;    float  _WaveSpeed;        float  _WaveFrequency;
                float4 _NormalMap_ST;     float  _NormalScale;      float  _NormalScrollSpeed;
                float4 _ShadowColor;      float  _ShadowThreshold;  float  _ShadowSmooth;
            CBUFFER_END

            struct Attributes
            {
                float4 pos     : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos         : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float4 screenPos   : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
            };

            Varyings WaterVert(Attributes IN)
            {
                Varyings OUT;

                // World position for wave calculation
                float3 worldPos = TransformObjectToWorld(IN.pos.xyz);

                // Two overlapping sin waves using world XZ so tiled meshes match
                float wave = sin(_Time.y * _WaveSpeed       + worldPos.x * _WaveFrequency
                                                             + worldPos.z * _WaveFrequency)
                           + sin(_Time.y * _WaveSpeed * 0.7 + worldPos.x * _WaveFrequency * 1.4
                                                             + worldPos.z * _WaveFrequency * 0.8);
                worldPos.y += wave * 0.5 * _WaveAmplitude;

                // Back to clip space from modified world position
                OUT.pos = TransformWorldToHClip(worldPos);
                OUT.worldPos = worldPos;

                // Tangent space for normal map
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normal, IN.tangent);
                OUT.normalWS    = normalInputs.normalWS;
                OUT.tangentWS   = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;

                OUT.uv          = TRANSFORM_TEX(IN.uv, _NormalMap);
                OUT.screenPos   = ComputeScreenPos(OUT.pos);
                OUT.shadowCoord = GetShadowCoord(GetVertexPositionInputs(IN.pos.xyz));
                return OUT;
            }

            half4 WaterFrag(Varyings IN) : SV_Target
            {
                // ---- Depth sampling ----
                float2 screenUV   = IN.screenPos.xy / IN.screenPos.w;
                float sceneDepth  = LinearEyeDepth(
                    SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_LinearClamp, screenUV).r,
                    _ZBufferParams);
                float surfaceDepth = IN.screenPos.w; // view-space depth of this fragment
                float depthDiff    = sceneDepth - surfaceDepth;
                depthDiff          = max(depthDiff, 0.0);

                // ---- Depth-based color and alpha ----
                float  depthT  = saturate(depthDiff / max(_DepthColorRange, 0.001));
                half4  water   = lerp(_ShallowColor, _DeepColor, depthT);

                // ---- Foam at edges ----
                float  foam    = 1.0 - saturate(depthDiff / max(_FoamDistance, 0.001));
                foam            = smoothstep(0.0, 0.5, foam); // soften foam edge
                water.rgb       = lerp(water.rgb, _FoamColor.rgb, foam * _FoamColor.a);

                // ---- Scrolling normal map (two layers) ----
                float speed     = _Time.y * _NormalScrollSpeed;
                float2 uv1      = IN.uv + float2( speed,        speed * 0.6);
                float2 uv2      = IN.uv + float2(-speed * 0.7,  speed * 0.9);
                half3  n1       = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1));
                half3  n2       = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2));
                half3  normalTS = normalize(n1 + n2);
                normalTS.xy    *= _NormalScale;

                // Transform normal map from tangent space to world space
                half3  normalWS = TransformTangentToWorld(
                    normalTS,
                    half3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS));
                normalWS        = normalize(normalWS);

                // ---- Cel shading ----
                Light  mainLight = GetMainLight(IN.shadowCoord);
                half3  lightDir  = normalize(mainLight.direction);
                half   NdotL     = dot(normalWS, lightDir);
                half   toonDiff  = smoothstep(_ShadowThreshold - _ShadowSmooth,
                                              _ShadowThreshold + _ShadowSmooth,
                                              NdotL * mainLight.shadowAttenuation);

                half3  litColor    = water.rgb * mainLight.color;
                half3  shadowColor = water.rgb * _ShadowColor.rgb;
                half3  color       = lerp(shadowColor, litColor, toonDiff);

                return half4(color, water.a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}