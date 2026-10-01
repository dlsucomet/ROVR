// Assets/Shaders/ROVRWorldSpaceTiled.shader
// Tiles a texture by WORLD position instead of by each object's own UVs.
//
// Why: ROVR builds walls and floors from stretched cubes. With normal UVs a texture is smeared
// across the whole 20 m wall. Here every surface gets the same texture density (one repeat per
// _TileSize metres), and the seams between separate wall pieces disappear.
//
// It only picks a projection direction per face, so it takes ONE texture sample. That is correct
// because the worlds are axis-aligned (walls and floors face +-X, +-Y or +-Z). Not for slopes.
// Built-in render pipeline (Standard lighting). Cheap enough for Quest.
Shader "ROVR/WorldSpaceTiled"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (tileable)", 2D) = "white" {}
        _TileSize ("Metres per texture repeat", Float) = 3
        _Glossiness ("Smoothness", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _TileSize;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = abs(IN.worldNormal);
            float2 uv;

            if (n.y >= n.x && n.y >= n.z)
                uv = IN.worldPos.xz;      // floors, wall tops
            else if (n.x >= n.z)
                uv = IN.worldPos.zy;      // walls facing east / west
            else
                uv = IN.worldPos.xy;      // walls facing north / south

            uv /= _TileSize;

            fixed4 c = tex2D(_MainTex, uv) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
