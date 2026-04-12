#version 420 core

uniform sampler2D BaseColorMap;
uniform sampler2D LayerMaskMap;
uniform sampler2D NormalMap;
uniform sampler2D RoughnessMap;
uniform sampler2D MetallicMap;
uniform sampler2D AOMap;
uniform sampler2D DetailMaskMap;
uniform sampler2D SpecularMaskMap;
uniform sampler2D HighlightMaskMap;
uniform sampler2D DiscardMaskMap;
uniform sampler2D DisplacementMap;

uniform sampler2D ShadowingColorMap;
uniform sampler2D ShadowingColorMaskMap;
uniform sampler2D RimLightMaskMap;
uniform sampler2D ParallaxMap;
uniform sampler2D EyelidShadowMaskMap;

uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffsetNormal;
uniform vec4 UVScaleOffset3;
uniform vec4 UVScaleOffsetLayerMask;
uniform vec4 UVCenterRotationLayerMask;
uniform int UVTransformMode;
uniform vec4 UVCenter0;
uniform float UVRotation;
uniform float UVRotationNormal;
uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 BaseColorLayer2;
uniform vec4 BaseColorLayer3;
uniform vec4 BaseColorLayer4;
uniform vec4 ShadowingColor;
uniform vec4 ShadowingColorLayer1;
uniform vec4 ShadowingColorLayer2;
uniform vec4 ShadowingColorLayer3;
uniform vec4 ShadowingColorLayer4;
uniform vec4 EmissionColorLayer5;
uniform float EmissionIntensityLayer5;

uniform float LayerMaskScale1;
uniform float LayerMaskScale2;
uniform float LayerMaskScale3;
uniform float LayerMaskScale4;

uniform bool EnableBaseColorMap;
uniform bool EnableAlphaTest;
uniform bool BaseColorMultiply;
uniform bool EnableLayerMaskMap;
uniform bool EnableNormalMap;
uniform bool EnableRoughnessMap;
uniform bool EnableMetallicMap;
uniform bool EnableAOMap;
uniform bool EnableDetailMaskMap;
uniform bool EnableSpecularMaskMap;
uniform bool EnableHighlightMaskMap;
uniform bool EnableDiscardMaskMap;
uniform bool EnableShadowingColorMap;
uniform bool EnableShadowingColorMaskMap;
uniform bool EnableRimLightMaskMap;

uniform bool EnableEyeOptions;
uniform bool EnableHighlight;
uniform bool EnableParallaxMap;
uniform bool RequireEyelidShadowMap;
uniform bool EnableUVScaleOffsetNormal;
uniform bool EnableDisplacementMap;

uniform int NumMaterialLayer;
uniform bool EnableVertexColor;
uniform bool TransparentPass;
uniform bool PremultiplyAlpha;
uniform bool LegacyMode;
uniform bool EnableHairSpecular;

uniform vec3 LightDirection;
uniform vec3 LightColor;
uniform vec3 AmbientColor;
uniform vec3 CameraPos;
uniform vec4 time_params;
uniform bool EnableTeraEffect;
uniform vec3 TeraColor;
uniform float TeraStrength;
uniform bool HasTangents;
uniform bool HasBinormals;
uniform bool HasUv1;
uniform bool FlipNormalY;
uniform bool ReconstructNormalZ;
uniform bool TwoSidedDiffuse;
uniform float LightWrap;
uniform float SpecularScale;
uniform float SpecularIntensity;
uniform float SpecularOffset;
uniform float SpecularContrast;
uniform float SpecularLayer1Offset;
uniform float SpecularLayer1Contrast;
uniform float SpecularLayer1Intensity;
uniform float SpecularLayer2Offset;
uniform float SpecularLayer2Contrast;
uniform float SpecularLayer2Intensity;
uniform float SpecularLayer3Offset;
uniform float SpecularLayer3Contrast;
uniform float SpecularLayer3Intensity;
uniform float SpecularLayer4Offset;
uniform float SpecularLayer4Contrast;
uniform float SpecularLayer4Intensity;
uniform float OcclusionStrength;
uniform float AlphaTestThreshold;
uniform float DiscardValue;
uniform float ParallaxHeight;
uniform float DisplacementHeight;
uniform float HalfLambertBias;
uniform float ShadowingShift;
uniform float ShadowingContrast;
uniform float ShadowStrength;
uniform float ShadowingGIGain;
uniform float RimLightOffset;
uniform float RimLightContrast;
uniform float RimLightIntensity;
uniform float BackRimLightIntensity;
uniform bool EnableAuraEffect;
uniform float AuraIntensity;
uniform float AuraRimPower;
uniform bool HasAuraTextures;
uniform bool IsAuraShell;
uniform float ShadowingBias;
uniform float ShadingBias;
uniform float MidAreaShift;
uniform float MidAreaContrast;
uniform float MidAreaHueOffset;
uniform float DarkAreaShift;
uniform float DarkAreaContrast;
uniform float DarkAreaHueOffset;
uniform float HueShiftAreaValue;
uniform float HueShiftBias;

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

uniform int UVIndexLayerMask;
uniform int UVIndexAO;
uniform int UVIndexLayer3;
uniform bool HasUVIndexLayerMask;
uniform bool HasUVIndexAO;

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

vec2 ApplyUvTransformPivot(vec2 uv, vec4 srt, float rotation, int mode, vec2 pivot)
{
    if (mode == 1)
    {
        return uv + srt.zw;
    }

    float c = cos(rotation);
    float s = sin(rotation);
    mat2 r = mat2(c, -s, s, c);
    vec2 local = (uv - pivot) * srt.xy;
    vec2 rotated = r * local;
    return rotated + pivot + srt.zw;
}

vec2 FlipV(vec2 uv)
{
    return vec2(uv.x, 1.0 - uv.y);
}

bool HasLayerMaskUvTransform()
{
    return any(notEqual(UVScaleOffsetLayerMask, vec4(1.0, 1.0, 0.0, 0.0))) ||
           any(notEqual(UVCenterRotationLayerMask, vec4(0.0)));
}

vec2 TransformUvFd(vec2 uv, vec4 scaleOffset, vec2 center)
{
    vec2 stuv = scaleOffset.xy * (uv - center - scaleOffset.zw) + center;
    return FlipV(stuv);
}

vec2 TransformUvFd(vec2 uv, vec4 scaleOffset, float rotationRad, vec2 center)
{
    float s = sin(rotationRad);
    float c = cos(rotationRad);
    mat2 rotationMat = mat2(c, s, -s, c);
    vec2 srtuv = scaleOffset.xy * ((uv - center) * rotationMat - scaleOffset.zw) + center;
    return FlipV(srtuv);
}

vec2 ApplyUvTransformFd(vec2 uv, vec4 scaleOffset, float rotationRad, int mode, vec2 center)
{
    if (mode == 1)
    {
        return FlipV(uv - scaleOffset.zw);
    }

    if (abs(rotationRad) > 0.000001)
    {
        return TransformUvFd(uv, scaleOffset, rotationRad, center);
    }
    return TransformUvFd(uv, scaleOffset, center);
}

vec2 WrapUvIfOutside01(vec2 uv)
{
    return uv;
}

float SGCheapContrast(float inputValue, float contrast)
{
    return clamp(mix(0.0 - contrast, contrast + 1.0, inputValue), 0.0, 1.0);
}

float SGSpecularParam(float specularOffset, float phongSpecular, float specularContrast, float specularIntensity)
{
    float specular = smoothstep(0.0 + specularOffset, 1.0 + specularOffset, phongSpecular);
    specular = SGCheapContrast(specular, specularContrast);
    return specular * specularIntensity;
}

float Remap(float inputValue, vec2 inMinMax, vec2 outMinMax)
{
    return outMinMax.x + (inputValue - inMinMax.x) * (outMinMax.y - outMinMax.x) / (inMinMax.y - inMinMax.x);
}

vec3 HueDegrees(vec3 inputColor, float offset)
{
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 P = mix(vec4(inputColor.bg, K.wz), vec4(inputColor.gb, K.xy), step(inputColor.b, inputColor.g));
    vec4 Q = mix(vec4(P.xyw, inputColor.r), vec4(inputColor.r, P.yzx), step(P.x, inputColor.r));
    float D = Q.x - min(Q.w, Q.y);
    float E = 1e-10;
    float V = (D == 0.0) ? Q.x : (Q.x + E);
    vec3 hsv = vec3(abs(Q.z + (Q.w - Q.y) / (6.0 * D + E)), D / (Q.x + E), V);

    float hue = hsv.x + offset / 360.0;
    hsv.x = (hue < 0.0) ? hue + 1.0 : ((hue > 1.0) ? hue - 1.0 : hue);

    vec4 K2 = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 P2 = abs(fract(hsv.xxx + K2.xyz) * 6.0 - K2.www);
    return hsv.z * mix(K2.xxx, clamp(P2 - K2.xxx, 0.0, 1.0), hsv.y);
}

void main()
{
    bool isEye = EnableEyeOptions || RequireEyelidShadowMap;
    vec2 rawUv0 = UV01.xy;
    vec2 rawUv1 = UV01.zw;
    vec2 rawUv1Safe = HasUv1 ? rawUv1 : rawUv0;

    bool useEyeCenter = EnableParallaxMap || isEye;
    vec2 uv = WrapUvIfOutside01(ApplyUvTransformFd(rawUv0, UVScaleOffset, UVRotation, UVTransformMode, useEyeCenter ? UVCenter0.xy : vec2(0.0)));

    vec2 uvNormal = uv;
    if (EnableUVScaleOffsetNormal)
    {
        uvNormal = WrapUvIfOutside01(TransformUvFd(rawUv0, UVScaleOffsetNormal, vec2(0.0)));
    }

    vec2 uvParallax = uv;
    vec2 uvNormalParallax = uvNormal;
    if (isEye && EnableParallaxMap)
    {
        vec3 nBase = normalize(Normal);
        vec3 viewDir = normalize(CameraPos - FragPos);

        mat3 tbn;
        if (HasTangents)
        {
            vec3 t = normalize(Tangent);
            vec3 b = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
            if (dot(b, b) < 0.0001)
            {
                b = normalize(cross(nBase, t));
            }
            tbn = mat3(t, b, nBase);
        }
        else
        {
            tbn = CotangentFrame(nBase, FragPos, uvNormal);
        }

        vec3 parallaxRay = -normalize(transpose(tbn) * viewDir);
        float denomZ = max(abs(parallaxRay.z), 0.0001);

        vec2 primaryUvDdx = dFdx(uvParallax);
        vec2 primaryUvDdy = dFdy(uvParallax);

        const float parallaxMinStep = 2.0;
        const float parallaxMaxStep = 12.0;
        float stepBias = clamp(abs(dot(viewDir, nBase)), 0.0, 1.0);
        float steps = mix(parallaxMaxStep, parallaxMinStep, stepBias);
        float stepSize = 1.0 / steps;

        vec2 stepUv = parallaxRay.xy * vec2(-1.0, 1.0) / denomZ * ParallaxHeight * stepSize;
        stepUv *= normalize(max(abs(primaryUvDdx + primaryUvDdy), vec2(0.00001)));
        stepUv *= 1.0 - pow(1.0 - stepBias, 5.0);

        vec2 cur = vec2(1.0);
        vec2 prev = vec2(1.0, 1.1);
        vec2 offset = vec2(0.0);

        int stepCount = int(floor(steps) + 2.0);
        for (int i = 0; i < stepCount; i++)
        {
            cur.x = texture(ParallaxMap, uvParallax + offset).r;
            if (cur.x >= cur.y)
            {
                float dh0 = cur.x - cur.y;
                float dh1 = prev.x - prev.y;
                float ratio = dh0 / max(dh0 - dh1, 0.00001);
                offset -= stepUv * ratio;
                break;
            }

            prev = cur;
            cur.y -= stepSize;
            offset += stepUv;
        }

        uvParallax += offset;
        uvNormalParallax += offset;
    }

    bool useLayerMask = EnableLayerMaskMap && (NumMaterialLayer > 0);
    vec4 layerMask = vec4(0.0);
    float baseLayerWeight = 1.0;
    if (useLayerMask)
    {
        int index = HasUVIndexLayerMask ? UVIndexLayerMask : -1;
        vec2 maskUv;
        if (HasLayerMaskUvTransform())
        {
            vec2 baseUv = (index < 0) ? FlipV(uvParallax) : ((index == 0) ? rawUv0 : rawUv1Safe);
            maskUv = TransformUvFd(baseUv, UVScaleOffsetLayerMask, radians(UVCenterRotationLayerMask.z), UVCenterRotationLayerMask.xy);
        }
        else
        {
            maskUv = (index < 0) ? uvParallax : FlipV((index == 0) ? rawUv0 : rawUv1Safe);
        }
        layerMask = texture(LayerMaskMap, maskUv);
        layerMask = mix(vec4(0.0), layerMask, vec4(LayerMaskScale1, LayerMaskScale2, LayerMaskScale3, LayerMaskScale4));
        if (dot(BaseColorLayer2.rgb, BaseColorLayer2.rgb) < 0.000001) layerMask.g = 0.0;
        if (dot(BaseColorLayer3.rgb, BaseColorLayer3.rgb) < 0.000001) layerMask.b = 0.0;
        if (dot(BaseColorLayer4.rgb, BaseColorLayer4.rgb) < 0.000001) layerMask.a = 0.0;

        float layerSum = clamp(dot(vec4(1.0), layerMask), 0.0, 1.0);
        baseLayerWeight = clamp(1.0 - layerSum, 0.0, 1.0);
    }

    vec4 baseSample = EnableBaseColorMap ? texture(BaseColorMap, uvParallax) : vec4(1.0);
    float alphaValue = baseSample.a;
    if (EnableDisplacementMap)
    {
        vec2 rawDispUv = (UVIndexLayer3 == 0) ? rawUv0 : rawUv1Safe;
        vec2 dispUv = WrapUvIfOutside01(TransformUvFd(rawDispUv, UVScaleOffset3, vec2(0.0)));
        float dispMask = texture(DisplacementMap, dispUv).r;
        alphaValue *= clamp(dispMask, 0.0, 1.0);
    }
    if (EnableAlphaTest)
    {
        float maskValue = EnableDiscardMaskMap ? texture(DiscardMaskMap, uvParallax).r : alphaValue;
        float threshold = EnableDiscardMaskMap ? DiscardValue : AlphaTestThreshold;
        if (maskValue < threshold)
        {
            discard;
        }
    }
    else if (EnableDisplacementMap)
    {
        if (alphaValue < DiscardValue)
        {
            discard;
        }
    }

    vec3 baseSampleRgb = baseSample.rgb;

    vec3 baseColorRgb = BaseColor.rgb * baseSampleRgb;

    vec3 layer1 = BaseColorLayer1.rgb;
    vec3 layer2 = BaseColorLayer2.rgb;
    vec3 layer3 = BaseColorLayer3.rgb;
    vec3 layer4 = BaseColorLayer4.rgb;
    if (BaseColorMultiply)
    {
        layer1 *= baseSampleRgb;
        layer2 *= baseSampleRgb;
        layer3 *= baseSampleRgb;
        layer4 *= baseSampleRgb;
    }

    if (useLayerMask)
    {
        baseColorRgb *= baseLayerWeight;
        baseColorRgb = mix(baseColorRgb, layer1, layerMask.r);
        baseColorRgb = mix(baseColorRgb, layer2, layerMask.g);
        baseColorRgb = mix(baseColorRgb, layer3, layerMask.b);
        baseColorRgb = mix(baseColorRgb, layer4, layerMask.a);
    }

    vec3 shadowingColorRgb = ShadowingColor.rgb;
    if (useLayerMask)
    {
        shadowingColorRgb *= baseLayerWeight;
        shadowingColorRgb = mix(shadowingColorRgb, ShadowingColorLayer1.rgb, layerMask.r);
        shadowingColorRgb = mix(shadowingColorRgb, ShadowingColorLayer2.rgb, layerMask.g);
        shadowingColorRgb = mix(shadowingColorRgb, ShadowingColorLayer3.rgb, layerMask.b);
        shadowingColorRgb = mix(shadowingColorRgb, ShadowingColorLayer4.rgb, layerMask.a);
    }

    vec3 albedo = baseColorRgb;

    if (isEye && RequireEyelidShadowMap)
    {
        float eyelidShadow = texture(EyelidShadowMaskMap, uvParallax).r;
        float eyelidFactor = mix(1.0, 0.65, clamp(eyelidShadow, 0.0, 1.0));
        albedo *= eyelidFactor;
        shadowingColorRgb *= eyelidFactor;
    }

    float highlightMaskSample = EnableHighlightMaskMap ? texture(HighlightMaskMap, uvParallax).r : 0.0;
    vec3 highlightAdd = vec3(0.0);
    if (EnableHighlight && EnableHighlightMaskMap)
    {
        float highlightMask = clamp(highlightMaskSample, 0.0, 1.0);
        vec3 highlight = EmissionColorLayer5.rgb * EmissionIntensityLayer5;
        if (isEye)
        {
            highlightAdd = highlight * highlightMask;
        }
        else
        {
            albedo = mix(albedo, highlight, highlightMask);
            shadowingColorRgb = mix(shadowingColorRgb, highlight, highlightMask);
        }
    }

    int aoIndex = HasUVIndexAO ? UVIndexAO : -1;
    vec2 aoUv = (aoIndex < 0) ? uvParallax : FlipV((aoIndex == 0) ? rawUv0 : rawUv1Safe);
    float aoSample = EnableAOMap ? texture(AOMap, aoUv).r : 1.0;
    float occStrength = (OcclusionStrength <= 0.0) ? 1.0 : OcclusionStrength;
    float ao = pow(clamp(aoSample, 0.0, 1.0), occStrength);
    float aoOut = isEye ? 1.0 : ao;

    vec3 n = normalize(Normal);
    vec3 tangentNormal = vec3(0.0, 0.0, 1.0);
    if (EnableNormalMap)
    {
        vec4 nm = texture(NormalMap, uvNormalParallax);
        vec2 rg = nm.rg * 2.0 - 1.0;
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

    if (EnableNormalMap)
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

    vec3 lightDir = normalize(-LightDirection);
    vec3 viewDir = normalize(CameraPos - FragPos);
    vec3 halfDir = normalize(lightDir + viewDir);

    float nDotL = dot(n, lightDir);
    if (TwoSidedDiffuse)
        nDotL = abs(nDotL);
    else
        nDotL = max(nDotL, 0.0);

    float halfLambert = nDotL * 0.5 + 0.5;
    float biasedHalfLambert = mix(halfLambert, halfLambert * halfLambert, clamp(HalfLambertBias, 0.0, 1.0));
    float wrappedNdotL = biasedHalfLambert;
    if (LightWrap > 0.0)
    {
        float lw = (nDotL + LightWrap) / (1.0 + LightWrap);
        wrappedNdotL = clamp(lw, 0.0, 1.0);
        wrappedNdotL = smoothstep(0.0, 1.0, wrappedNdotL);
    }

    float roughness = EnableRoughnessMap ? texture(RoughnessMap, uv).r : 0.35;
    roughness = clamp(roughness, 0.04, 1.0);
    float metallic = EnableMetallicMap ? texture(MetallicMap, uv).r : 0.0;

    float specPower = mix(16.0, 96.0, 1.0 - roughness);
    if (EnableHairSpecular)
    {
        specPower = mix(32.0, 256.0, 1.0 - roughness);
    }
    float phongSpec = pow(max(dot(n, halfDir), 0.0), specPower);
    phongSpec *= (1.0 - roughness);
    phongSpec *= wrappedNdotL;

    float specMask = EnableSpecularMaskMap ? texture(SpecularMaskMap, uvParallax).r : 1.0;
    if (isEye)
    {
        specMask = max(specMask, clamp(highlightMaskSample, 0.0, 1.0));
    }
    float specularOffset = SpecularOffset;
    float specularContrast = SpecularContrast;
    float specularIntensity = SpecularIntensity;
    if (useLayerMask)
    {
        vec3 spec0 = vec3(SpecularOffset, SpecularContrast, SpecularIntensity);
        vec3 spec1 = vec3(SpecularLayer1Offset, SpecularLayer1Contrast, SpecularLayer1Intensity);
        vec3 spec2 = vec3(SpecularLayer2Offset, SpecularLayer2Contrast, SpecularLayer2Intensity);
        vec3 spec3 = vec3(SpecularLayer3Offset, SpecularLayer3Contrast, SpecularLayer3Intensity);
        vec3 spec4 = vec3(SpecularLayer4Offset, SpecularLayer4Contrast, SpecularLayer4Intensity);
        vec3 mixed = mix(spec0, spec1, layerMask.r);
        mixed = mix(mixed, spec2, layerMask.g);
        mixed = mix(mixed, spec3, layerMask.b);
        mixed = mix(mixed, spec4, layerMask.a);
        specularOffset = mixed.x;
        specularContrast = mixed.y;
        specularIntensity = mixed.z;
    }
    float spec = SGSpecularParam(specularOffset, phongSpec, specularContrast, specularIntensity);

    float remappedHalfLambert = smoothstep(0.0 + ShadowingShift, 1.0 + ShadowingShift, halfLambert);
    remappedHalfLambert = SGCheapContrast(remappedHalfLambert, ShadowingContrast);
    vec3 shadowedDiffuse = mix(vec3(1.0), shadowingColorRgb, clamp(remappedHalfLambert, 0.0, 1.0));
    vec3 shadedBaseColor = albedo * clamp(shadowingColorRgb + aoOut, 0.0, 1.0);

    vec3 diffuseLight = LightColor * (biasedHalfLambert * aoOut);
    float diffuseLightIntensity = max(diffuseLight.r, max(diffuseLight.g, diffuseLight.b));
    float diffuseMid = smoothstep(1.0 + MidAreaShift, MidAreaShift, diffuseLightIntensity);
    diffuseMid = SGCheapContrast(diffuseMid, MidAreaContrast);
    float diffuseDark = smoothstep(1.0 + DarkAreaShift, DarkAreaShift, diffuseLightIntensity);
    diffuseDark = SGCheapContrast(diffuseDark, DarkAreaContrast);

    vec3 shadedHue = shadedBaseColor;
    if (abs(HueShiftBias) > 0.000001 || abs(MidAreaHueOffset) > 0.000001 || abs(DarkAreaHueOffset) > 0.000001)
    {
        vec3 midColor = HueDegrees(shadedHue, MidAreaHueOffset);
        vec3 darkColor = HueDegrees(shadedHue, DarkAreaHueOffset);
        midColor = mix(shadedHue, midColor, diffuseMid);
        float hueShiftAreaFactor = mix(1.0, 0.5, HueShiftAreaValue * diffuseMid);
        vec3 darkToMid = mix(darkColor, midColor, diffuseDark) * hueShiftAreaFactor;
        vec3 midToDark = mix(midColor, darkColor, diffuseDark) * hueShiftAreaFactor;

        float shadowingGradient = smoothstep(0.0 + ShadowingShift, 1.0 + ShadowingShift, halfLambert);
        shadowingGradient = SGCheapContrast(shadowingGradient, ShadowingContrast);
        vec3 shifted = mix(darkToMid, midToDark, shadowingGradient);
        shadedHue = mix(shadedHue, shifted, vec3(clamp(HueShiftBias, 0.0, 1.0)));
    }

    vec3 diffuse = shadedHue * (1.0 - metallic);
    vec3 specColor = mix(vec3(0.04), shadedHue, metallic);
    vec3 lightTerm = AmbientColor + LightColor * wrappedNdotL;
    vec3 color = diffuse * shadowedDiffuse * lightTerm;

    float specBoost = EnableHairSpecular ? 1.25 : 1.0;
    float shadowScale = clamp(1.0 + ShadowStrength, 0.0, 2.0);
    vec3 specTerm = (spec * specColor) * (SpecularScale * specMask * specBoost) * shadowScale;
    color += specTerm;

    vec3 eyeSpecTerm = vec3(0.0);
    if (isEye)
    {
        float eyeSparkle = pow(max(dot(n, halfDir), 0.0), 384.0) * wrappedNdotL;
        float sparkleMask = 0.15 + 0.85 * clamp(highlightMaskSample, 0.0, 1.0);
        eyeSpecTerm = LightColor * eyeSparkle * sparkleMask * 0.8;
        color += eyeSpecTerm;
    }

    float rimMaskOut = 0.0;
    if (EnableRimLightMaskMap)
    {
        float rimMask = texture(RimLightMaskMap, uv).r;
        rimMaskOut = rimMask;
        float rimBase = 1.0 - max(dot(n, viewDir), 0.0);
        float rim = clamp(rimBase + RimLightOffset, 0.0, 1.0);
        float rimContrast = clamp(RimLightContrast, 0.0, 1.0);
        rim = pow(rim, mix(1.0, 6.0, rimContrast));
        rim *= RimLightIntensity;

        float backRim = pow(clamp(-dot(n, lightDir), 0.0, 1.0), 2.0) * BackRimLightIntensity;
        float rimTerm = (rim + backRim) * rimMask;
        color += rimTerm * vec3(1.0);
    }

    vec3 specViewColor = specTerm + eyeSpecTerm;
    float specView = clamp(max(specViewColor.r, max(specViewColor.g, specViewColor.b)) * 4.0, 0.0, 1.0);
    color += highlightAdd;

    if (TransparentPass)
    {
        float alpha = (EnableBaseColorMap ? baseSample.a : 1.0) * BaseColor.a;
        vec3 outColor = color;
        if (IsAuraShell)
        {
            float aura = clamp(max(AuraIntensity, 0.0), 0.0, 2.0);
            float nv = clamp(dot(n, viewDir), 0.0, 1.0);
            float rim = pow(1.0 - nv, max(AuraRimPower, 0.0001));
            float rimMask = EnableRimLightMaskMap ? texture(RimLightMaskMap, uv).r : 1.0;
            float fillAlpha = 0.35 * aura;
            float edgeAlpha = rim * rimMask * (0.65 * aura);
            float shellAlpha = clamp(fillAlpha + edgeAlpha, 0.0, 1.0);
            vec3 shellColor = vec3(0.0);
            if (PremultiplyAlpha)
            {
                shellColor *= shellAlpha;
            }
            gAlbedo = vec4(shellColor, shellAlpha);
            gNormal = vec4(0.0);
            gSpecular = vec4(0.0);
            gAO = vec4(0.0, 0.0, 0.0, 2.0);
            return;
        }
        if (HasAuraTextures)
        {
            outColor = vec3(0.0);
        }
        if (HasAuraTextures || EnableAuraEffect)
        {
            float aura = clamp(max(AuraIntensity, 0.0), 0.0, 2.0);
            float nv = clamp(dot(n, viewDir), 0.0, 1.0);
            float rim = pow(1.0 - nv, max(AuraRimPower, 0.0001));
            float rimMask = EnableRimLightMaskMap ? texture(RimLightMaskMap, uv).r : 1.0;

            float fillAlpha = 0.12 * aura;
            float edgeAlpha = rim * rimMask * (0.88 * aura);
            alpha *= clamp(fillAlpha + edgeAlpha, 0.0, 1.0);
        }
        if (PremultiplyAlpha)
        {
            outColor *= alpha;
        }
        gAlbedo = vec4(outColor, alpha);
        gNormal = vec4(0.0);
        gSpecular = vec4(0.0);
        gAO = vec4(0.0, 0.0, 0.0, 2.0);
        return;
    }

    if (LegacyMode)
    {
        gAlbedo = vec4(albedo, 0.0);
        gNormal = vec4(normalize(Normal) * 0.5 + 0.5, 0.0);
        gSpecular = vec4(aoOut, specView, 0.0, 0.0);
        gAO = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec3 emission = vec3(0.0);
    if (EnableTeraEffect)
    {
        vec3 tint = clamp(TeraColor, vec3(0.0), vec3(8.0));
        float nv = clamp(dot(n, viewDir), 0.0, 1.0);
        float rim = pow(clamp(1.0 - nv, 0.0, 1.0), 4.0);
        float cell = dot(floor(FragPos * 40.0), vec3(12.9898, 78.233, 37.719));
        float sparkle = step(0.985, fract(sin(cell) * 43758.5453));
        float sparkleAnim = 0.5 + 0.5 * sin(time_params.x * 12.0 + dot(FragPos, vec3(3.1, 4.2, 5.3)));
        sparkle *= sparkleAnim;
        emission += tint * (rim * 0.55 + sparkle * 1.2) * clamp(TeraStrength, 0.0, 4.0);
        color = mix(color, color * mix(vec3(1.0), tint, 0.65), 0.25 * clamp(TeraStrength, 0.0, 4.0));
    }
    gAlbedo = vec4(color, ShadowingGIGain);
    gNormal = vec4(n * 0.5 + 0.5, ShadowStrength);
    gSpecular = vec4(aoOut, rimMaskOut, 0.0, 0.0);
    gAO = vec4(emission, 2.0);
}
