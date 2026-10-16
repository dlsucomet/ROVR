// A soft glow drawn over an object's own materials, brightest at its outline. Used by ROVR's
// Highlighter to show the options when the system asks "which one?". Additive, so it only adds
// light; built-in render pipeline, works in single-pass VR.
Shader "ROVR/Highlight"
{
    Properties
    {
        _GlowColor ("Glow colour (alpha = strength)", Color) = (1, 0.8, 0.2, 0.6)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _GlowColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float rim : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 toEye = normalize(WorldSpaceViewDir(v.vertex));
                o.rim = 1.0 - saturate(dot(n, toEye));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float strength = _GlowColor.a * (0.35 + 0.65 * i.rim);
                return fixed4(_GlowColor.rgb, strength);
            }
            ENDCG
        }
    }
}
