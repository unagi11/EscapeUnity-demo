Shader "Hidden/Escape/RoomTransitionMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Progress ("Progress", Range(-0.01, 1)) = -0.01
        _Mode ("Mode", Float) = 0
        _UseTextureColor ("Use Texture Color", Float) = 1
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
            sampler2D _MaskTex;
            fixed4 _Color;
            float _Progress;
            float _Mode;
            float _UseTextureColor;

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
                fixed4 source = tex2D(_MainTex, input.texcoord) * input.color;
                fixed mask = tex2D(_MaskTex, input.texcoord).a;
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    mask = input.texcoord.x;
                }
                else if (_Mode > 1.5 && _Mode < 2.5)
                {
                    mask = 1.0 - input.texcoord.y;
                }
                else if (_Mode > 2.5 && _Mode < 3.5)
                {
                    float2 delta = input.texcoord - 0.5;
                    mask = smoothstep(0.0, 0.70710678, length(delta));
                }
                else if (_Mode > 3.5 && _Mode < 4.5)
                {
                    mask = 1.0 - input.texcoord.x;
                }
                else if (_Mode > 4.5 && _Mode < 5.5)
                {
                    mask = input.texcoord.y;
                }

                fixed visible = step(mask, _Progress);
                if (_UseTextureColor > 0.5)
                {
                    return fixed4(source.rgb, source.a * visible);
                }

                return fixed4(input.color.rgb, input.color.a * visible);
            }
            ENDCG
        }
    }
}
