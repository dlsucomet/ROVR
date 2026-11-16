// A glow drawn over an object's own materials, brightest at its outline. Used by ROVR's Highlighter to
// show the options when the system asks "which one?". Drawn as a shell pushed a few millimetres out along
// the normals, so it never z-fights with the object's own surface (that flickered at a distance on the
// Quest), and alpha-blended in a saturated colour so it shows on light and dark objects alike.
// Built-in render pipeline, works in single-pass VR.
Shader "ROVR/Highlight"
{
    Properties
    {
        _GlowColor ("Glow colour (alpha = strength)", Color) = (0.1, 0.9, 1, 0.6)
        _Push ("Shell offset (m)", Float) = 0.006
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _GlowColor;
            float _Push;

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
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz + n * _Push;
                o.pos = UnityWorldToClipPos(world);
                float3 toEye = normalize(_WorldSpaceCameraPos - world);
                o.rim = 1.0 - saturate(dot(n, toEye));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float strength = _GlowColor.a * (0.3 + 0.7 * i.rim * i.rim);
                return fixed4(_GlowColor.rgb, strength);
            }
            ENDCG
        }
    }
}
