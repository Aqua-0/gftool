#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D BaseColorMap1;
uniform sampler2D BaseColorMap2;
uniform sampler2D NormalMap;
uniform sampler2D NormalMap1;
uniform sampler2D NormalMap2;
uniform sampler2D PackedMap;
uniform sampler2D PackedMap1;
uniform sampler2D PackedMap2;
uniform sampler2D RoughnessMap;
uniform sampler2D RoughnessMap1;
uniform sampler2D RoughnessMap2;
uniform sampler2D MetallicMap;
uniform sampler2D MetallicMap1;
uniform sampler2D AOMap;
uniform sampler2D AOMap1;
uniform sampler2D EmissionColorMap;

uniform vec4 UVScaleOffset;
uniform int UVTransformMode;

uniform bool EnableBaseColorMap;
uniform bool EnableBaseColorMap1;
uniform bool EnableBaseColorMap2;
uniform bool EnableNormalMap;
uniform bool EnableNormalMap1;
uniform bool EnableNormalMap2;
uniform bool EnablePackedMap;
uniform bool EnablePackedMap1;
uniform bool EnablePackedMap2;
uniform bool EnableRoughnessMap;
uniform bool EnableRoughnessMap1;
uniform bool EnableRoughnessMap2;
uniform bool EnableMetallicMap;
uniform bool EnableAOMap;
uniform bool EnableEmissionColorMap;
uniform bool EnableAlphaTest;
uniform float AlphaTestThreshold;

uniform bool EnableVertexBaseColor;
uniform bool EnableVertexSelectedChannel;
uniform bool EnableVertexColor;
uniform bool EnableWorldXzUv;
uniform int VertexColorChannel;
uniform int VertexColorChannel1;
uniform float VertexColorIntensity;

uniform float Influence;
uniform float Contrast;
uniform float Tiling;
uniform float Influence1;
uniform float Contrast1;
uniform float Tiling1;
uniform float Tiling2;
uniform float RoughnessIntensity;
uniform float RoughnessIntensity1;
uniform float RoughnessIntensity2;
uniform float NormalIntensity;
uniform float NormalIntensity1;
uniform float NormalIntensity2;
uniform float LayerMaskScale1;
uniform bool EnableMetallicValue;
uniform float MetallicIntensity;
uniform float MetallicIntensity1;
uniform float MetallicIntensity2;

uniform float NormalHeight;
uniform float Metallic;
uniform float Roughness;
uniform float EmissionIntensity;

uniform vec3 CameraPos;
uniform bool HasTangents;
uniform bool HasBinormals;
uniform bool HasUv1;
uniform bool FlipNormalY;
uniform bool ReconstructNormalZ;

layout (location = 0) out vec4 gAlbedo;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out vec4 gSpecular;
layout (location = 3) out vec4 gAO;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;
in vec4 UV01;
in vec4 Color;
in vec3 Tangent;
in vec3 Bitangent;
in vec3 Binormal;

vec2 ChooseUv(int idx)
{
    if (idx == 1)
    {
        if (!HasUv1)
        {
            return UV01.xy;
        }
        return UV01.zw;
    }
    return UV01.xy;
}

vec2 XformUv(vec2 uv, vec4 srt, int mode)
{
    if (mode == 1)
    {
        return uv + srt.zw;
    }
    return uv * srt.xy + srt.zw;
}

vec2 WrapUvIfOutside01(vec2 uv)
{
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
    {
        return fract(uv);
    }
    return uv;
}

float PickChan(vec4 v, int c)
{
    if (c == 1) return v.g;
    if (c == 2) return v.b;
    if (c == 3) return v.a;
    return v.r;
}

mat3 MakeFrame(vec3 n, vec2 uv)
{
    if (HasTangents)
    {
        vec3 bt = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
        if (dot(bt, bt) < 0.0001)
        {
            bt = normalize(cross(n, normalize(Tangent)));
        }
        return mat3(normalize(Tangent), bt, n);
    }

    vec3 dp1 = dFdx(FragPos);
    vec3 dp2 = dFdy(FragPos);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);
    vec3 dp2p = cross(dp2, n);
    vec3 dp1p = cross(n, dp1);
    vec3 tt = dp2p * duv1.x + dp1p * duv2.x;
    vec3 bb = dp2p * duv1.y + dp1p * duv2.y;
    float inv = inversesqrt(max(dot(tt, tt), dot(bb, bb)));
    return mat3(tt * inv, bb * inv, n);
}

vec3 DecodeNm(vec3 s)
{
    vec2 xy = s.xy * 2.0 - 1.0;
    if (FlipNormalY)
    {
        xy.y = -xy.y;
    }
    float zz = sqrt(max(1.0 - dot(xy, xy), 0.0));
    if (!ReconstructNormalZ)
    {
        zz = s.z * 2.0 - 1.0;
    }
    return normalize(vec3(xy, zz));
}

float CheapContrast(float t, float a)
{
    return clamp(mix(-a, a + 1.0, t), 0.0, 1.0);
}

void HeightLerp2(
    vec3 c0, vec3 n0, vec3 orh0,
    vec3 c1, vec3 n1, vec3 orh1,
    float phase, float height, float influence,
    out vec3 outC, out vec3 outN, out vec3 outOrh)
{
    float control = clamp((phase * 2.0) + (height - 1.0), 0.0, 1.0);
    float blend = CheapContrast(control, influence);
    outC = mix(c0, c1, blend);
    outN = mix(n0, n1, blend);
    outOrh = mix(orh0, orh1, blend);
}

void main()
{
    vec2 uvBase = vec2(ChooseUv(0).x, 1.0 - ChooseUv(0).y);
    vec2 uvX = XformUv(uvBase, UVScaleOffset, UVTransformMode);
    vec2 uv0 = WrapUvIfOutside01(uvX * max(Tiling, 0.0001));
    vec2 uv1 = WrapUvIfOutside01(uvX * max(Tiling1, 0.0001));
    vec2 uv2 = WrapUvIfOutside01(uvX * max(Tiling2, 0.0001));

    vec4 top1_c = EnableBaseColorMap ? texture(BaseColorMap, uv0) : vec4(1.0);
    if (EnableAlphaTest && (top1_c.a < AlphaTestThreshold))
    {
        discard;
    }

    vec3 top1_n = EnableNormalMap ? DecodeNm(texture(NormalMap, uv0).rgb) : vec3(0.0, 0.0, 1.0);
    vec3 top1_orh = vec3(1.0, 1.0, 0.0);
    if (EnablePackedMap)
    {
        top1_orh = texture(PackedMap, uv0).rgb;
    }
    else if (EnableRoughnessMap)
    {
        top1_orh.g = texture(RoughnessMap, uv0).r;
    }

    top1_n = normalize(vec3(top1_n.xy * NormalIntensity, mix(1.0, top1_n.z, clamp(NormalIntensity, 0.0, 1.0))));
    top1_orh.g *= RoughnessIntensity;
    float top1_m = EnableMetallicValue ? MetallicIntensity : Metallic;

    vec3 blended_c = top1_c.rgb;
    vec3 blended_n = top1_n;
    vec3 blended_orh = top1_orh;
    float blended_m = top1_m;

    if (EnableBaseColorMap2)
    {
        vec4 bottom_c = EnableBaseColorMap2 ? texture(BaseColorMap2, uv2) : vec4(1.0);
        vec3 bottom_n = (EnableNormalMap2 ? DecodeNm(texture(NormalMap2, uv2).rgb) : vec3(0.0, 0.0, 1.0));
        vec3 bottom_orh = vec3(1.0, 1.0, 0.0);
        if (EnablePackedMap2)
        {
            bottom_orh = texture(PackedMap2, uv2).rgb;
        }
        else if (EnableRoughnessMap2)
        {
            bottom_orh.g = texture(RoughnessMap2, uv2).r;
        }
        bottom_n = normalize(vec3(bottom_n.xy * NormalIntensity2, mix(1.0, bottom_n.z, clamp(NormalIntensity2, 0.0, 1.0))));
        bottom_orh.g *= RoughnessIntensity2;
        float bottom_m = EnableMetallicValue ? MetallicIntensity2 : Metallic;

        float top1_v = 0.0;
        if (EnableVertexSelectedChannel)
        {
            top1_v = PickChan(Color, VertexColorChannel);
        }
        else
        {
            top1_v = (VertexColorChannel == 0) ? Color.r : ((VertexColorChannel == 1) ? Color.g : Color.b);
        }
        float phase = clamp(pow(max(top1_v, 0.0), max(Contrast, 0.0001)), 0.0, 1.0);
        HeightLerp2(bottom_c.rgb, bottom_n, bottom_orh, top1_c.rgb, top1_n, top1_orh, phase, top1_orh.b, Influence, blended_c, blended_n, blended_orh);
        blended_m = mix(bottom_m, top1_m, phase);
    }

    if (EnableBaseColorMap1)
    {
        vec4 top2_c = texture(BaseColorMap1, uv1);
        vec3 top2_n = EnableNormalMap1 ? DecodeNm(texture(NormalMap1, uv1).rgb) : vec3(0.0, 0.0, 1.0);
        vec3 top2_orh = vec3(1.0, 1.0, 0.0);
        if (EnablePackedMap1)
        {
            top2_orh = texture(PackedMap1, uv1).rgb;
        }
        else if (EnableRoughnessMap1)
        {
            top2_orh.g = texture(RoughnessMap1, uv1).r;
        }

        top2_n = normalize(vec3(top2_n.xy * NormalIntensity1, mix(1.0, top2_n.z, clamp(NormalIntensity1, 0.0, 1.0))));
        top2_orh.g *= RoughnessIntensity1;
        float top2_m = EnableMetallicValue ? MetallicIntensity1 : Metallic;

        float top2_v = 0.0;
        if (EnableVertexSelectedChannel)
        {
            top2_v = PickChan(Color, VertexColorChannel);
        }
        else
        {
            int c = VertexColorChannel1;
            top2_v = (c == 0) ? Color.r : ((c == 1) ? Color.g : Color.b);
        }
        float phase2 = clamp(pow(max(top2_v, 0.0), max(Contrast1, 0.0001)), 0.0, 1.0);
        phase2 = clamp(phase2, 0.0, max(LayerMaskScale1, 0.0));
        HeightLerp2(blended_c, blended_n, blended_orh, top2_c.rgb, top2_n, top2_orh, phase2, top2_orh.b, Influence1, blended_c, blended_n, blended_orh);
        blended_m = mix(blended_m, top2_m, phase2);
    }

    if (EnableVertexBaseColor && EnableVertexColor)
    {
        blended_c *= mix(vec3(1.0), Color.rgb, clamp(VertexColorIntensity, 0.0, 1.0));
    }

    vec3 em = vec3(0.0);
    if (EnableEmissionColorMap)
    {
        em = texture(EmissionColorMap, uv0).rgb * EmissionIntensity;
    }

    vec3 nW = normalize(Normal);
    mat3 tbn = MakeFrame(nW, uvBase);
    vec3 n = normalize(tbn * normalize(blended_n));

    float refl = 1.0;
    float ao = clamp(blended_orh.r, 0.0, 1.0);
    float rough = clamp(blended_orh.g, 0.04, 1.0);
    float met = clamp(blended_m, 0.0, 1.0);

    gAlbedo = vec4(blended_c, rough);
    gNormal = vec4(n * 0.5 + 0.5, refl);
    gSpecular = vec4(ao, met, 0.0, 0.0);
    gAO = vec4(em, 0.0);
}
