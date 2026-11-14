// Assets/Resources/Shaders/ROVRBeacon.shader
// The study markers' beacon: a column of yellow light, translucent at the bottom and fading to clear at the
// top, with soft sides. Meant for Unity's cylinder primitive (object space y runs from -1 to 1).
// Unlit, built-in render pipeline, single-pass instanced stereo for the Quest. It sits in a Resources folder
// so builds include it even though only code (StudyTasks) uses it.
Shader "ROVR/Beacon"
{
    Properties
    {
        _Color ("Colour (alpha = opacity at the bottom)", Color) = (1, 0.85, 0.2, 0.55)
        _Falloff ("Fade towards the top (higher = sooner)", Range(0.5, 4)) = 1.6
        _Soft ("Soft sides (higher = softer)", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Falloff;
            half _Soft;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float height : TEXCOORD0;   // 0 at the bottom, 1 at the top
                float facing : TEXCOORD1;   // 1 where the side faces the viewer, 0 at its silhouette
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.height = saturate(v.vertex.y * 0.5 + 0.5);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = abs(dot(n, view));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float fade = pow(1.0 - i.height, _Falloff);
                float soft = pow(saturate(i.facing), _Soft);
                return fixed4(_Color.rgb, _Color.a * fade * soft);
            }
            ENDCG
        }
    }
}
