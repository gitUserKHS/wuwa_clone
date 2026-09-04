Shader "WuWa/Critter"
{
    // Butterfly / bird wings: two quads flapped in the vertex shader, shaped by
    // an analytic mask, lit by a simple top-light and fogged.
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Flap ("Flap Speed", Float) = 20
        _FlapAmp ("Flap Amplitude", Float) = 0.7
        _Phase ("Phase", Float) = 0
        _Shape ("Shape (0 butterfly, 1 bird)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Flap, _FlapAmp, _Phase, _Shape;
            float _WuWaNight;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float lit : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                float flap = sin(_Time.y * _Flap + _Phase);
                float4 p = v.vertex;
                p.y += abs(p.x) * flap * _FlapAmp;
                p.x *= 1.0 - abs(flap) * 0.28;
                o.pos = UnityObjectToClipPos(p);
                o.uv = v.texcoord;
                o.lit = 0.75 + 0.25 * saturate(flap * 0.5 + 0.5);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;          // x: body (0) -> wing tip (1), y: hind (0) -> fore (1)
                float mask;
                fixed3 col;
                if (_Shape < 0.5)
                {
                    // butterfly silhouette: fore-wing ellipse + hind-wing ellipse, notch at the tip
                    float fore = 1.0 - length((uv - float2(0.50, 0.66)) / float2(0.52, 0.36));
                    float hind = 1.0 - length((uv - float2(0.36, 0.26)) / float2(0.40, 0.30));
                    mask = max(fore, hind);
                    float notch = length((uv - float2(0.98, 0.44)) / float2(0.16, 0.13)) - 1.0;
                    mask = min(mask, notch);
                    clip(mask);
                    // ink rim, dark body strip, one eye-spot and a pale band so it reads as a wing, not a blob
                    float rim = smoothstep(0.0, 0.14, mask);
                    float body = smoothstep(0.11, 0.02, uv.x);
                    float spot = smoothstep(0.11, 0.05, length((uv - float2(0.60, 0.68)) / float2(1.0, 1.35)));
                    float band = smoothstep(0.03, 0.0, abs(uv.x - 0.80)) * step(0.35, uv.y);
                    float vein = smoothstep(0.012, 0.0, abs(uv.y - 0.50)) * step(0.15, uv.x) * 0.5;
                    fixed3 base = saturate(_Color.rgb * 1.08);
                    col = base * lerp(0.28, 1.0, rim);
                    col = lerp(col, fixed3(0.10, 0.07, 0.12), saturate(body * 0.9 + spot * 0.75 + vein));
                    col = lerp(col, saturate(base * 1.3 + 0.15), band * 0.8);
                }
                else
                {
                    // bird: tapered wing
                    mask = (0.5 - abs(uv.y - 0.5)) - uv.x * 0.32 + 0.1;
                    clip(mask);
                    col = _Color.rgb;
                }
                col *= i.lit;
                col *= 1.0 - _WuWaNight * 0.6;
                fixed4 o = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
}
