#include "BitmapCommon.mg.fxh"

void LightmappedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    texCoord = clamp(texCoord, texTL, texBR);

    float4 lightmapColor = tex2D(TextureSampler2, texCoord) * 2;
    lightmapColor.a = 1;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    multiplyColor = multiplyColor * lightmapColor;

    result = multiplyColor * tex2D(TextureSampler, texCoord);
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique ScreenSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique WorldSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}