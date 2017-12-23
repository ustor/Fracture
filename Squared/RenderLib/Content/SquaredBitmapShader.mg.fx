#include "BitmapCommon.mg.fxh"

uniform const float4 ShadowColor;
uniform const float2 ShadowOffset;

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    result += (addColor * result.a);

    // THIS ONE IS OKAY, I THINK ^^

    /* HORROR SHOW
    result = float4(texCoord.x, texCoord.y, 1, 1);
    //*/
}

void ShadowedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float2 shadowTexCoord = clamp(texCoord - (ShadowOffset * HalfTexel * 2), texTL, texBR);
    float4 texColor = tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    float4 shadowColor = ShadowColor * tex2D(TextureSampler, shadowTexCoord);
    float shadowAlpha = 1 - texColor.a;
    result = ((shadowColor * shadowAlpha) + (addColor * texColor.a)) * multiplyColor.a + (texColor * multiplyColor);

    // THIS ONE IS OKAY, I THINK ^^

    /* HORROR SHOW
    float antioptimization = (shadowColor.b * shadowAlpha) + (addColor.b * 1) * (multiplyColor.a * 0.1);
    result = float4(texCoord.x, texCoord.y, antioptimization, 1);
    //*/
}

void BasicPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);

    // THIS ONE IS OKAY, I THINK ^^

    /* HORROR SHOW
    result = float4(texCoord.x, texCoord.y, 1, 1);
    //*/
}

// UNIFORM STRUCT TESTING -- Monogame's pipeline tool doesn't like this
struct testStruct // Ignored/Optimized Out
{
    float2 structComponent;
    //float4x4 someMatrix;
};
//uniform testStruct ABC;
uniform float2 ABC_structComponent;
void UniformStructDummyShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
)
{
    //ABC.structComponent = float2(texTL.x, texBR.y);
    //ABC_structComponent = float2(texTL.x, texBR.y);

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    //result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    //result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, ABC.structComponent, ABC.structComponent));
    result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, ABC_structComponent, ABC_structComponent));
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique WorldSpaceBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 WorldSpaceVertexShader();
        pixelShader = compile ps_4_0 BasicPixelShader();
    }
}

technique ScreenSpaceBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_4_0 BasicPixelShader();
    }
}

technique WorldSpaceShadowedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 WorldSpaceVertexShader();
        pixelShader = compile ps_4_0 ShadowedPixelShader();
    }
}

technique ScreenSpaceShadowedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_4_0 ShadowedPixelShader();
    }
}

technique WorldSpaceBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 WorldSpaceVertexShader();
        pixelShader = compile ps_4_0 BasicPixelShaderWithDiscard();
    }
}

technique ScreenSpaceBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_4_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_4_0 BasicPixelShaderWithDiscard();
    }
}