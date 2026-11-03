// SMAA 1x (PristonSmaa) on the tonemapped 8-bit image: luma edge detection, blending weight
// calculation (orthogonal + diagonal patterns, corner rounding) and neighbourhood blending.
//
// Adapted for the SM 6.0 library (Vulkan) from SMAA.hlsl, "SMAA: Enhanced Subpixel Morphological
// Antialiasing", Jimenez, Echevarria, Sousa and Gutierrez, Eurographics 2012, http://www.iryoku.com/smaa/
// (preset HIGH; the precomputed area and search textures are uploaded by PristonSmaa.cs). Licence of the original:
//
// Copyright (C) 2013 Jorge Jimenez (jorge@iryoku.com)
// Copyright (C) 2013 Jose I. Echevarria (joseignacioechevarria@gmail.com)
// Copyright (C) 2013 Belen Masia (bmasia@unizar.es)
// Copyright (C) 2013 Fernando Navarro (fernandn@microsoft.com)
// Copyright (C) 2013 Diego Gutierrez (diegog@unizar.es)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy this software and
// associated documentation files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions: The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software. As clarification, there is no
// requirement that the copyright notice and permission be included in binary distributions of the
// Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
// LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
// NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

// ---- Configuration (SMAA_PRESET_HIGH) ----------------------------------------------------------------
#define SMAA_THRESHOLD 0.1
#define SMAA_MAX_SEARCH_STEPS 16
#define SMAA_MAX_SEARCH_STEPS_DIAG 8
#define SMAA_CORNER_ROUNDING 25
#define SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR 2.0

#define SMAA_AREATEX_MAX_DISTANCE 16
#define SMAA_AREATEX_MAX_DISTANCE_DIAG 20
#define SMAA_AREATEX_PIXEL_SIZE (1.0 / float2(160.0, 560.0))
#define SMAA_AREATEX_SUBTEX_SIZE (1.0 / 7.0)
#define SMAA_SEARCHTEX_SIZE float2(66.0, 33.0)
#define SMAA_SEARCHTEX_PACKED_SIZE float2(64.0, 16.0)
#define SMAA_CORNER_ROUNDING_NORM (float(SMAA_CORNER_ROUNDING) / 100.0)

cbuffer PostSmaaConstants : register(b0)
{
    float4 RtMetrics; // (1 / width, 1 / height, width, height)
};

// The tonemapped, sRGB-encoded image (luma edge detection wants gamma-space values). The edge pass reads it with
// the point sampler, the blending pass with the linear one: one sampler per shader, so the texture keeps its name.
Texture2D<float4> Color : register(t0);
Texture2D<float4> Edges : register(t1);
Texture2D<float4> Blend : register(t2);
Texture2D<float4> AreaTex : register(t3);     // precomputed tables (RG of RGBA8)
Texture2D<float4> SearchTex : register(t4);   // (R of RGBA8)
SamplerState ColorPoint : register(s0) = sampler_state { Texture = (Color); MinFilter = Point; MagFilter = Point; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };
SamplerState ColorLinear : register(s0) = sampler_state { Texture = (Color); MinFilter = Linear; MagFilter = Linear; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };
SamplerState EdgesSampler : register(s1) = sampler_state { Texture = (Edges); MinFilter = Linear; MagFilter = Linear; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };
SamplerState BlendSampler : register(s2) = sampler_state { Texture = (Blend); MinFilter = Linear; MagFilter = Linear; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };
SamplerState AreaSampler : register(s3) = sampler_state { Texture = (AreaTex); MinFilter = Linear; MagFilter = Linear; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };
SamplerState SearchSampler : register(s4) = sampler_state { Texture = (SearchTex); MinFilter = Point; MagFilter = Point; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };

float4 SampleLevelZero(Texture2D<float4> t, SamplerState s, float2 uv)
{
    return t.SampleLevel(s, uv, 0);
}

float4 SampleLevelZeroOffset(Texture2D<float4> t, SamplerState s, float2 uv, float2 offset)
{
    return t.SampleLevel(s, uv + offset * RtMetrics.xy, 0);
}

void Movc(bool2 cond, inout float2 variable, float2 value)
{
    [flatten] if (cond.x) variable.x = value.x;
    [flatten] if (cond.y) variable.y = value.y;
}

void Movc(bool4 cond, inout float4 variable, float4 value)
{
    Movc(cond.xy, variable.xy, value.xy);
    Movc(cond.zw, variable.zw, value.zw);
}

// ---- Pass 1: luma edge detection ------------------------------------------------------------------------
struct EdgeVSOut
{
    float4 Position : SV_Position;
    float2 Uv       : TEXCOORD0;
    float4 Offset0  : TEXCOORD1;
    float4 Offset1  : TEXCOORD2;
    float4 Offset2  : TEXCOORD3;
};

EdgeVSOut VS_Edges(FullscreenIn i)
{
    EdgeVSOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    o.Offset0 = RtMetrics.xyxy * float4(-1.0, 0.0, 0.0, -1.0) + i.Uv.xyxy;
    o.Offset1 = RtMetrics.xyxy * float4(1.0, 0.0, 0.0, 1.0) + i.Uv.xyxy;
    o.Offset2 = RtMetrics.xyxy * float4(-2.0, 0.0, 0.0, -2.0) + i.Uv.xyxy;
    return o;
}

float4 PS_Edges(EdgeVSOut i) : SV_Target0
{
    float2 threshold = float2(SMAA_THRESHOLD, SMAA_THRESHOLD);
    float L = Luminance(SampleLevelZero(Color, ColorPoint, i.Uv).rgb);
    float Lleft = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset0.xy).rgb);
    float Ltop = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset0.zw).rgb);

    float4 delta;
    delta.xy = abs(L - float2(Lleft, Ltop));
    float2 edges = step(threshold, delta.xy);
    if (dot(edges, float2(1.0, 1.0)) == 0.0)
        discard;

    float Lright = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset1.xy).rgb);
    float Lbottom = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset1.zw).rgb);
    delta.zw = abs(L - float2(Lright, Lbottom));
    float2 maxDelta = max(delta.xy, delta.zw);

    float Lleftleft = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset2.xy).rgb);
    float Ltoptop = Luminance(SampleLevelZero(Color, ColorPoint, i.Offset2.zw).rgb);
    delta.zw = abs(float2(Lleft, Ltop) - float2(Lleftleft, Ltoptop));
    maxDelta = max(maxDelta.xy, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);

    // Local contrast adaptation.
    edges.xy *= step(finalDelta, SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);
    return float4(edges, 0.0, 1.0);
}

// ---- Pass 2: blending weights -----------------------------------------------------------------------------
float2 DecodeDiagBilinearAccess(float2 e)
{
    e.r = e.r * abs(5.0 * e.r - 5.0 * 0.75);
    return round(e);
}

float4 DecodeDiagBilinearAccess(float4 e)
{
    e.rb = e.rb * abs(5.0 * e.rb - 5.0 * 0.75);
    return round(e);
}

float2 SearchDiag1(float2 texcoord, float2 dir, out float2 e)
{
    float4 coord = float4(texcoord, -1.0, 1.0);
    float3 t = float3(RtMetrics.xy, 1.0);
    e = float2(0.0, 0.0);
    [loop]
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) && coord.w > 0.9)
    {
        coord.xyz = t * float3(dir, 1.0) + coord.xyz;
        e = SampleLevelZero(Edges, EdgesSampler, coord.xy).rg;
        coord.w = dot(e, float2(0.5, 0.5));
    }
    return coord.zw;
}

float2 SearchDiag2(float2 texcoord, float2 dir, out float2 e)
{
    float4 coord = float4(texcoord, -1.0, 1.0);
    coord.x += 0.25 * RtMetrics.x; // fetch both edges at once with bilinear filtering
    float3 t = float3(RtMetrics.xy, 1.0);
    e = float2(0.0, 0.0);
    [loop]
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) && coord.w > 0.9)
    {
        coord.xyz = t * float3(dir, 1.0) + coord.xyz;
        e = SampleLevelZero(Edges, EdgesSampler, coord.xy).rg;
        e = DecodeDiagBilinearAccess(e);
        coord.w = dot(e, float2(0.5, 0.5));
    }
    return coord.zw;
}

float2 AreaDiag(float2 dist, float2 e, float offset)
{
    float2 texcoord = float2(SMAA_AREATEX_MAX_DISTANCE_DIAG, SMAA_AREATEX_MAX_DISTANCE_DIAG) * e + dist;
    texcoord = SMAA_AREATEX_PIXEL_SIZE * texcoord + 0.5 * SMAA_AREATEX_PIXEL_SIZE;
    texcoord.x += 0.5;                                 // diagonal areas: second half of the texture
    texcoord.y += SMAA_AREATEX_SUBTEX_SIZE * offset;
    return SampleLevelZero(AreaTex, AreaSampler, texcoord).rg;
}

float2 CalculateDiagWeights(float2 texcoord, float2 e)
{
    float2 weights = float2(0.0, 0.0);
    float4 d;
    float2 end;
    if (e.r > 0.0)
    {
        d.xz = SearchDiag1(texcoord, float2(-1.0, 1.0), end);
        d.x += float(end.y > 0.9);
    }
    else
    {
        d.xz = float2(0.0, 0.0);
    }
    d.yw = SearchDiag1(texcoord, float2(1.0, -1.0), end);

    [branch]
    if (d.x + d.y > 2.0)
    {
        float4 coords = float4(-d.x + 0.25, d.x, d.y, -d.y - 0.25) * RtMetrics.xyxy + texcoord.xyxy;
        float4 c;
        c.xy = SampleLevelZeroOffset(Edges, EdgesSampler, coords.xy, float2(-1.0, 0.0)).rg;
        c.zw = SampleLevelZeroOffset(Edges, EdgesSampler, coords.zw, float2(1.0, 0.0)).rg;
        c.yxwz = DecodeDiagBilinearAccess(c.xyzw);
        float2 cc = float2(2.0, 2.0) * c.xz + c.yw;
        Movc(bool2(step(0.9, d.zw)), cc, float2(0.0, 0.0));
        weights += AreaDiag(d.xy, cc, 0.0);
    }

    d.xz = SearchDiag2(texcoord, float2(-1.0, -1.0), end);
    if (SampleLevelZeroOffset(Edges, EdgesSampler, texcoord, float2(1.0, 0.0)).r > 0.0)
    {
        d.yw = SearchDiag2(texcoord, float2(1.0, 1.0), end);
        d.y += float(end.y > 0.9);
    }
    else
    {
        d.yw = float2(0.0, 0.0);
    }

    [branch]
    if (d.x + d.y > 2.0)
    {
        float4 coords = float4(-d.x, -d.x, d.y, d.y) * RtMetrics.xyxy + texcoord.xyxy;
        float4 c;
        c.x = SampleLevelZeroOffset(Edges, EdgesSampler, coords.xy, float2(-1.0, 0.0)).g;
        c.y = SampleLevelZeroOffset(Edges, EdgesSampler, coords.xy, float2(0.0, -1.0)).r;
        c.zw = SampleLevelZeroOffset(Edges, EdgesSampler, coords.zw, float2(1.0, 0.0)).gr;
        float2 cc = float2(2.0, 2.0) * c.xz + c.yw;
        Movc(bool2(step(0.9, d.zw)), cc, float2(0.0, 0.0));
        weights += AreaDiag(d.xy, cc, 0.0).gr;
    }
    return weights;
}

float SearchLength(float2 e, float offset)
{
    // The search texture is flipped vertically, left and right cases side by side.
    float2 scale = SMAA_SEARCHTEX_SIZE * float2(0.5, -1.0);
    float2 bias = SMAA_SEARCHTEX_SIZE * float2(offset, 1.0);
    scale += float2(-1.0, 1.0);
    bias += float2(0.5, -0.5);
    scale *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    bias *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    return SampleLevelZero(SearchTex, SearchSampler, scale * e + bias).r;
}

float SearchXLeft(float2 texcoord, float end)
{
    float2 e = float2(0.0, 1.0);
    [loop]
    while (texcoord.x > end && e.g > 0.8281 && e.r == 0.0)
    {
        e = SampleLevelZero(Edges, EdgesSampler, texcoord).rg;
        texcoord = -float2(2.0, 0.0) * RtMetrics.xy + texcoord;
    }
    float offset = -(255.0 / 127.0) * SearchLength(e, 0.0) + 3.25;
    return RtMetrics.x * offset + texcoord.x;
}

float SearchXRight(float2 texcoord, float end)
{
    float2 e = float2(0.0, 1.0);
    [loop]
    while (texcoord.x < end && e.g > 0.8281 && e.r == 0.0)
    {
        e = SampleLevelZero(Edges, EdgesSampler, texcoord).rg;
        texcoord = float2(2.0, 0.0) * RtMetrics.xy + texcoord;
    }
    float offset = -(255.0 / 127.0) * SearchLength(e, 0.5) + 3.25;
    return -RtMetrics.x * offset + texcoord.x;
}

float SearchYUp(float2 texcoord, float end)
{
    float2 e = float2(1.0, 0.0);
    [loop]
    while (texcoord.y > end && e.r > 0.8281 && e.g == 0.0)
    {
        e = SampleLevelZero(Edges, EdgesSampler, texcoord).rg;
        texcoord = -float2(0.0, 2.0) * RtMetrics.xy + texcoord;
    }
    float offset = -(255.0 / 127.0) * SearchLength(e.gr, 0.0) + 3.25;
    return RtMetrics.y * offset + texcoord.y;
}

float SearchYDown(float2 texcoord, float end)
{
    float2 e = float2(1.0, 0.0);
    [loop]
    while (texcoord.y < end && e.r > 0.8281 && e.g == 0.0)
    {
        e = SampleLevelZero(Edges, EdgesSampler, texcoord).rg;
        texcoord = float2(0.0, 2.0) * RtMetrics.xy + texcoord;
    }
    float offset = -(255.0 / 127.0) * SearchLength(e.gr, 0.5) + 3.25;
    return -RtMetrics.y * offset + texcoord.y;
}

float2 Area(float2 dist, float e1, float e2, float offset)
{
    // Rounding prevents precision errors of bilinear filtering.
    float2 texcoord = float2(SMAA_AREATEX_MAX_DISTANCE, SMAA_AREATEX_MAX_DISTANCE) * round(4.0 * float2(e1, e2)) + dist;
    texcoord = SMAA_AREATEX_PIXEL_SIZE * texcoord + 0.5 * SMAA_AREATEX_PIXEL_SIZE;
    texcoord.y = SMAA_AREATEX_SUBTEX_SIZE * offset + texcoord.y;
    return SampleLevelZero(AreaTex, AreaSampler, texcoord).rg;
}

void DetectHorizontalCornerPattern(inout float2 weights, float4 texcoord, float2 d)
{
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;
    rounding /= leftRight.x + leftRight.y; // less blending for pixels in the centre of a line
    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.xy, float2(0.0, 1.0)).r;
    factor.x -= rounding.y * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.zw, float2(1.0, 1.0)).r;
    factor.y -= rounding.x * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.xy, float2(0.0, -2.0)).r;
    factor.y -= rounding.y * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.zw, float2(1.0, -2.0)).r;
    weights *= saturate(factor);
}

void DetectVerticalCornerPattern(inout float2 weights, float4 texcoord, float2 d)
{
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;
    rounding /= leftRight.x + leftRight.y;
    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.xy, float2(1.0, 0.0)).g;
    factor.x -= rounding.y * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.zw, float2(1.0, 1.0)).g;
    factor.y -= rounding.x * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.xy, float2(-2.0, 0.0)).g;
    factor.y -= rounding.y * SampleLevelZeroOffset(Edges, EdgesSampler, texcoord.zw, float2(-2.0, 1.0)).g;
    weights *= saturate(factor);
}

struct WeightsVSOut
{
    float4 Position : SV_Position;
    float2 Uv       : TEXCOORD0;
    float2 Pixel    : TEXCOORD1;
    float4 Offset0  : TEXCOORD2;
    float4 Offset1  : TEXCOORD3;
    float4 Offset2  : TEXCOORD4;
};

WeightsVSOut VS_Weights(FullscreenIn i)
{
    WeightsVSOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    o.Pixel = i.Uv * RtMetrics.zw;
    // Offsets for the searches (pseudo-gather of four edges per bilinear fetch) ...
    o.Offset0 = RtMetrics.xyxy * float4(-0.25, -0.125, 1.25, -0.125) + i.Uv.xyxy;
    o.Offset1 = RtMetrics.xyxy * float4(-0.125, -0.25, -0.125, 1.25) + i.Uv.xyxy;
    // ... and the ends of the search loops.
    o.Offset2 = RtMetrics.xxyy * float4(-2.0, 2.0, -2.0, 2.0) * float(SMAA_MAX_SEARCH_STEPS)
              + float4(o.Offset0.xz, o.Offset1.yw);
    return o;
}

float4 PS_Weights(WeightsVSOut i) : SV_Target0
{
    float4 weights = float4(0.0, 0.0, 0.0, 0.0);
    float2 e = SampleLevelZero(Edges, EdgesSampler, i.Uv).rg;

    [branch]
    if (e.g > 0.0) // edge at north
    {
        // Diagonals have both north and west edges: searching from one of them is enough; they win
        // over the horizontal/vertical patterns.
        weights.rg = CalculateDiagWeights(i.Uv, e);
        [branch]
        if (weights.r == -weights.g)
        {
            float2 d;
            float3 coords;
            coords.x = SearchXLeft(i.Offset0.xy, i.Offset2.x);
            coords.y = i.Offset1.y;
            d.x = coords.x;
            float e1 = SampleLevelZero(Edges, EdgesSampler, coords.xy).r;
            coords.z = SearchXRight(i.Offset0.zw, i.Offset2.y);
            d.y = coords.z;
            d = abs(round(RtMetrics.zz * d - i.Pixel.xx));
            float2 sqrtD = sqrt(d);
            float e2 = SampleLevelZeroOffset(Edges, EdgesSampler, coords.zy, float2(1.0, 0.0)).r;
            weights.rg = Area(sqrtD, e1, e2, 0.0);
            coords.y = i.Uv.y;
            DetectHorizontalCornerPattern(weights.rg, coords.xyzy, d);
        }
        else
        {
            e.r = 0.0; // skip vertical processing
        }
    }

    [branch]
    if (e.r > 0.0) // edge at west
    {
        float2 d;
        float3 coords;
        coords.y = SearchYUp(i.Offset1.xy, i.Offset2.z);
        coords.x = i.Offset0.x;
        d.x = coords.y;
        float e1 = SampleLevelZero(Edges, EdgesSampler, coords.xy).g;
        coords.z = SearchYDown(i.Offset1.zw, i.Offset2.w);
        d.y = coords.z;
        d = abs(round(RtMetrics.ww * d - i.Pixel.yy));
        float2 sqrtD = sqrt(d);
        float e2 = SampleLevelZeroOffset(Edges, EdgesSampler, coords.xz, float2(0.0, 1.0)).g;
        weights.ba = Area(sqrtD, e1, e2, 0.0);
        coords.x = i.Uv.x;
        DetectVerticalCornerPattern(weights.ba, coords.xyxz, d);
    }
    return weights;
}

// ---- Pass 3: neighbourhood blending ------------------------------------------------------------------------
struct BlendVSOut
{
    float4 Position : SV_Position;
    float2 Uv       : TEXCOORD0;
    float4 Offset   : TEXCOORD1;
};

BlendVSOut VS_Blend(FullscreenIn i)
{
    BlendVSOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    o.Offset = RtMetrics.xyxy * float4(1.0, 0.0, 0.0, 1.0) + i.Uv.xyxy;
    return o;
}

float4 PS_Blend(BlendVSOut i) : SV_Target0
{
    float4 a;
    a.x = SampleLevelZero(Blend, BlendSampler, i.Offset.xy).a; // right
    a.y = SampleLevelZero(Blend, BlendSampler, i.Offset.zw).g; // top
    a.wz = SampleLevelZero(Blend, BlendSampler, i.Uv).xz;      // bottom / left

    [branch]
    if (dot(a, float4(1.0, 1.0, 1.0, 1.0)) < 1e-5)
        return float4(SampleLevelZero(Color, ColorLinear, i.Uv).rgb, 1.0);

    bool h = max(a.x, a.z) > max(a.y, a.w); // max(horizontal) > max(vertical)
    float4 blendingOffset = float4(0.0, a.y, 0.0, a.w);
    float2 blendingWeight = a.yw;
    Movc(bool4(h, h, h, h), blendingOffset, float4(a.x, 0.0, a.z, 0.0));
    Movc(bool2(h, h), blendingWeight, a.xz);
    blendingWeight /= dot(blendingWeight, float2(1.0, 1.0));
    float4 blendingCoord = blendingOffset * float4(RtMetrics.xy, -RtMetrics.xy) + i.Uv.xyxy;
    // Bilinear filtering mixes the pixel with the chosen neighbour.
    float3 color = blendingWeight.x * SampleLevelZero(Color, ColorLinear, blendingCoord.xy).rgb;
    color += blendingWeight.y * SampleLevelZero(Color, ColorLinear, blendingCoord.zw).rgb;
    return float4(color, 1.0);
}

technique EdgeDetection
{
    pass P0
    {
        VertexShader = compile vs_6_0 VS_Edges();
        PixelShader = compile ps_6_0 PS_Edges();
    }
}

technique BlendingWeights
{
    pass P0
    {
        VertexShader = compile vs_6_0 VS_Weights();
        PixelShader = compile ps_6_0 PS_Weights();
    }
}

technique NeighborhoodBlending
{
    pass P0
    {
        VertexShader = compile vs_6_0 VS_Blend();
        PixelShader = compile ps_6_0 PS_Blend();
    }
}
