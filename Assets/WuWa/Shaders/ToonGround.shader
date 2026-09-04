Shader "WuWa/ToonGround"
{
    // Vertex-color painted terrain: biome tints are baked into mesh colors,
    // lit by a soft half-lambert with main-light shadows, cloud cookies and
    // fog. Procedural world-space detail (macro patches, micro grain, slope
    // strata, snow sparkle) breaks up the flat vertex paint up close.
    Properties
    {
        _BaseColor ("Base Tint", Color) = (1, 1, 1, 1)
        _ShadeTint ("Shade Tint", Color) = (0.50, 0.56, 0.70, 1)
        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.10
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _StrataStrength ("Slope Strata", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadeTint;
                half _AmbientBoost;
                half _DetailStrength;
                half _StrataStrength;
            CBUFFER_END

            float _WuWaNight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                half4 color : COLOR;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + 1.0), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half3 n = normalize(i.normalWS);

                // ---- procedural detail
                float dist = distance(i.positionWS, _WorldSpaceCameraPos.xyz);
                float distFade = saturate(1.0 - dist / 160.0);
                float2 p = i.positionWS.xz;
                float n1 = VNoise(p * 0.07 + 13.1);
                float n2 = VNoise(p * 0.55 + 3.7);
                float n3 = VNoise(p * 2.3 + 7.9);
                half detail = 1.0h + ((n1 - 0.5) * 0.18 + (n2 - 0.5) * 0.12 * distFade + (n3 - 0.5) * 0.08 * distFade)
                              * _DetailStrength * 2.0h;
                half3 albedo = i.color.rgb * _BaseColor.rgb * detail;

                half slope = saturate(1.0h - n.y);
                half strata = sin(i.positionWS.y * 2.6 + n1 * 4.0) * 0.5h + 0.5h;
                albedo *= 1.0h - smoothstep(0.22h, 0.45h, slope) * strata * 0.22h * _StrataStrength * 2.0h;

                half snow = smoothstep(0.42h, 0.6h, dot(i.color.rgb, half3(0.33h, 0.34h, 0.33h)));
                half sparkle = step(0.986, Hash21(floor(p * 9.0))) * snow * distFade;

                // ---- lighting
                half nd = dot(n, mainLight.direction) * 0.5h + 0.5h;     // half-lambert
                half lit = saturate(nd * nd) * mainLight.shadowAttenuation;
                half3 sun = saturate(mainLight.color);                    // clamp the sun (keeps paint values)
                half nightMul = 1.0h - saturate(_WuWaNight) * 0.7h;
                half3 shade = albedo * _ShadeTint.rgb * nightMul;
                half3 c = lerp(shade, albedo * sun, lit * 0.82h + 0.18h);
                c += SampleSH(n) * albedo * _AmbientBoost;
                c += sparkle * 0.45h * sun;
                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Simple Lit"
}
