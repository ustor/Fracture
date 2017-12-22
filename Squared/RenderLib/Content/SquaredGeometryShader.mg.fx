#include "GeometryCommon.mg.fxh"

uniform texture BasicTexture : register(t0);

const sampler TextureSampler = sampler_state {
    Texture = (BasicTexture);
    
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

void ScreenSpaceVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void WorldSpaceVertexShader(
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    position -= Viewport_Position.xy;
    position *= Viewport_Scale.xy;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void VertexColorPixelShader(
    inout float4 color : COLOR0
) {
}

void TexturedPixelShader(
    inout float4 color : COLOR0,
    in float2 texCoord : TEXCOORD0
) {
    color *= tex2D(TextureSampler, texCoord);
}

technique ScreenSpaceUntextured
{
    pass P0
    {
        vertexShader = compile vs_4_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_4_0 VertexColorPixelShader();
    }
}

technique WorldSpaceUntextured
{
    pass P0
    {
        vertexShader = compile vs_4_0 WorldSpaceVertexShader();
        pixelShader = compile ps_4_0 VertexColorPixelShader();
    }
}