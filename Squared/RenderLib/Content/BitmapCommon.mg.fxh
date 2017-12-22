#include "ViewTransformCommon.mg.fxh"

#ifndef MIP_BIAS
#define MIP_BIAS 0
#endif

// TODO: THIS PROBABLY NEEDS THE NO-HALF-PIXEL BEHAVIOR
float4 TransformPosition (float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
#ifdef FNA
    // ... Except for OpenGL, who don't need no half pixels
    float4 modelViewPos = mul(position, Viewport_ModelView);
#else
    float4 modelViewPos = mul(position, Viewport_ModelView) - float4(offset, offset, 0, 0);
#endif
    // Finally project after offsetting
    return mul(modelViewPos, Viewport_Projection);
}

uniform const float2 BitmapTextureSize;
uniform const float2 HalfTexel;

Texture2D BitmapTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (BitmapTexture);
    MipLODBias = MIP_BIAS;
};

Texture2D SecondTexture : register(t1);

sampler TextureSampler2 : register(s1) {
    Texture = (SecondTexture);
};

static const float2 Corners[] = {
    {0, 0},
    {1, 0},
    {1, 1},
    {0, 1}
};

inline float2 ComputeRegionSize(
    in float4 texRgn : POSITION1
) {
    return texRgn.zw - texRgn.xy;
}

inline float2 ComputeCorner(
    in int2 cornerIndex : BLENDINDICES0,
    in float2 regionSize
) {
    float2 corner = Corners[cornerIndex.x];
    return corner * regionSize;
}

inline float2 ComputeTexCoord(
    in int2 cornerIndex : BLENDINDICES0,
    in float2 corner,
    in float4 texRgn : POSITION1,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2
) {
    texTL = min(texRgn.xy, texRgn.zw);
    texBR = max(texRgn.xy, texRgn.zw);
    return clamp(
        texRgn.xy + corner, texTL, texBR
    );
}

inline float2 ComputeRotatedCorner(
    in float2 corner,
    in float4 texRgn : POSITION1,
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3
) {
    float2 regionSize = abs(texRgn.zw - texRgn.xy);

    corner = abs(corner);
    corner -= (scaleOrigin.zw * regionSize);
    float2 sinCos, rotatedCorner;
    corner *= scaleOrigin.xy;
    corner *= BitmapTextureSize;
    sincos(rotation, sinCos.x, sinCos.y);
    return float2(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y)
    );
}

void ScreenSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    in float4 texRgn : POSITION1, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3,
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    in int2 cornerIndex : BLENDINDICES0, // 0-3
    out float2 texCoord : TEXCOORD0,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2,
    out float4 result : POSITION0
) {
    //* OG
    float2 regionSize = ComputeRegionSize(texRgn);
    float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn, texTL, texBR);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);
    
    position.xy += rotatedCorner;

    result = TransformPosition(float4(position.xy, position.z, 1), 0.5);
    //*/

    /* EXPERIMENTAL
    float2 regionSize = ComputeRegionSize(texRgn);
    float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn, texTL, texBR);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);

    float2 fakeCorner = Corners[cornerIndex.x];
    rotatedCorner = ComputeRotatedCorner(fakeCorner, float4(0, 0, 1, 1), float4(1, 1, 0, 0), 0);
    
    position.xy += rotatedCorner;

    result = TransformPosition(float4(position.xy, position.z, 1), 0.5);
    result = float4(position.xy, 1, 1);
    //*/

    //* HORROR SHOW
    float2 fakeCorner = Corners[cornerIndex.x]; // * regionSize?
    result = float4(fakeCorner.x, fakeCorner.y, 1, 1); // PLEASE SOMETHING
    //*/

}

void WorldSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    in float4 texRgn : POSITION1, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3,
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    in int2 cornerIndex : BLENDINDICES0, // 0-3
    out float2 texCoord : TEXCOORD0,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2,
    out float4 result : POSITION0
) {
    float2 regionSize = ComputeRegionSize(texRgn);
    float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn, texTL, texBR);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);
    
    position.xy += rotatedCorner - Viewport_Position.xy;
    
    result = TransformPosition(float4(position.xy * Viewport_Scale.xy, position.z, 1), 0.5);

    //* HORROR SHOW
    float2 fakeCorner = Corners[cornerIndex.x]; // * regionSize?
    result = float4(fakeCorner.x, fakeCorner.y, 1, 1); // PLEASE SOMETHING
    //*/
    //result = float4(position.xy, position.z, 1);
}