#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D LayerMaskMap;
uniform sampler2D NormalMap;
uniform sampler2D RoughnessMap;
uniform sampler2D AOMap;
uniform sampler2D MetallicMap;
uniform sampler2D EmissionColorMap;

uniform bool EnableBaseColorMap;
uniform bool EnableLayerMaskMap;
uniform bool EnableNormalMap;
uniform bool EnableRoughnessMap;
uniform bool EnableAOMap;
uniform int NumMaterialLayer;
uniform bool EnableSSSMaskMap;
uniform bool EnableMetallicMap;
uniform bool EnableEmissionColorMap;
uniform bool EnableVertexColor;
uniform bool TransparentPass;
uniform bool PremultiplyAlpha;
uniform bool EnableAlphaTest;
uniform float AlphaTestThreshold;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffsetNormal;
uniform int UVTransformMode;
uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 BaseColorLayer2;
uniform vec4 BaseColorLayer3;
uniform vec4 BaseColorLayer4;
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
uniform float NormalHeight;
uniform float Metallic;
uniform float MetallicLayer1;
uniform float MetallicLayer2;
uniform float MetallicLayer3;
uniform float MetallicLayer4;
uniform float Roughness;
uniform float RoughnessLayer1;
uniform float RoughnessLayer2;
uniform float RoughnessLayer3;
uniform float RoughnessLayer4;
uniform float Reflectance;
uniform float LayerMaskScale1;
uniform float LayerMaskScale2;
uniform float LayerMaskScale3;
uniform float LayerMaskScale4;
uniform bool BaseColorMultiply;

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

layout (location = 0) out vec4 gAlbedo;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out vec4 gSpecular;
layout (location = 3) out vec4 gAO;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;
in vec4 Color;
in vec3 Tangent;
in vec3 Bitangent;
in vec3 Binormal;

vec2 ApplyUvTransform(vec2 uv, vec4 srt, int mode)
{
    if (mode == 1)
    {
        return uv + srt.zw;
    }
    return uv * srt.xy + srt.zw;
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
    vec2 uv = vec2(TexCoord.x, 1.0 - TexCoord.y);
    vec2 uvBase = ApplyUvTransform(uv, UVScaleOffset, UVTransformMode);
    vec2 uvNormal = ApplyUvTransform(uv, UVScaleOffsetNormal, UVTransformMode);
    bool useLayerMask = EnableLayerMaskMap && (NumMaterialLayer > 0);
    vec4 layerMask = vec4(0.0);
    float baseLayerWeight = 1.0;
    if (useLayerMask)
    {
        layerMask = texture(LayerMaskMap, uvBase);
        layerMask *= vec4(LayerMaskScale1, LayerMaskScale2, LayerMaskScale3, LayerMaskScale4);
        float layerSum = clamp(dot(vec4(1.0), layerMask), 0.0, 1.0);
        baseLayerWeight = clamp(1.0 - layerSum, 0.0, 1.0);
    }

    vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uvBase) : vec4(1.0);
    if (EnableAlphaTest && EnableBaseColorMap && baseSample.a < AlphaTestThreshold)
    {
        discard;
    }
    vec3 baseSampleRgb = baseSample.rgb;
    vec3 baseColor = BaseColor.rgb * baseSampleRgb;
    if (!EnableEmissionColorMap)
    {
        baseColor *= max(1.0 - EmissionIntensity, 0.0);
    }

    if (useLayerMask)
    {
        vec3 layer1 = baseSampleRgb * BaseColorLayer1.rgb;
        vec3 layer2 = baseSampleRgb * BaseColorLayer2.rgb;
        vec3 layer3 = baseSampleRgb * BaseColorLayer3.rgb;
        vec3 layer4 = baseSampleRgb * BaseColorLayer4.rgb;
        if (!EnableEmissionColorMap)
        {
            layer1 *= max(1.0 - EmissionIntensityLayer1, 0.0);
            layer2 *= max(1.0 - EmissionIntensityLayer2, 0.0);
            layer3 *= max(1.0 - EmissionIntensityLayer3, 0.0);
            layer4 *= max(1.0 - EmissionIntensityLayer4, 0.0);
        }

        baseColor *= baseLayerWeight;
        baseColor = mix(baseColor, layer1, layerMask.r);
        baseColor = mix(baseColor, layer2, layerMask.g);
        baseColor = mix(baseColor, layer3, layerMask.b);
        baseColor = mix(baseColor, layer4, layerMask.a);
    }
    vec3 vertexColor = EnableVertexColor ? Color.rgb : vec3(1.0);
    vec3 albedo = baseColor * vertexColor;

    float roughness = EnableRoughnessMap ? texture(RoughnessMap, uvBase).r : Roughness;
    roughness = clamp(roughness, 0.04, 1.0);
    float metallic = EnableMetallicMap ? texture(MetallicMap, uvBase).r : Metallic;
    float ao = EnableAOMap ? texture(AOMap, uvBase).r : 1.0;

    vec3 emissionColor = EmissionColor.rgb;
    if (EnableEmissionColorMap)
    {
        emissionColor = texture(EmissionColorMap, uvBase).rgb;
    }
    vec3 emission = emissionColor * EmissionIntensity;
    if (useLayerMask && !EnableEmissionColorMap)
    {
        emission *= baseLayerWeight;
        emission = mix(emission, EmissionColorLayer1.rgb * EmissionIntensityLayer1, layerMask.r);
        emission = mix(emission, EmissionColorLayer2.rgb * EmissionIntensityLayer2, layerMask.g);
        emission = mix(emission, EmissionColorLayer3.rgb * EmissionIntensityLayer3, layerMask.b);
        emission = mix(emission, EmissionColorLayer4.rgb * EmissionIntensityLayer4, layerMask.a);
    }

    if (useLayerMask && !EnableMetallicMap)
    {
        metallic *= baseLayerWeight;
        metallic = mix(metallic, MetallicLayer1, layerMask.r);
        metallic = mix(metallic, MetallicLayer2, layerMask.g);
        metallic = mix(metallic, MetallicLayer3, layerMask.b);
        metallic = mix(metallic, MetallicLayer4, layerMask.a);
    }

    if (useLayerMask && !EnableRoughnessMap)
    {
        roughness *= baseLayerWeight;
        roughness = mix(roughness, RoughnessLayer1, layerMask.r);
        roughness = mix(roughness, RoughnessLayer2, layerMask.g);
        roughness = mix(roughness, RoughnessLayer3, layerMask.b);
        roughness = mix(roughness, RoughnessLayer4, layerMask.a);
        roughness = clamp(roughness, 0.04, 1.0);
    }

    vec3 n = normalize(Normal);
    if (EnableNormalMap && HasTangents)
    {
        vec4 nm = texture(NormalMap, uvNormal);
        vec2 rg = nm.rg * 2.0 - 1.0;
        rg *= max(NormalHeight, 0.0);
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
            tangentNormal.y = -tangentNormal.y;
        vec3 bitangent = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
        if (dot(bitangent, bitangent) < 0.0001)
        {
            bitangent = normalize(cross(n, normalize(Tangent)));
        }
        mat3 tbn = mat3(normalize(Tangent), bitangent, n);
        n = normalize(tbn * tangentNormal);
    }

    float alpha = EnableBaseColorMap ? baseSample.a : 1.0;
    alpha *= BaseColor.a;

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

        float a = clamp(roughness, 0.04, 1.0);
        float a2 = a * a;
        float k = (a + 1.0);
        k = (k * k) / 8.0;

        float refl = clamp(Reflectance, 0.0, 1.0);
        vec3 F0 = mix(vec3(0.04 * refl), albedo, clamp(metallic, 0.0, 1.0));
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

    // Deferred attributes (rarely used for transparent materials)
    gAlbedo = vec4(albedo, roughness);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);
    gSpecular = vec4(ao, metallic, 0.0, 0.0);
    gAO = vec4(emission, 0.0); // emission, shadingModel=PBR
}
