Shader "Custom/MagicMirror/MirrorStencilWriter"
{
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "Queue" = "Geometry+1" // Vẽ sau các vật thể môi trường một chút
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StencilWriter"
            // Timeline cameras can cross the mirror volume and view it from the
            // opposite side. Both faces must write the stencil, otherwise the
            // SecretObject render pass disappears after the camera crosses it.
            Cull Off
            ZWrite Off          // QUAN TRỌNG: Không ghi chiều sâu để tránh lỗi xanh đục
            ColorMask 0         // Vô hình hoàn toàn
            
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace    // Ghi số 1 vào vùng gương
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
