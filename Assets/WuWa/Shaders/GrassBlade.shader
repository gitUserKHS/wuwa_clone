Shader "WuWa/GrassBlade"
{
    // Procedural instanced grass blades (see GrassField.cs). Per-blade data
    // comes from a structured buffer indexed by SV_InstanceID.
    Properties
    {
        _TipColor ("Tip Tint", Color) = (1.12, 1.16, 0.88, 1)
        _ShadeTint ("Shade Tint", Color) = (0.50, 0.58, 0.80, 1)
        _WindStrength ("Wind", Range(0, 1)) = 0.32
        _AmbientBoost ("Ambient", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        struct Blade
        {
            float3 pos;
            float3 col;
            float yaw;
            float h;
            float w;
            float seed;
        };
        StructuredBuffer<Blade> _Blades;
        float4 _PlayerPos;
        float4 _Fade;
        float _WuWaNight;

        CBUFFER_START(UnityPerMaterial)
            half4 _TipColor;
            half4 _ShadeTint;
            half _WindStrength;
            half _AmbientBoost;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            uint instanceID : SV_InstanceID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 fwdWS : TEXCOORD1;
            float3 color : TEXCOORD2;
            float2 uv : TEXCOORD3;
            float fogFactor : TEXCOORD4;
        };

        Varyings BladeVert(Attributes v)
        {
            Varyings o;
            Blade b = _Blades[v.instanceID];
            float s, c;
            sincos(b.yaw, s, c);
            float3 right = float3(c, 0, -s);
            float3 fwd = float3(s, 0, c);
            float y = v.positionOS.y;

            float dist = distance(b.pos, _WorldSpaceCameraPos.xyz);
            float fade = saturate((_Fade.y - dist) / max(0.01, _Fade.y - _Fade.x));
            float h = b.h * fade;

            float t = _Time.y;
            float sway = sin(t * 1.6 + b.pos.x * 0.35 + b.pos.z * 0.2 + b.seed * 6.28) * 0.5
                       + sin(t * 2.7 + b.pos.z * 0.5 + b.seed * 3.0) * 0.25;
            float gust = 0.6 + 0.4 * sin(t * 0.3 + (b.pos.x + b.pos.z) * 0.03);
            float bend = (0.16 + sway * _WindStrength * gust) * y * y;

            float3 toP = b.pos - _PlayerPos.xyz;
            toP.y = 0;
            float pd = length(toP);
            float push = saturate(1.0 - pd / 1.4) * y;
            float3 pushDir = pd > 0.001 ? toP / pd : fwd;

            float3 pos = b.pos + right * (v.positionOS.x * b.w * (1.0 + push * 0.3))
                       + float3(0, y * h, 0) + fwd * bend * h + pushDir * push * 0.75 * h;
            pos.y -= (bend * bend + push * push) * h * 0.35;

            o.positionWS = pos;
            o.positionCS = TransformWorldToHClip(pos);
            o.fwdWS = fwd;
            o.color = b.col;
            o.uv = v.uv;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BladeVert
            #pragma fragment BladeFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 BladeFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 fwd = IS_FRONT_VFACE(face, i.fwdWS, -i.fwdWS);
                half3 n = normalize(half3(fwd.x * 0.35h, 1.0h, fwd.z * 0.35h));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half3 sun = saturate(L.color);

                half v = saturate(i.uv.y);
                half3 albedo = lerp(i.color * 0.58h, i.color * _TipColor.rgb, v * v);

                half nd = dot(n, L.direction) * 0.5h + 0.5h;
                half lit = nd * nd * L.shadowAttenuation;
                half nightMul = 1.0h - saturate(_WuWaNight) * 0.72h;
                half3 shade = albedo * _ShadeTint.rgb * nightMul;
                half3 c = lerp(shade, albedo * sun, lit * 0.85h + 0.15h);
                c += SampleSH(n) * albedo * _AmbientBoost;
                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BladeVert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BladeVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DNFrag(Varyings i) : SV_Target
            {
                float3 n = float3(0, 1, 0);
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
}
