Shader "AFrameWork/GameUI/HealthBarGPU"
{
    Properties
    {
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 0.7)
        _BorderColor ("Border Color", Color) = (0, 0, 0, 0.5)
        _BorderWidth ("Border Width", Range(0, 0.2)) = 0.04
        _SizeScale ("Size Scale", Range(0.001, 0.01)) = 0.008
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "HealthBarGPU"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma instancing_options proceduralinstancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct HealthBarInstance
            {
                float4 position;  // xyz = world pos, w = fill percent (0-1)
                float4 color;     // rgba fill color
                float4 size;      // x = width, y = height, z = unused, w = visible
            };

            StructuredBuffer<HealthBarInstance> _InstanceBuffer;

            CBUFFER_START(UnityPerMaterial)
                float4 _BackgroundColor;
                float4 _BorderColor;
                float _BorderWidth;
                float _SizeScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fillPercent : TEXCOORD1;
                float4 fillColor : TEXCOORD2;
                float visible : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                HealthBarInstance inst = _InstanceBuffer[input.instanceID];

                // 不可见 -> 退化三角形 (光栅器自动跳过)
                if (inst.size.w < 0.5)
                {
                    output.positionCS = float4(0, 0, 0, 1);
                    output.visible = 0;
                    return output;
                }

                // 世界坐标 -> 视空间
                float3 viewPos = mul(UNITY_MATRIX_V, float4(inst.position.xyz, 1)).xyz;

                // 相机背面剔除
                if (viewPos.z > 0)
                {
                    output.positionCS = float4(0, 0, 0, 1);
                    output.visible = 0;
                    return output;
                }

                output.visible = 1;

                // 距离自适应缩放 (保持屏幕尺寸一致)
                float viewZ = -viewPos.z;
                float scale = viewZ * _SizeScale;

                // 视空间公告板偏移 (X=右, Y=上)
                viewPos.x += input.positionOS.x * inst.size.x * scale;
                viewPos.y += input.positionOS.y * inst.size.y * scale;

                output.positionCS = mul(UNITY_MATRIX_P, float4(viewPos, 1));
                output.uv = input.uv;
                output.fillPercent = inst.position.w;
                output.fillColor = inst.color;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                if (input.visible < 0.5)
                    discard;

                float2 uv = input.uv;

                // 背景
                float4 color = _BackgroundColor;

                // 填充 (UV.x <= 填充百分比)
                if (uv.x <= input.fillPercent)
                    color = input.fillColor;

                // 边框
                if (uv.x < _BorderWidth || uv.x > 1.0 - _BorderWidth ||
                    uv.y < _BorderWidth || uv.y > 1.0 - _BorderWidth)
                    color = _BorderColor;

                return color;
            }
            ENDHLSL
        }
    }
}
