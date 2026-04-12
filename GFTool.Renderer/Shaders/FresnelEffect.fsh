#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D NormalMap;
uniform sampler2D BaseColorMap1;
uniform sampler2D NormalMap1;
uniform sampler2D AOMap;
uniform sampler2D EmissionColorMap;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffset1;
uniform int UVTransformMode;

uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 EmissionColor;

uniform float Metallic;
uniform float Roughness;
uniform float NormalHeight;
uniform float NormalHeight1;
uniform float BaseColorMapSaturation;
uniform vec4 ParallaxUVIntensity;

uniform float EmissionIntensity;
uniform float EmissionIntensityLayer1;

uniform float FresnelAlphaMin;
uniform float FresnelAlphaMax;
uniform float FresnelAngleBias;
uniform float LayerMaskScale1;

uniform bool EnableBaseColorMap;
uniform bool EnableAOMap;
uniform bool EnableNormalMap;
uniform bool EnableEmissionColorMap;
uniform bool EnableFresnelTexture;

uniform int UVIndexAO;
uniform int UVIndexLayer1;

uniform vec3 CameraPos;
uniform bool HasTangents;
uniform bool HasBinormals;
uniform bool FlipNormalY;
uniform bool ReconstructNormalZ;

layout (location = 0) out vec4 gAlbedo;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out vec4 gSpecular;
layout (location = 3) out vec4 gAO;

in vec3 FragPos;
in vec3 Normal;
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

vec3 DecodeNormalSample(vec4 nm, float height)
{
    vec2 xy = nm.rg * 2.0 - 1.0;
    if (FlipNormalY)
    {
        xy.y = -xy.y;
    }
    xy *= max(height, 0.0);
    float z;
    if (ReconstructNormalZ)
    {
        z = sqrt(max(1.0 - dot(xy, xy), 0.0));
    }
    else
    {
        z = nm.a * 2.0 - 1.0;
    }
    return normalize(vec3(xy, z));
}

vec3 BlendNormalReoriented(vec3 baseN, vec3 detailN)
{
    vec3 t = baseN + vec3(0.0, 0.0, 1.0);
    vec3 u = detailN * vec3(-1.0, -1.0, 1.0);
    return (t / max(t.z, 0.00001)) * dot(t, u) - u;
}

float FresnelSchlickScalar(float f0, float f90, float u)
{
    return f0 + (f90 - f0) * pow(1.0 - u, 5.0);
}

void main()
{
    vec2 baseUv0 = vec2(SelectUv(0).x, 1.0 - SelectUv(0).y);
    vec2 primaryUv = ApplyUvTransform(baseUv0, UVScaleOffset, UVTransformMode);

    vec3 viewVec = normalize(CameraPos - FragPos);
    vec3 n0 = normalize(Normal);

    vec3 t = normalize(Tangent);
    vec3 b = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
    if (dot(b, b) < 0.0001)
    {
        b = normalize(cross(n0, t));
    }
    mat3 tbn = mat3(t, b, n0);

    vec3 viewTs = transpose(tbn) * viewVec;
    float parallax = 0.5;
    vec2 parallaxOffset = (viewTs.xy / max(viewTs.z, 0.00001)) * parallax;
    parallaxOffset *= vec2(1.0, -1.0);

    vec4 baseColor = BaseColor;
    if (EnableBaseColorMap)
    {
        vec2 uvp = primaryUv + parallaxOffset * ParallaxUVIntensity.xy;
        baseColor *= texture(BaseColorMap, uvp);
        float l = dot(baseColor.rgb, vec3(0.299, 0.587, 0.114));
        baseColor.rgb = mix(vec3(l), baseColor.rgb, clamp(BaseColorMapSaturation, 0.0, 1.0));
    }

    float ao = 1.0;
    if (EnableAOMap)
    {
        vec2 aoUv = (UVIndexAO < 0) ? primaryUv : vec2(SelectUv(UVIndexAO).x, 1.0 - SelectUv(UVIndexAO).y);
        ao = texture(AOMap, aoUv).r;
    }

    vec3 n = n0;
    if (EnableNormalMap && HasTangents)
    {
        vec2 normalUv = (UVIndexLayer1 < 0) ? primaryUv : vec2(SelectUv(UVIndexLayer1).x, 1.0 - SelectUv(UVIndexLayer1).y);
        vec3 normalTs = DecodeNormalSample(texture(NormalMap, normalUv), NormalHeight);
        vec3 normalTsLayer1 = DecodeNormalSample(texture(NormalMap1, primaryUv), NormalHeight1);
        vec3 mixedTs = mix(normalTs, BlendNormalReoriented(normalTsLayer1, normalTs), clamp(NormalHeight, 0.0, 4.0));
        n = normalize(tbn * mixedTs);
    }

    n *= (gl_FrontFacing ? 1.0 : -1.0);

    vec3 emission;
    if (EnableEmissionColorMap)
    {
        emission = texture(EmissionColorMap, primaryUv).rgb * EmissionIntensity;
    }
    else
    {
        emission = EmissionColor.rgb * EmissionIntensity;
    }

    float nv = clamp(dot(n0, viewVec), 0.00001, 1.0);
    if (EnableFresnelTexture)
    {
        float fresnelAlpha = FresnelSchlickScalar(FresnelAlphaMin, FresnelAlphaMax, max(nv - FresnelAngleBias, 0.0));
        vec2 layerUv = ApplyUvTransform(primaryUv + parallaxOffset * ParallaxUVIntensity.xy, UVScaleOffset1, UVTransformMode);
        vec3 fresnelColor = texture(BaseColorMap1, layerUv).rgb;
        fresnelColor *= ao * BaseColorLayer1.rgb * EmissionIntensityLayer1;
        fresnelColor = mix(emission, fresnelColor, clamp(LayerMaskScale1, 0.0, 1.0));
        emission = mix(fresnelColor, emission, fresnelAlpha);
    }

    float rough = clamp(Roughness, 0.04, 1.0);
    float metallic = clamp(Metallic, 0.0, 1.0);

    gAlbedo = vec4(baseColor.rgb, rough);
    gNormal = vec4(n * 0.5 + 0.5, 1.0);
    gSpecular = vec4(ao, metallic, 0.0, 0.0);
    gAO = vec4(emission, 0.0);
}
