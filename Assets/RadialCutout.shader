Shader "UI/RadialCutout"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}        // required by UI Image
        _DarkColor ("Dark Color", Color) = (0,0,0,0.9)
        _Radius ("Radius", Float) = 0.30
        _Feather ("Feather", Float) = 0.15
        _Center ("Center", Vector) = (0.5,0.5,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Lighting Off
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float4 _Center;
            float _Radius;
            float _Feather;
            float4 _DarkColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex); // use UI Image UVs
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Base sprite color (white if no sprite assigned)
                fixed4 baseCol = tex2D(_MainTex, i.uv) * i.color;

                // Screen-space UV for the mask (0..1 across the Image quad)
                // Reconstruct using normalized quad UVs (assuming sprite fills the Image)
                // If your Image uses a sprite with custom UVs, consider passing separate 0..1 coords.
                float2 screenUV = i.uv; // works for default white or full-rect sprites

                // Compute circular cutout
                float2 p = screenUV - _Center.xy;
                float r = length(p);

                float edgeStart = _Radius - _Feather * 0.5;
                float edgeEnd   = _Radius + _Feather * 0.5;
                float alphaMask = smoothstep(edgeStart, edgeEnd, r); // 0 inside, 1 outside

                // Darken outside, keep inside transparent
                fixed4 dark = fixed4(_DarkColor.rgb, _DarkColor.a * alphaMask);

                // UI overlay: we only output the dark mask; base sprite is not needed for the overlay.
                return dark;
            }
            ENDCG
        }
    }
}