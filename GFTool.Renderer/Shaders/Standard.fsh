#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D LayerMaskMap;
uniform sampler2D NormalMap;
uniform sampler2D NormalMap1;
uniform sampler2D NormalMap2;
uniform sampler2D RoughnessMap;
uniform sampler2D RoughnessMap1;
uniform sampler2D RoughnessMap2;
uniform sampler2D MetallicMap;
uniform sampler2D AOMap;
uniform sampler2D PackedMap;
uniform sampler2D EmissionColorMap;
uniform sampler2D DetailMaskMap;
uniform sampler2D SpecularMaskMap;
uniform sampler2D HighlightMaskMap;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffsetNormal;
uniform int UVTransformMode;
uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 BaseColorLayer2;
uniform vec4 BaseColorLayer3;
uniform vec4 BaseColorLayer4;

uniform bool EnableBaseColorMap;
uniform bool EnableAlphaTest;
uniform bool BaseColorMultiply;
uniform bool EnableLayerMaskMap;
uniform bool EnableNormalMap;
uniform bool EnableNormalMap1;
uniform bool EnableNormalMap2;
uniform bool EnableRoughnessMap;
uniform bool EnableRoughnessMap1;
uniform bool EnableRoughnessMap2;
uniform bool EnableMetallicMap;
uniform bool EnableAOMap;
uniform bool EnablePackedMap;
uniform bool EnableEmissionColorMap;
uniform bool EnableDetailMaskMap;
uniform bool EnableSpecularMaskMap;
uniform bool EnableHighlightMaskMap;
uniform int NumMaterialLayer;
uniform bool EnableVertexColor;
uniform bool TransparentPass;
uniform bool PremultiplyAlpha;
uniform bool LegacyMode;
uniform bool EnableLerpBaseColorEmission;
uniform float AlphaTestThreshold;
uniform float Metallic;
uniform float Roughness;
uniform vec4 EmissionColor;
uniform float EmissionIntensity;
uniform bool EnableFresnelEffect;
uniform vec4 FresnelColor;
uniform float FresnelIntensity;
uniform float FresnelPower;
uniform float FresnelAngleBias;
uniform float NormalHeight;
uniform float RoughnessIntensity;
uniform float MetallicIntensity;
uniform vec4 ScrollUVSpeed;
uniform vec4 time_params;
uniform bool EnableTeraEffect;
uniform vec3 TeraColor;
uniform float TeraStrength;

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

vec2 SelectUv(int index)
{
    if (index == 1)
    {
        return UV01.zw;
    }
    return UV01.xy;
}

vec2 ApplyUvTransform(vec2 uv, vec4 srt, int mode)
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

mat3 CotangentFrame(vec3 n, vec3 p, vec2 uv)
{
    vec3 dp1 = dFdx(p);
    vec3 dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);

    vec3 dp2perp = cross(dp2, n);
    vec3 dp1perp = cross(n, dp1);
    vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;

    float invmax = inversesqrt(max(dot(t, t), dot(b, b)));
    return mat3(t * invmax, b * invmax, n);
}

float WrapNdotL(float nDotL, float wrap)
{
    float w = max(wrap, 0.0);
    return clamp((nDotL + w) / (1.0 + w), 0.0, 1.0);
}

float D_GGX(float a2, float NdotH)
{
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (3.14159265 * denom * denom);
}

float G_SchlickGGX(float k, float NdotV)
{
    return NdotV / (NdotV * (1.0 - k) + k);
}

float G_Smith(float k, float NdotV, float NdotL)
{
    return G_SchlickGGX(k, NdotV) * G_SchlickGGX(k, NdotL);
}

vec3 F_Schlick(vec3 F0, float VdotH)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - VdotH, 0.0, 1.0), 5.0);
}

void main()
{
    vec2 baseUv = vec2(SelectUv(0).x, 1.0 - SelectUv(0).y);
    baseUv += ScrollUVSpeed.xy * time_params.x;
    vec2 uv = WrapUvIfOutside01(ApplyUvTransform(baseUv, UVScaleOffset, UVTransformMode));
    vec2 uvNormal = WrapUvIfOutside01(ApplyUvTransform(baseUv, UVScaleOffsetNormal, UVTransformMode));

    bool useLayerMask = EnableLayerMaskMap && (NumMaterialLayer > 0);
    vec4 layerMask = vec4(0.0);
    float baseLayerWeight = 1.0;
    if (useLayerMask)
    {
        vec2 layerBase = (UVIndexLayerMask == 1) ? vec2(SelectUv(1).x, 1.0 - SelectUv(1).y) : baseUv;
        vec2 uvLayer = WrapUvIfOutside01(ApplyUvTransform(layerBase, UVScaleOffset, UVTransformMode));
        layerMask = texture(LayerMaskMap, uvLayer);
        if (dot(BaseColorLayer2.rgb, BaseColorLayer2.rgb) < 0.000001) layerMask.g = 0.0;
        if (dot(BaseColorLayer3.rgb, BaseColorLayer3.rgb) < 0.000001) layerMask.b = 0.0;
        if (dot(BaseColorLayer4.rgb, BaseColorLayer4.rgb) < 0.000001) layerMask.a = 0.0;
        baseLayerWeight = clamp(1.0 - dot(vec4(1.0), layerMask), 0.0, 1.0);
    }

    if (LegacyMode)
    {
        vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uv) : vec4(1.0);
        if (EnableAlphaTest && EnableBaseColorMap && baseSample.a < AlphaTestThreshold)
        {
            discard;
        }
        float alpha = (EnableBaseColorMap ? baseSample.a : 1.0) * BaseColor.a;
        vec3 texColor = baseSample.rgb;
        vec3 baseTint = BaseColorMultiply ? BaseColor.rgb : vec3(1.0);
        vec3 baseLayerColor = texColor * baseTint;
        vec3 layerColor = baseLayerColor;
        if (useLayerMask)
        {
            vec3 layer1 = texColor * BaseColorLayer1.rgb;
            vec3 layer2 = texColor * BaseColorLayer2.rgb;
            vec3 layer3 = texColor * BaseColorLayer3.rgb;
            vec3 layer4 = texColor * BaseColorLayer4.rgb;
            layerColor = baseLayerColor * baseLayerWeight +
                         layer1 * layerMask.r +
                         layer2 * layerMask.g +
                         layer3 * layerMask.b +
                         layer4 * layerMask.a;
        }

    vec3 vertexColor = EnableVertexColor ? Color.rgb : vec3(1.0);
    vec3 albedo = layerColor * vertexColor;

    vec3 emissionColor = EmissionColor.rgb;
    if (EnableEmissionColorMap)
    {
        emissionColor = texture(EmissionColorMap, uv).rgb;
    }
    vec3 emission = emissionColor * EmissionIntensity;
    if (EnableLerpBaseColorEmission)
    {
        float t = clamp(EmissionIntensity, 0.0, 1.0);
        albedo = mix(albedo, emissionColor, t);
    }

    if (EnableFresnelEffect && FresnelIntensity > 0.0)
    {
        vec3 viewDir = normalize(CameraPos - FragPos);
        float nv = clamp(dot(normalize(Normal), viewDir), 0.00001, 1.0);
        float u = clamp(max(nv - FresnelAngleBias, 0.0), 0.0, 1.0);
        float rim = pow(1.0 - u, max(FresnelPower, 0.0001));
        emission += FresnelColor.rgb * (FresnelIntensity * rim);
    }

    if (TransparentPass)
    {
        vec3 colorOut = albedo + emission;
        if (PremultiplyAlpha)
        {
            colorOut *= alpha;
        }
        gAlbedo = vec4(colorOut, alpha);
        gNormal = vec4(0.0);
        gSpecular = vec4(0.0);
        gAO = vec4(0.0);
        return;
    }

    gAlbedo = vec4(albedo, 1.0);
    gNormal = vec4(normalize(Normal) * 0.5 + 0.5, 1.0);
    gSpecular = vec4(1.0, 0.0, 0.0, 0.0);
    gAO = vec4(emission, 1.0);
    return;
}

    vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uv) : vec4(1.0);
    if (EnableAlphaTest && EnableBaseColorMap && baseSample.a < AlphaTestThreshold)
    {
        discard;
    }
    float alpha = (EnableBaseColorMap ? baseSample.a : 1.0) * BaseColor.a;
    vec3 texColor = baseSample.rgb;
    vec3 baseTint = BaseColorMultiply ? BaseColor.rgb : vec3(1.0);
    vec3 baseLayerColor = texColor * baseTint;
    vec3 layerColor = baseLayerColor;
    if (useLayerMask)
    {
        vec3 layer1 = texColor * BaseColorLayer1.rgb;
        vec3 layer2 = texColor * BaseColorLayer2.rgb;
        vec3 layer3 = texColor * BaseColorLayer3.rgb;
        vec3 layer4 = texColor * BaseColorLayer4.rgb;
        layerColor = baseLayerColor * baseLayerWeight +
                     layer1 * layerMask.r +
                     layer2 * layerMask.g +
                     layer3 * layerMask.b +
                     layer4 * layerMask.a;
    }
    vec3 vertexColor = EnableVertexColor ? Color.rgb : vec3(1.0);
    vec3 albedo = layerColor * vertexColor;

    vec3 emissionColor = EmissionColor.rgb;
    if (EnableEmissionColorMap)
    {
        emissionColor = texture(EmissionColorMap, uv).rgb;
    }
    vec3 emission = emissionColor * EmissionIntensity;
    if (EnableLerpBaseColorEmission)
    {
        float t = clamp(EmissionIntensity, 0.0, 1.0);
        albedo = mix(albedo, emissionColor, t);
    }

    float detailMask = EnableDetailMaskMap ? texture(DetailMaskMap, uv).r : 0.0;
    albedo *= mix(1.0, 0.85, detailMask);

    float roughness = EnableRoughnessMap ? texture(RoughnessMap, uv).r : Roughness;
    if (EnableRoughnessMap1 && useLayerMask)
    {
        float rough1 = texture(RoughnessMap1, uv).r;
        roughness = mix(roughness, rough1, layerMask.g);
    }
    if (EnableRoughnessMap2 && useLayerMask)
    {
        float rough2 = texture(RoughnessMap2, uv).r;
        roughness = mix(roughness, rough2, layerMask.b);
    }
    roughness = clamp(roughness, 0.04, 1.0);

    float metallic = EnableMetallicMap ? texture(MetallicMap, uv).r : Metallic;
    float ao = 1.0;
    if (EnableAOMap)
    {
        vec2 aoBase = (UVIndexAO == 1) ? vec2(SelectUv(1).x, 1.0 - SelectUv(1).y) : baseUv;
        vec2 uvAo = WrapUvIfOutside01(ApplyUvTransform(aoBase, UVScaleOffset, UVTransformMode));
        ao = texture(AOMap, uvAo).r;
    }
    if (EnablePackedMap)
    {
        vec3 packedValue = texture(PackedMap, uv).rgb;
        ao = packedValue.r;
        roughness = packedValue.g * RoughnessIntensity;
        metallic = packedValue.b * MetallicIntensity;
    }

    vec3 n = normalize(Normal);
    vec3 tangentNormal = vec3(0.0, 0.0, 1.0);
    if (EnableNormalMap)
    {
        vec4 nm = texture(NormalMap, uvNormal);
        vec2 rg = (nm.rg * 2.0 - 1.0) * NormalHeight;
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
            tangentNormal.y = -tangentNormal.y;
    }
    if (EnableNormalMap1 && useLayerMask && HasTangents)
    {
        vec4 nm1 = texture(NormalMap1, uvNormal);
        vec2 rg1 = (nm1.rg * 2.0 - 1.0) * NormalHeight;
        vec3 n1;
        if (ReconstructNormalZ)
        {
            float nz1 = sqrt(max(0.0, 1.0 - dot(rg1, rg1)));
            n1 = vec3(rg1, nz1);
        }
        else
        {
            n1 = vec3(nm1.r, nm1.g, nm1.a) * 2.0 - 1.0;
        }
        if (FlipNormalY)
            n1.y = -n1.y;
        tangentNormal = normalize(mix(tangentNormal, n1, layerMask.g));
    }
    if (EnableNormalMap2 && useLayerMask && HasTangents)
    {
        vec4 nm2 = texture(NormalMap2, uvNormal);
        vec2 rg2 = (nm2.rg * 2.0 - 1.0) * NormalHeight;
        vec3 n2;
        if (ReconstructNormalZ)
        {
            float nz2 = sqrt(max(0.0, 1.0 - dot(rg2, rg2)));
            n2 = vec3(rg2, nz2);
        }
        else
        {
            n2 = vec3(nm2.r, nm2.g, nm2.a) * 2.0 - 1.0;
        }
        if (FlipNormalY)
            n2.y = -n2.y;
        tangentNormal = normalize(mix(tangentNormal, n2, layerMask.b));
    }

    if ((EnableNormalMap || EnableNormalMap1 || EnableNormalMap2))
    {
        mat3 tbn;
        if (HasTangents)
        {
            vec3 bitangent = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
            if (dot(bitangent, bitangent) < 0.0001)
            {
                bitangent = normalize(cross(n, normalize(Tangent)));
            }
            tbn = mat3(normalize(Tangent), bitangent, n);
        }
        else
        {
            tbn = CotangentFrame(n, FragPos, uvNormal);
        }
        n = normalize(tbn * tangentNormal);
    }

    float specMask = EnableSpecularMaskMap ? texture(SpecularMaskMap, uv).r : 1.0;
    float highlightMask = EnableHighlightMaskMap ? texture(HighlightMaskMap, uv).r : 0.0;
    float highlightBoost = mix(1.0, 2.0, highlightMask);
    float reflectance = clamp(specMask * highlightBoost, 0.0, 1.0);
    if (EnableLerpBaseColorEmission)
    {
        reflectance = mix(reflectance, 0.0, clamp(EmissionIntensity, 0.0, 1.0));
    }

    if (EnableFresnelEffect && FresnelIntensity > 0.0)
    {
        vec3 viewDir = normalize(CameraPos - FragPos);
        float nv = clamp(dot(n, viewDir), 0.00001, 1.0);
        float u = clamp(max(nv - FresnelAngleBias, 0.0), 0.0, 1.0);
        float rim = pow(1.0 - u, max(FresnelPower, 0.0001));
        emission += FresnelColor.rgb * (FresnelIntensity * rim);
    }

    if (TransparentPass)
    {
        if (!gl_FrontFacing)
        {
            n = -n;
        }

        vec3 worldPos = FragPos;
        vec3 viewDir = normalize(CameraPos - worldPos);
        vec3 lightDir = normalize(-LightDirection);
        vec3 halfDir = normalize(viewDir + lightDir);

        float NdotL0 = dot(n, lightDir);
        float NdotL = TwoSidedDiffuse ? abs(NdotL0) : max(NdotL0, 0.0);
        float wrappedNdotL = WrapNdotL(NdotL, LightWrap);

        float NdotV = clamp(abs(dot(n, viewDir)), 0.0001, 1.0);
        float NdotH = clamp(max(dot(n, halfDir), 0.0), 0.0, 1.0);
        float VdotH = clamp(dot(viewDir, halfDir), 0.0, 1.0);

        if (EnableTeraEffect)
        {
            vec3 tint = clamp(TeraColor, vec3(0.0), vec3(8.0));
            float rim = pow(clamp(1.0 - NdotV, 0.0, 1.0), 4.0);
            float cell = dot(floor(worldPos * 40.0), vec3(12.9898, 78.233, 37.719));
            float sparkle = step(0.985, fract(sin(cell) * 43758.5453));
            float sparkleAnim = 0.5 + 0.5 * sin(time_params.x * 12.0 + dot(worldPos, vec3(3.1, 4.2, 5.3)));
            sparkle *= sparkleAnim;
            emission += tint * (rim * 0.65 + sparkle * 1.4) * clamp(TeraStrength, 0.0, 4.0);
        }

        float a = clamp(roughness, 0.04, 1.0);
        float a2 = a * a;
        float k = (a + 1.0);
        k = (k * k) / 8.0;

        vec3 F0 = mix(vec3(0.04 * reflectance), albedo, clamp(metallic, 0.0, 1.0));
        vec3 F = F_Schlick(F0, VdotH);
        float D = D_GGX(a2, NdotH);
        float G = G_Smith(k, NdotV, NdotL);
        vec3 specularBRDF = (D * G) * F / max(4.0 * NdotV * NdotL, 0.0001);

        vec3 diffuse = albedo * (1.0 - clamp(metallic, 0.0, 1.0));
        vec3 lit = AmbientColor + LightColor * wrappedNdotL;
        vec3 diffuseLit = diffuse * lit;
        vec3 specularLit = specularBRDF * LightColor * NdotL * max(SpecularScale, 0.0);

        float aoSoft = mix(1.0, ao, 0.65);
        vec3 colorOut = (diffuseLit + specularLit) * aoSoft + emission;
        if (PremultiplyAlpha)
        {
            colorOut *= alpha;
        }

        gAlbedo = vec4(colorOut, alpha);
        gNormal = vec4(0.0);
        gSpecular = vec4(0.0);
        gAO = vec4(0.0);
        return;
    }

    if (EnableTeraEffect)
    {
        vec3 tint = clamp(TeraColor, vec3(0.0), vec3(8.0));
        vec3 viewDir = normalize(CameraPos - FragPos);
        float nv = clamp(abs(dot(n, viewDir)), 0.0, 1.0);
        float rim = pow(clamp(1.0 - nv, 0.0, 1.0), 4.0);
        float cell = dot(floor(FragPos * 40.0), vec3(12.9898, 78.233, 37.719));
        float sparkle = step(0.985, fract(sin(cell) * 43758.5453));
        float sparkleAnim = 0.5 + 0.5 * sin(time_params.x * 12.0 + dot(FragPos, vec3(3.1, 4.2, 5.3)));
        sparkle *= sparkleAnim;

        float strength = clamp(TeraStrength, 0.0, 4.0);
        emission += tint * (rim * 0.55 + sparkle * 1.2) * strength;
        albedo = mix(albedo, albedo * mix(vec3(1.0), tint, 0.65), 0.35 * strength);
        roughness = mix(roughness, 0.08, 0.6 * strength);
        metallic = mix(metallic, 0.2, 0.4 * strength);
    }

    gAlbedo = vec4(albedo, roughness);
    gNormal = vec4(n * 0.5 + 0.5, reflectance);
    gSpecular = vec4(ao, metallic, 0.0, 0.0);
    gAO = vec4(emission, 0.0);
}
