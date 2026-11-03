// See-through copy of a gun part for the VR movement demonstrations (VRGhostDemo).
// Additive and rim-lit like the socket hologram, but depth-tested: the first pass draws the visible surface with bright
// edges, the second a faint version where the ghost is behind something (the trainee's hand, the controller, the frame),
// so the demonstration is never lost. The surface is pushed out a fraction of a millimetre along its normals, so at rest
// the ghost shows as a glowing shell around the real part instead of fighting it for the same depth.
// No lighting, no textures, no depth writes: cheap on Quest. Single-pass-instanced safe.
Shader "VRArmory/GhostDemo"
{
    Properties
    {
        _Color ("Color (alpha = visibility)", Color) = (0.3, 0.95, 0.88, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _Fill ("Face Opacity", Range(0, 1)) = 0.14
        _Hidden ("Opacity Behind Objects", Range(0, 1)) = 0.3
        _Inflate ("Inflate (metres)", Range(0, 0.003)) = 0.0006
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back

        CGINCLUDE
        #include "UnityCG.cginc"

        fixed4 _Color;
        half _RimPower, _Fill, _Hidden;
        float _Inflate;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 viewDir : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert (appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 worldNormal = UnityObjectToWorldNormal(v.normal);
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz + worldNormal * _Inflate;
            o.pos = UnityWorldToClipPos(worldPos);
            o.normal = worldNormal;
            o.viewDir = UnityWorldSpaceViewDir(worldPos); // per eye, set up by UNITY_SETUP_INSTANCE_ID
            return o;
        }

        // Faces turned towards the viewer stay faint; edges glow, which reads as an outline of the part.
        fixed4 Shade (v2f i, half strength)
        {
            half rim = 1.0 - saturate(dot(normalize(i.normal), normalize(i.viewDir)));
            half a = _Color.a * strength * (_Fill + (1.0 - _Fill) * pow(rim, _RimPower));
            return fixed4(_Color.rgb, a);
        }
        ENDCG

        Pass
        {
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return Shade(i, 1.0); }
            ENDCG
        }

        Pass
        {
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return Shade(i, _Hidden); }
            ENDCG
        }
    }
}
