Shader "WuWa/AnimeSky"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.28, 0.52, 0.95, 1)
        _MidColor ("Horizon Color", Color) = (0.78, 0.90, 1.0, 1)
        _BottomColor ("Bottom Color", Color) = (0.55, 0.68, 0.82, 1)
        _HorizonGlow ("Horizon Glow", Color) = (1.0, 0.95, 0.82, 1)
        _SunDir ("Sun Direction", Vector) = (0.35, 0.45, 0.6, 0)
        _SunColor ("Sun Color", Color) = (1.0, 0.97, 0.86, 1)
        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.035
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _StarIntensity ("Stars", Range(0, 1)) = 0
        _MoonDir ("Moon Direction", Vector) = (-0.35, 0.45, -0.6, 0)
        _MoonColor ("Moon Color", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _TopColor, _MidColor, _BottomColor, _HorizonGlow, _SunColor, _CloudColor, _MoonColor;
            float4 _SunDir, _MoonDir;
            float _SunSize, _StarIntensity;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += noise(p) * a;
                    p *= 2.15;
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                // three-band anime gradient
                half3 col;
                if (y > 0.0)
                {
                    float t = pow(saturate(y), 0.62);
                    col = lerp(_MidColor.rgb, _TopColor.rgb, t);
                }
                else
                {
                    float t = pow(saturate(-y), 0.5);
                    col = lerp(_MidColor.rgb, _BottomColor.rgb, t);
                }

                // warm glow hugging the horizon
                float glow = pow(saturate(1.0 - abs(y)), 7.0);
                col = lerp(col, _HorizonGlow.rgb, glow * 0.55);

                // stars (only meaningful at night; fade near the horizon)
                if (y > 0.0 && _StarIntensity > 0.001)
                {
                    float2 suv = float2(atan2(d.z, d.x) * 57.3, asin(saturate(y)) * 57.3) * 1.7;
                    float2 cell = floor(suv);
                    float hs = hash(cell);
                    float2 f = frac(suv) - 0.5;
                    float2 jitter = float2(hash(cell + 3.1), hash(cell + 7.7)) - 0.5;
                    float star = smoothstep(0.94, 1.0, hs) * saturate(1.0 - length(f - jitter * 0.6) * 5.0);
                    star *= 0.55 + 0.45 * sin(_Time.y * 2.5 + hs * 60.0);
                    col += star * _StarIntensity * float3(0.9, 0.95, 1.0) * smoothstep(0.02, 0.25, y) * 1.6;
                }

                // sun disc + halo
                float3 sun = normalize(_SunDir.xyz);
                float cosang = dot(d, sun);
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.55, cosang);
                float halo = pow(saturate(cosang), 90.0) * 0.35;
                col += _SunColor.rgb * (disc * 1.4 + halo);

                // moon disc + soft halo
                float3 moon = normalize(_MoonDir.xyz);
                float mc = dot(d, moon);
                float mdisc = smoothstep(1.0 - 0.02, 1.0 - 0.012, mc);
                float mhalo = pow(saturate(mc), 300.0) * 0.5;
                col += _MoonColor.rgb * (mdisc * 1.2 + mhalo);

                // drifting flat anime clouds
                if (y > 0.02)
                {
                    float2 uv = d.xz / (y + 0.18);
                    uv *= 0.85;
                    uv.x += _Time.x * 1.1;
                    float c = fbm(uv * 1.35);
                    float cloud = smoothstep(0.58, 0.72, c);
                    float band = smoothstep(0.0, 0.24, y) * smoothstep(1.0, 0.32, y);
                    float shade = smoothstep(0.58, 0.95, c);
                    half3 cloudCol = lerp(_CloudColor.rgb * 0.86, _CloudColor.rgb, shade);
                    col = lerp(col, cloudCol, cloud * band * 0.85);
                }

                return half4(col, 1);
            }
            ENDCG
        }
    }
}
