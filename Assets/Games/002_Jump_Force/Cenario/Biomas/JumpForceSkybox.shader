Shader "Lumera/JumpForce/Skybox"
{
    Properties
    {
        [NoScaleOffset] _MainTex("Panorama 360", 2D) = "white" {}
        _Tint("Cor", Color) = (1,1,1,1)
        _Exposure("Exposicao", Range(0,3)) = 1
        _Rotation("Rotacao Y", Range(0,360)) = 0
        [HideInInspector] _NextTex("Proximo panorama", 2D) = "white" {}
        [HideInInspector] _NextTint("Proxima cor", Color) = (1,1,1,1)
        [HideInInspector] _NextExposure("Proxima exposicao", Float) = 1
        [HideInInspector] _NextRotation("Proxima rotacao", Float) = 0
        [HideInInspector] _Blend("Transicao", Range(0,1)) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _NextTex;
            half4 _Tint, _NextTint;
            half _Exposure, _NextExposure, _Blend;
            float _Rotation, _NextRotation;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            float2 PanoramaUV(float3 direction, float rotation)
            {
                float3 d = normalize(direction);
                float longitude = atan2(d.z, d.x) + rotation * UNITY_PI / 180.0;
                return float2(0.5 - longitude / (2.0 * UNITY_PI), 1.0 - acos(clamp(d.y, -1.0, 1.0)) / UNITY_PI);
            }
            half4 frag(v2f i) : SV_Target
            {
                half3 color = tex2D(_MainTex, PanoramaUV(i.direction, _Rotation)).rgb * _Tint.rgb * _Exposure;
                // Fora da transicao ha uma amostra de textura e uma passagem de ceu.
                UNITY_BRANCH if (_Blend > 0.0001h)
                {
                    half3 next = tex2D(_NextTex, PanoramaUV(i.direction, _NextRotation)).rgb * _NextTint.rgb * _NextExposure;
                    color = lerp(color, next, _Blend);
                }
                return half4(color, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
