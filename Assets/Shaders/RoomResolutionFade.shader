Shader "Hidden/Escape/RoomResolutionFade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _PixelHeight ("Pixel Height", Float) = 1080
        _TextureSize ("Texture Size", Vector) = (1920, 1080, 0.00052, 0.00093)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _PixelHeight;
            float4 _TextureSize;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.texcoord = input.texcoord;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float width = max(_TextureSize.x, 1.0);
                float height = max(_TextureSize.y, 1.0);
                float pixelHeight = max(_PixelHeight, 1.0);
                float pixelRows = max(floor(pixelHeight + 0.5), 1.0);
                float pixelWidth = max(floor(pixelRows * width / height + 0.5), 1.0);
                float2 cells = float2(pixelWidth, pixelRows);
                float2 uv = (floor(input.texcoord * cells) + 0.5) / cells;
                fixed4 color = tex2D(_MainTex, uv) * input.color;
                color.a = input.color.a;
                return color;
            }
            ENDCG
        }
    }
}
