Shader "WuWa/CharToon"
{
    // Character cel shader (WuWa-leaning): albedo × stepped light ramp with a tinted shadow,
    // soft rim, optional hair band highlight, and an inverted-hull outline pass.
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ShadeTint ("Shade Tint", Color) = (0.62, 0.58, 0.72, 1)
        _MidTint ("Mid Tint", Color) = (0.88, 0.86, 0.92, 1)
        _StepMid ("Mid Step", Range(0, 1)) = 0.28
        _StepLit ("Lit Step", Range(0, 1)) = 0.52
        _StepSoft ("Step Softness", Range(0.001, 0.2)) = 0.02
        _Wrap ("Light Wrap", Range(0, 1)) = 0.25
        _AmbientBoost ("Ambient", Range(0, 1)) = 0.25
        _RimColor ("Rim Color", Color) = (0.7, 0.9, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.35
        _HairBand ("Hair Highlight Strength", Range(0, 1)) = 0
        _HairBandCenter ("Hair Highlight Center (object Y)", Float) = 1.55
        _HairBandWidth ("Hair Highlight Width", Float) = 0.06
        _OutlineWidth ("Outline Width (m)", Range(0, 0.01)) = 0.0025
        _OutlineColor ("Outline Color", Color) = (0.05, 0.03, 0.06, 1)
        _BlinkMap ("Lid Albedo (eyes painted over)", 2D) = "white" {}
        _Blink ("Blink (0 open, 1 closed)", Range(0, 1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeTint;
            half4 _MidTint;
            half _StepMid;
            half _StepLit;
            half _StepSoft;
            half _Wrap;
            half _AmbientBoost;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half _HairBand;
            half _HairBandCenter;
            half _HairBandWidth;
            half _OutlineWidth;
            half4 _OutlineColor;
            half _Cutoff;
            half _AlphaClip;
            half _Blink;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BlinkMap);
        SAMPLER(sampler_BlinkMap);
        float _WuWaNight;
        ENDHLSL

        // ------------------------------------------------------------ forward
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

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
                float2 eye : TEXCOORD1;      // eye-local coords baked in UV2: (x, y) in iris radii, (0, 10) away from the eyes
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float objY : TEXCOORD4;
                float2 eye : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.objY = v.positionOS.y;
                o.eye = v.eye;
                return o;
            }

            // eyelid sweep: above the moving upper edge (and below the rising lower edge) show the lid albedo,
            // draw a lash line on the edge and a soft shadow just under it
            half3 ApplyEyelid(half3 albedo, float2 uv, float2 e)
            {
                float de = length(e);
                float t = _Blink;
                if (de > 1.5 || t < 0.002) return albedo;
                float x2 = e.x * e.x;
                float closedLine = -0.15 - 0.20 * x2;
                float edge = lerp(0.95, closedLine, t);                       // upper lid starts at the resting lid line, not at the disc rim
                float low = lerp(-1.1, closedLine - 0.02, pow(t, 2.5));      // lower lid rises late and less
                float rimFade = smoothstep(1.5, 1.25, de);
                float covered = saturate(smoothstep(edge - 0.05, edge + 0.05, e.y) + smoothstep(low + 0.05, low - 0.05, e.y)) * rimFade;
                half3 lid = SAMPLE_TEXTURE2D(_BlinkMap, sampler_BlinkMap, uv).rgb * _BaseColor.rgb;
                half3 c = lerp(albedo, lid, covered);
                float lashW = 0.12 * (1.0 - 0.45 * x2);
                float lash = (1.0 - smoothstep(lashW * 0.45, lashW, abs(e.y - edge))) * step(abs(e.x), 1.15) * smoothstep(0.02, 0.12, t) * rimFade;
                c = lerp(c, half3(0.15, 0.09, 0.08), lash * 0.9);
                float shadow = (1.0 - smoothstep(0.0, 0.45, edge - e.y)) * step(e.y, edge) * rimFade * t;
                c *= 1.0 - 0.16 * shadow;
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                tex.rgb = ApplyEyelid(tex.rgb, i.uv, i.eye);
                if (_AlphaClip > 0.5h) clip(tex.a - _Cutoff);
                half3 n = normalize(i.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half3 sun = saturate(L.color);

                half nd = dot(n, L.direction);
                half lit = saturate((nd + _Wrap) / (1.0h + _Wrap)) * L.shadowAttenuation;
                half tMid = smoothstep(_StepMid - _StepSoft, _StepMid + _StepSoft, lit);
                half tLit = smoothstep(_StepLit - _StepSoft, _StepLit + _StepSoft, lit);

                half nightMul = 1.0h - saturate(_WuWaNight) * 0.65h;
                half3 albedo = tex.rgb;
                half3 shade = albedo * _ShadeTint.rgb * nightMul;
                half3 mid = albedo * _MidTint.rgb * nightMul;
                half3 bright = albedo * max(sun, 0.35h);
                half3 c = lerp(lerp(shade, mid, tMid), bright, tLit);
                c += SampleSH(n) * albedo * _AmbientBoost;

                // rim (view-facing edges, stronger on the lit side)
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                c += _RimColor.rgb * rim * _RimStrength * (0.4h + 0.6h * tLit);

                // hair band highlight (object-space height band, cheap anime specular)
                half band = 1.0h - saturate(abs(i.objY - _HairBandCenter) / max(_HairBandWidth, 1e-3h));
                c += band * band * _HairBand * 0.6h;

                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ outline (inverted hull)
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog

            // vertex colour R = per-vertex outline width mask (thin on the face, medium on hair); white when the mesh has no colours
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float fogFactor : TEXCOORD0; float2 uv : TEXCOORD1; };

            V OutlineVert(A v)
            {
                V o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                // keep the line width roughly constant on screen: scale by distance
                float dist = distance(positionWS, GetCameraPositionWS());
                float w = (_OutlineWidth * saturate(dist / 6.0) + _OutlineWidth * 0.3) * v.color.r;
                positionWS += normalWS * w;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 OutlineFrag(V i) : SV_Target
            {
                if (_AlphaClip > 0.5h) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                half3 c = MixFog(_OutlineColor.rgb, i.fogFactor);
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
            Cull Back

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
                if (_AlphaClip > 0.5h) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V DepthVert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 DepthFrag(V i) : SV_Target
            {
                if (_AlphaClip > 0.5h) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ depth normals
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V DNVert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 DNFrag(V i) : SV_Target
            {
                return half4(PackNormalOctRectEncode(TransformWorldToViewDir(normalize(i.normalWS), true)), 0, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
