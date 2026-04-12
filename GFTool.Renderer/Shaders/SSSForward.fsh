#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D LayerMaskMap;
uniform sampler2D NormalMap;
uniform sampler2D RoughnessMap;
uniform sampler2D AOMap;
uniform sampler2D SSSMaskMap;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffsetNormal;

uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 BaseColorLayer2;
uniform vec4 BaseColorLayer3;
uniform vec4 BaseColorLayer4;

uniform vec4 SubsurfaceColor;
uniform vec4 SubsurfaceColorLayer1;
uniform vec4 SubsurfaceColorLayer2;
uniform vec4 SubsurfaceColorLayer3;
uniform vec4 SubsurfaceColorLayer4;

uniform vec4 EmissionColor;
uniform vec4 EmissionColorLayer1;
uniform vec4 EmissionColorLayer2;
uniform vec4 EmissionColorLayer3;
uniform vec4 EmissionColorLayer4;

uniform float EmissionIntensity;
uniform float EmissionIntensityLayer1;
uniform float EmissionIntensityLayer2;
uniform float EmissionIntensityLayer3;
uniform float EmissionIntensityLayer4;

uniform float LayerMaskScale1;
uniform float LayerMaskScale2;
uniform float LayerMaskScale3;
uniform float LayerMaskScale4;

uniform float Reflectance;
uniform float Roughness;

uniform float SSSScatterPower;
uniform float SSSEmission;
uniform float SSSMaskStrength;
uniform float SSSMaskScale;
uniform float SSSMaskOffset;

uniform bool EnableBaseColorMap;
uniform bool EnableLayerMaskMap;
uniform bool EnableNormalMap;
uniform bool EnableRoughnessMap;
uniform bool EnableAOMap;
uniform int NumMaterialLayer;
uniform bool EnableSSSMaskMap;
uniform bool EnableVertexColor;
uniform bool PremultiplyAlpha;

uniform vec3 LightDirection;
uniform vec3 LightColor;
uniform vec3 AmbientColor;
uniform vec3 CameraPos;
uniform bool HasTangents;
uniform bool HasBinormals;
uniform bool FlipNormalY;
uniform bool ReconstructNormalZ;
uniform bool TwoSidedDiffuse;
uniform float LightWrap;
uniform float SpecularScale;
uniform int UVIndexLayerMask;
uniform int UVIndexAO;
uniform int UVTransformMode;

out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec4 UV01;
in vec4 Color;
in vec3 Tangent;
in vec3 Bitangent;
in vec3 Binormal;

vec2 SelectUv(int index)
{
    return (index == 1) ? UV01.zw : UV01.xy;
}

vec2 ApplyUvTransform(vec2 uv, vec4 srt, int mode)
{
    if (mode == 1)
    {
        return uv + srt.zw;
    }
    return uv * srt.xy + srt.zw;
}

vec3 MixLayeredColor(vec3 baseRgb, vec3 l1, vec3 l2, vec3 l3, vec3 l4, vec4 mask, float baseWeight)
{
    vec3 c = baseRgb * baseWeight;
    c = mix(c, l1, mask.r);
    c = mix(c, l2, mask.g);
    c = mix(c, l3, mask.b);
    c = mix(c, l4, mask.a);
    return c;
}

void main()
{
    vec2 baseUv = vec2(SelectUv(0).x, 1.0 - SelectUv(0).y);
    vec2 uv = ApplyUvTransform(baseUv, UVScaleOffset, UVTransformMode);
    vec2 uvNormal = ApplyUvTransform(baseUv, UVScaleOffsetNormal, UVTransformMode);

    vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uv) : vec4(1.0);
    vec3 baseSampleRgb = baseSample.rgb;

    bool useLayerMask = EnableLayerMaskMap && (NumMaterialLayer > 0);
    vec4 layerMask = vec4(0.0);
    if (useLayerMask)
    {
        vec2 layerBase = (UVIndexLayerMask == 1) ? vec2(SelectUv(1).x, 1.0 - SelectUv(1).y) : baseUv;
        vec2 uvLayer = ApplyUvTransform(layerBase, UVScaleOffset, UVTransformMode);
        layerMask = texture(LayerMaskMap, uvLayer);
        layerMask *= vec4(LayerMaskScale1, LayerMaskScale2, LayerMaskScale3, LayerMaskScale4);
    }

    float baseWeight = 1.0;
    if (useLayerMask)
    {
        baseWeight = clamp(1.0 - dot(vec4(1.0), layerMask), 0.0, 1.0);
    }

    vec3 vertexColor = EnableVertexColor ? Color.rgb : vec3(1.0);

    vec3 baseRgb = BaseColor.rgb * baseSampleRgb;
    vec3 l1 = BaseColorLayer1.rgb * baseSampleRgb;
    vec3 l2 = BaseColorLayer2.rgb * baseSampleRgb;
    vec3 l3 = BaseColorLayer3.rgb * baseSampleRgb;
    vec3 l4 = BaseColorLayer4.rgb * baseSampleRgb;
    vec3 albedo = MixLayeredColor(baseRgb, l1, l2, l3, l4, layerMask, baseWeight) * vertexColor;

    vec3 sssBase = SubsurfaceColor.rgb;
    vec3 sss1 = SubsurfaceColorLayer1.rgb;
    vec3 sss2 = SubsurfaceColorLayer2.rgb;
    vec3 sss3 = SubsurfaceColorLayer3.rgb;
    vec3 sss4 = SubsurfaceColorLayer4.rgb;
    vec3 subsurfaceTint = MixLayeredColor(sssBase, sss1, sss2, sss3, sss4, layerMask, baseWeight);
    subsurfaceTint = clamp(subsurfaceTint, 0.0, 8.0);

    vec3 emissionBase = EmissionColor.rgb * EmissionIntensity;
    vec3 e1 = EmissionColorLayer1.rgb * EmissionIntensityLayer1;
    vec3 e2 = EmissionColorLayer2.rgb * EmissionIntensityLayer2;
    vec3 e3 = EmissionColorLayer3.rgb * EmissionIntensityLayer3;
    vec3 e4 = EmissionColorLayer4.rgb * EmissionIntensityLayer4;
    vec3 emission = MixLayeredColor(emissionBase, e1, e2, e3, e4, layerMask, baseWeight);

    float roughness = EnableRoughnessMap ? texture(RoughnessMap, uv).r : Roughness;
    roughness = clamp(roughness, 0.04, 1.0);

    float ao = 1.0;
    if (EnableAOMap)
    {
        vec2 aoBase = (UVIndexAO == 1) ? vec2(SelectUv(1).x, 1.0 - SelectUv(1).y) : baseUv;
        vec2 uvAo = ApplyUvTransform(aoBase, UVScaleOffset, UVTransformMode);
        ao = texture(AOMap, uvAo).r;
    }

    float sssMask = EnableSSSMaskMap ? texture(SSSMaskMap, uv).r : 0.0;
    sssMask = sssMask * SSSMaskScale + SSSMaskOffset;
    sssMask = clamp(sssMask * SSSMaskStrength, 0.0, 1.0);

    vec3 n = normalize(Normal);
    if (EnableNormalMap && HasTangents)
    {
        vec4 nm = texture(NormalMap, uvNormal);
        vec2 rg = nm.rg * 2.0 - 1.0;
        vec3 tangentNormal;
        if (ReconstructNormalZ)
        {
            float nz = sqrt(max(0.0, 1.0 - dot(rg, rg)));
            tangentNormal = vec3(rg, nz);
        }
        else
        {
            tangentNormal = vec3(nm.r, nm.g, nm.a) * 2.0 - 1.0;
        }
        if (FlipNormalY)
        {
            tangentNormal.y = -tangentNormal.y;
        }
        vec3 bitangent = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
        if (dot(bitangent, bitangent) < 0.0001)
        {
            bitangent = normalize(cross(n, normalize(Tangent)));
        }
        mat3 tbn = mat3(normalize(Tangent), bitangent, n);
        n = normalize(tbn * tangentNormal);
    }

    vec3 lightDir = normalize(-LightDirection);
    vec3 viewDir = normalize(CameraPos - FragPos);
    vec3 halfDir = normalize(lightDir + viewDir);

    float nDotL = dot(n, lightDir);
    if (TwoSidedDiffuse)
        nDotL = abs(nDotL);
    else
        nDotL = max(nDotL, 0.0);
    float wrappedNdotL = (nDotL + LightWrap) / (1.0 + LightWrap);

    float specPower = mix(16.0, 96.0, 1.0 - roughness);
    float spec = pow(max(dot(n, halfDir), 0.0), specPower) * SpecularScale;
    vec3 specColor = vec3(max(Reflectance, 0.0));

    vec3 color = AmbientColor * albedo + LightColor * wrappedNdotL * albedo;
    color += LightColor * spec * specColor * wrappedNdotL;

    float nl01 = clamp(nDotL, 0.0, 1.0);
    float scatterPower = max(SSSScatterPower, 0.0001);
    float scatter = pow(1.0 - nl01, scatterPower);
    vec3 sss = LightColor * scatter * (sssMask * SSSEmission) * (albedo * subsurfaceTint);
    color += sss;
    color += emission;

    float alpha = clamp(baseSample.a, 0.0, 1.0);
    vec3 outRgb = color;
    if (PremultiplyAlpha)
    {
        outRgb *= alpha;
    }
    FragColor = vec4(outRgb, alpha);
}
