// Assets/Resources/Shaders/ROVRGlowTrail.shader
// The Maze's trail: a glowing strip with tread-like chevrons pointing along it and brighter edges, fading
// out at its sides. Expects MazeTrail's mesh: u = metres along the route, v = 0 at the left edge, 1 at the
// right. Static by default (_Speed 0); a moving floor pattern can make some people queasy in VR.
// Unlit, built-in render pipeline, single-pass instanced stereo for the Quest. It sits in a Resources folder
// so builds include it even though only code (MazeTrail) uses it.
Shader "ROVR/GlowTrail"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.55, 0.1, 0.9)
        _Period ("Metres between chevrons", Float) = 0.9
        _Sharpness ("Chevron angle", Range(0, 2)) = 0.5
        _Speed ("Chevrons' speed (m/s, 0 = still)", Float) = 0
        _Base ("Glow between the chevrons", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Period;
            float _Sharpness;
            float _Speed;
            half _Base;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float across = abs(i.uv.y * 2.0 - 1.0);                 // 0 in the middle, 1 at the edges
                float edgeFade = 1.0 - smoothstep(0.82, 1.0, across);    // soft sides
                float rails = smoothstep(0.6, 0.75, across);              // brighter edges, like tyre treads

                // Chevrons: lines of constant (u + k * across), so the middle leads: ">" towards the exit.
                float phase = frac((i.uv.x + across * _Sharpness * _Period - _Time.y * _Speed) / _Period);
                float chevron = smoothstep(0.0, 0.06, phase) * (1.0 - smoothstep(0.4, 0.46, phase));

                float glow = _Base + (1.0 - _Base) * max(chevron, rails);
                return fixed4(_Color.rgb, _Color.a * glow * edgeFade);
            }
            ENDCG
        }
    }
}
