Shader "WuWa/AnimeWater"
{
    // Stylized flat-shaded water: two drifting noise bands over a deep color,
    // thin caustic lines, depth-based shoreline foam and soft edges, a sun
    // glint and grazing-angle sky reflection. Transparent, unlit, fogged.
    Properties
    {
        _DeepColor ("Deep", Color) = (0.10, 0.30, 0.42, 0.92)
        _ShallowColor ("Shallow", Color) = (0.30, 0.62, 0.68, 0.85)
        _FoamColor ("Sparkle / Foam", Color) = (0.85, 1.0, 1.0, 0.9)
        _HorizonColor ("Horizon Reflection", Color) = (0.72, 0.86, 1.0, 1)
        _WaveScale ("Wave Scale", Float) = 0.13
        _Speed ("Speed", Float) = 0.45
        _FoamWidth ("Shore Foam Width", Float) = 1.4
        _SpecPower ("Sun Glint Power", Float) = 160
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _DeepColor, _ShallowColor, _FoamColor, _HorizonColor;
            float _WaveScale, _Speed, _FoamWidth, _SpecPower;
            sampler2D _CameraDepthTexture;        // URP camera depth (global)
            float4 _MainLightPosition;            // URP main light direction (global)
            float _WuWaNight;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 ws : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 ws = mul(unity_ObjectToWorld, v.vertex).xyz;
                ws.y += sin(ws.x * 0.35 + _Time.y * 1.1) * 0.05
                      + sin(ws.z * 0.28 + _Time.y * 0.8) * 0.05;
                o.ws = ws;
                o.pos = UnityWorldToClipPos(ws);
                o.screenPos = ComputeScreenPos(o.pos);
                o.screenPos.z = -mul(UNITY_MATRIX_V, float4(ws, 1.0)).z;   // eye depth of the surface
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p1 = i.ws.xz * _WaveScale + float2(t, t * 0.7);
                float2 p2 = i.ws.xz * _WaveScale * 2.3 - float2(t * 0.8, t * 0.5);
                float n1 = noise(p1);
                float n2 = noise(p2);
                float band = smoothstep(0.35, 0.85, n1 * 0.65 + n2 * 0.35);

                half4 col = lerp(_DeepColor, _ShallowColor, band);

                // thin bright caustic lines where the two fields cross (fade far away)
                float camDist = distance(i.ws, _WorldSpaceCameraPos);
                float lineFade = saturate(1.0 - camDist / 260.0);
                float line1 = smoothstep(0.46, 0.5, n1) * smoothstep(0.54, 0.5, n1);
                float line2 = smoothstep(0.46, 0.5, n2) * smoothstep(0.54, 0.5, n2);
                col.rgb += _FoamColor.rgb * (line1 + line2) * 1.0 * _FoamColor.a * lineFade;

                // perturbed normal from the noise gradient for glints + fresnel
                float2 e = float2(0.35, 0.0);
                float nx = noise(p1 + e.xy) - noise(p1 - e.xy);
                float nz = noise(p1 + e.yx) - noise(p1 - e.yx);
                float3 n = normalize(float3(-nx * 1.4, 1.0, -nz * 1.4));
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.ws);
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float dayMul = 1.0 - _WuWaNight * 0.75;
                float spec = pow(saturate(dot(reflect(-viewDir, n), sunDir)), _SpecPower) * saturate(sunDir.y * 4.0);
                col.rgb += spec * 1.5 * dayMul;
                float fres = pow(1.0 - saturate(dot(viewDir, n)), 3.5);
                col.rgb = lerp(col.rgb, _HorizonColor.rgb * dayMul, fres * 0.55);
                col.a = max(col.a, fres * 0.55);

                // depth-based shoreline: soft intersection + noisy foam band
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                float diff = sceneZ - i.screenPos.z;
                float edge = saturate(diff / 0.55);
                float foamMask = 1.0 - saturate(diff / _FoamWidth);
                float foamN = noise(i.ws.xz * 1.9 + float2(t * 0.9, -t * 0.6));
                float foam = smoothstep(0.3, 0.75, foamMask * (0.55 + 0.7 * foamN));
                col.rgb = lerp(col.rgb, _FoamColor.rgb * dayMul, foam * 0.9);
                col.a = lerp(col.a, 1.0, foam * 0.6) * edge;

                col.rgb *= 1.0 - _WuWaNight * 0.6;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
