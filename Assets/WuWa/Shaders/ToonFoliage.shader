Shader "WuWa/ToonFoliage"
{
    // Alpha-cutout leaf/grass cards with two-sided lighting, wrapped toon
    // diffuse, back-light translucency, vertex wind and leaf-shaped shadows.
    Properties
    {
        _BaseMap ("Leaf Atlas", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.4
        _ShadeTint ("Shade Tint", Color) = (0.50, 0.58, 0.80, 1)
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
        _Translucency ("Translucency", Range(0, 1)) = 0.35
        _AmbientBoost ("Ambient", Range(0, 1)) = 0.35
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.15
        _WindSpeed ("Wind Speed", Float) = 1.3
        _UvHeightMask ("Wind by UV height", Range(0, 1)) = 0
        _BottomDarken ("Bottom Darken (UV)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half4 _ShadeTint;
            half _Wrap;
            half _Translucency;
            half _AmbientBoost;
            half _WindStrength;
            half _WindSpeed;
            half _UvHeightMask;
            half _BottomDarken;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // set by DayNightCycle (0 = day). Unset globals read as 0, so day is the default.
        float _WuWaNight;

        float3 WuWaWind(float3 positionWS, float2 uv)
        {
            float t = _Time.y * _WindSpeed;
            float phase = positionWS.x * 0.35 + positionWS.z * 0.25;
            float sway = sin(t + phase) * 0.6 + sin(t * 1.7 + phase * 2.3) * 0.25 + sin(t * 0.37 + positionWS.z * 0.1) * 0.35;
            float gust = 0.6 + 0.4 * sin(t * 0.21 + positionWS.x * 0.02);
            float w = lerp(1.0, saturate(uv.y), _UvHeightMask);
            float amp = _WindStrength * 0.35 * w * gust;
            return float3(sway * amp, -abs(sway) * amp * 0.25, sway * amp * 0.6);
        }
        ENDHLSL

        // ------------------------------------------------------------ forward
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += WuWaWind(positionWS, v.uv);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);

                half3 n = normalize(i.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half3 sun = saturate(L.color);

                half nd = dot(n, L.direction);
                half lit = saturate((nd + _Wrap) / (1.0h + _Wrap));
                lit = smoothstep(0.08h, 0.78h, lit) * L.shadowAttenuation;

                half3 albedo = tex.rgb * lerp(1.0h - _BottomDarken, 1.0h, saturate(i.uv.y));
                half nightMul = 1.0h - saturate(_WuWaNight) * 0.72h;
                half3 shade = albedo * _ShadeTint.rgb * nightMul;
                half3 c = lerp(shade, albedo * sun, lit);
                c += SampleSH(n) * albedo * _AmbientBoost;

                // light coming through the leaf toward the viewer
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half back = pow(saturate(dot(-v, L.direction)), 3.0h);
                c += albedo * sun * back * _Translucency * L.shadowAttenuation;

                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V ShadowVert(A v)
            {
                V o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += WuWaWind(positionWS, v.uv);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ depth
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V DepthVert(A v)
            {
                V o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += WuWaWind(positionWS, v.uv);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 DepthFrag(V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ depth normals (SSAO)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            V DNVert(A v)
            {
                V o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += WuWaWind(positionWS, v.uv);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 DNFrag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                float3 n = normalize(i.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                float2 remapped = saturate(oct * 0.5 + 0.5);
                half3 packed = PackFloat2To888(remapped);
                return half4(packed, 0.0);
            #else
                return half4(n, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Simple Lit"
}
