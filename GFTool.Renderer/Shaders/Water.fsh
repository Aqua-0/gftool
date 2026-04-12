#version 420 core

uniform sampler2D SceneColor;
uniform sampler2D SceneDepth;
uniform samplerCube EnvCubemap;

uniform sampler2D BaseColorMap;
uniform sampler2D BaseColorMap1;
uniform sampler2D FoamMaskMap;
uniform sampler2D FlowMap;
uniform sampler2D NormalMap;
uniform sampler2D NormalMap1;

uniform vec4 BaseColor;
uniform vec4 BaseColorLayer1;
uniform vec4 ScrollUVSpeed;
uniform vec4 ScrollUVSpeed1;
uniform vec4 UVScaleOffset;
uniform vec4 UVScaleOffset1;
uniform int UVTransformMode;
uniform int UVIndexLayer1;
uniform float LayerMaskScale1;
uniform float NormalHeight;
uniform float NormalHeight1;
uniform float Metallic;
uniform float Roughness;
uniform vec4 EmissionColor;
uniform float EmissionIntensity;
uniform float ReflectionScaleX;
uniform float ReflectionScaleY;
uniform float RefractionScaleX;
uniform float RefractionScaleY;
uniform float RefractionDepthBias;
uniform float DepthFadeDistance;
uniform float SoftEdgeRatio;
uniform float FoamEdgeRatio;
uniform float WaterOpaqueDistanceStart;
uniform float WaterOpaqueDistance;
uniform float WaterOpaquePower;
uniform float FresnelAlphaMin;
uniform float FresnelAlphaMax;

uniform vec2 ScreenSize;
uniform float CameraNear;
uniform float CameraFar;
uniform float EnvMaxLod;
uniform float EnvIntensity;

uniform vec4 WaterFogColorNear;
uniform vec4 WaterFogColorFar;
uniform vec4 FoamColor;

uniform float WaterFogNearLength;
uniform float WaterFogFarLength;
uniform float WaterFogPower;

uniform vec3 LightDirection;
uniform vec3 LightColor;
uniform vec3 AmbientColor;

uniform float FlowPower;
uniform float FlowScaleX;
uniform float FlowScaleY;
uniform float FoamMaskUVScale;
uniform float FoamMaskIntensity;
uniform float FoamFlowPower;
uniform float FoamSharpness;
uniform float FoamWaveInfluence;

uniform float WaveAmplitude0;
uniform float WaveAmplitude1;
uniform float WaveSpeed0;
uniform float WaveSpeed1;
uniform float WaveLength0;
uniform float WaveLength1;
uniform float WaveDirectionX0;
uniform float WaveDirectionY0;
uniform float WaveSteepness0;
uniform float WaveBinormalInfluence;

uniform float WaterSpecThresholdMin;
uniform float WaterSpecThresholdMax;

uniform int NumMaterialLayer;

uniform bool EnableWaveAnimation;
uniform bool EnableFlowMap;
uniform bool EnableDepthFade;
uniform bool EnableVertexFoamMask;
uniform bool EnableVertexAnimationMask;
uniform bool EnableScreenSpaceReflection;
uniform bool EnableVertexAlpha;
uniform bool EnableConservativeFoamFade;
uniform bool EnableBaseColorMap;
uniform bool EnableBaseColorMap1;
uniform bool EnableFoamMaskMap;
uniform bool EnableNormalMap;
uniform bool EnableNormalMap1;
uniform bool TransparentPass;

uniform vec3 CameraPos;
uniform bool HasTangents;
uniform bool HasBinormals;
uniform bool HasUv1;
uniform bool FlipNormalY;
uniform bool ReconstructNormalZ;
uniform vec4 time_params;

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

vec2 ApplyUvTransform(vec2 uv, vec4 srt, int mode)
{
    if (mode == 1)
    {
        return uv + srt.zw;
    }
    return uv * srt.xy + srt.zw;
}

mat3 MakeFrame(vec3 n, vec2 uv)
{
    if (HasTangents)
    {
        vec3 t = Tangent;
        if (dot(t, t) > 0.0001)
        {
        vec3 bt = HasBinormals ? normalize(Binormal) : normalize(Bitangent);
        if (dot(bt, bt) < 0.0001)
        {
                bt = normalize(cross(n, normalize(t)));
        }
            return mat3(normalize(t), bt, n);
        }
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

float LinearStep(float a, float b, float v)
{
    float d = max(b - a, 0.00001);
    return clamp((v - a) / d, 0.0, 1.0);
}

float LinearizeDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    float nearPlane = max(CameraNear, 0.0001);
    float farPlane = max(CameraFar, nearPlane + 0.001);
    return (2.0 * nearPlane * farPlane) / (farPlane + nearPlane - z * (farPlane - nearPlane));
}

vec3 EnvBRDFApprox(vec3 F0, float roughness, float NdotV)
{
    vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
    vec4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    vec2 ab = vec2(-1.04, 1.04) * a004 + r.zw;
    return F0 * ab.x + ab.y;
}

vec4 SampleFlow(sampler2D tex, vec2 uv, vec4 flowOffset, float blendWeight)
{
    if (!EnableFlowMap)
    {
        return texture(tex, uv);
    }
    return mix(texture(tex, uv + flowOffset.xy), texture(tex, uv + flowOffset.zw), blendWeight);
}

void main()
{
    vec2 uv0Raw = ChooseUv(0);
    vec2 uv0 = ApplyUvTransform(uv0Raw, UVScaleOffset, UVTransformMode);
    float t = time_params.x;

    vec2 primaryUv = uv0 + ScrollUVSpeed.xy * t;

    vec4 flowOffset = vec4(0.0);
    float blendWeight = 0.0;
    if (EnableFlowMap)
    {
        vec2 flowUv = vec2(uv0Raw.x, 1.0 - uv0Raw.y);
        vec2 flowVec = 1.0 - 2.0 * texture(FlowMap, flowUv).xy;
        flowVec *= vec2(FlowScaleX, FlowScaleY);
        vec2 phase = fract(t * FlowPower + vec2(0.0, 0.5));
        blendWeight = abs(2.0 * phase.x - 1.0);
        flowOffset = vec4(flowVec * phase.x, flowVec * phase.y);
        flowOffset.yw *= -1.0;
    }

    vec3 nBase = normalize(Normal);
    mat3 tbn = MakeFrame(nBase, primaryUv);

    vec3 nt = vec3(0.0, 0.0, 1.0);
    if (EnableNormalMap)
    {
        nt = DecodeNormalSample(SampleFlow(NormalMap, primaryUv, flowOffset, blendWeight), NormalHeight);
    }
    if (NumMaterialLayer > 1 && EnableNormalMap1)
    {
        vec2 uv1Raw = (UVIndexLayer1 < 0) ? uv0Raw : ChooseUv(UVIndexLayer1);
        vec2 uv1 = ApplyUvTransform(uv1Raw, UVScaleOffset1, UVTransformMode) + ScrollUVSpeed1.xy * t;
        vec3 nt1 = DecodeNormalSample(SampleFlow(NormalMap1, uv1, flowOffset, blendWeight), NormalHeight1);
        nt = normalize(mix(nt, nt1, clamp(LayerMaskScale1, 0.0, 1.0)));
    }
    vec3 n = normalize(tbn * nt);
    n *= (gl_FrontFacing ? 1.0 : -1.0);

    vec4 base = BaseColor;
    if (EnableBaseColorMap)
    {
        base *= SampleFlow(BaseColorMap, primaryUv, flowOffset, blendWeight);
    }
    if (NumMaterialLayer > 1 && EnableBaseColorMap1)
    {
        vec2 uv1Raw = (UVIndexLayer1 < 0) ? uv0Raw : ChooseUv(UVIndexLayer1);
        vec2 uv1 = ApplyUvTransform(uv1Raw, UVScaleOffset1, UVTransformMode) + ScrollUVSpeed1.xy * t;
        vec4 layer1 = SampleFlow(BaseColorMap1, uv1, flowOffset, blendWeight) * BaseColorLayer1;
        float layerWeight = clamp(LayerMaskScale1, 0.0, 1.0);
        base.rgb = mix(base.rgb, layer1.rgb, layerWeight);
        base.a *= mix(1.0, layer1.a, layerWeight);
    }

    float foamMask = 0.0;
    if (EnableFoamMaskMap)
    {
        vec2 foamUv = uv0 * max(FoamMaskUVScale, 0.0001);
        foamMask = SampleFlow(FoamMaskMap, foamUv, flowOffset * FoamFlowPower, blendWeight).r;
        foamMask = clamp(foamMask * FoamMaskIntensity, 0.0, 1.0);
    }
    float foamV = mix(1.0, Color.g, clamp(FoamWaveInfluence, 0.0, 1.0));
    float foam = clamp(foamMask * foamV, 0.0, 1.0);
    if (EnableVertexFoamMask)
    {
        foam *= clamp(1.0 - Color.r, 0.0, 1.0);
    }

    float linearZ = LinearizeDepth(gl_FragCoord.z);
    float fogT = LinearStep(WaterFogNearLength, WaterFogFarLength, linearZ);
    fogT = pow(fogT, max(WaterFogPower, 0.0001));
    vec3 fogMul = mix(WaterFogColorNear.rgb, WaterFogColorFar.rgb, fogT);

    vec3 baseRgb = base.rgb * fogMul;
    float rough = clamp(Roughness + foam * (1.0 - Roughness), 0.04, 1.0);
    vec3 emission = EmissionColor.rgb * EmissionIntensity;

    vec3 viewVec = normalize(CameraPos - FragPos);
    vec3 lightVec = normalize(-LightDirection);
    float nl = max(dot(n, lightVec), 0.0);
    vec3 halfVec = normalize(viewVec + lightVec);
    float nh = max(dot(n, halfVec), 0.0);

    float specPow = mix(512.0, 32.0, rough);
    float w = smoothstep(WaterSpecThresholdMin, WaterSpecThresholdMax, nl);
    float spec = pow(nh, specPow) * w;
    float fres = pow(1.0 - max(dot(n, viewVec), 0.0), 5.0);
    float reflectionStrength = clamp(0.5 * (abs(ReflectionScaleX) + abs(ReflectionScaleY)), 0.0, 2.0);

    vec3 envColor = vec3(0.75, 0.82, 0.9);
    vec3 reflection = envColor * (reflectionStrength * fres);
    float envI = max(EnvIntensity, 0.0);
    float nv = clamp(dot(n, viewVec), 0.00001, 1.0);
    vec3 R = reflect(-viewVec, n);
    float envLod = rough * max(EnvMaxLod, 0.0);
    vec3 envSpec = textureLod(EnvCubemap, R, envLod).rgb;
    vec3 envDiff = textureLod(EnvCubemap, n, max(EnvMaxLod, 0.0)).rgb;
    vec3 F0 = vec3(0.04);
    vec3 envSpecTerm = envSpec * EnvBRDFApprox(F0, rough, nv);
    vec3 envDiffTerm = envDiff * baseRgb;
    vec3 litColor = baseRgb * (AmbientColor + LightColor * nl) + LightColor * spec * (0.15 + 0.85 * fres) + reflection + envI * (envDiffTerm + envSpecTerm);

    float alpha = clamp(base.a, 0.0, 1.0);
    if (EnableVertexAlpha)
    {
        alpha *= clamp(Color.a, 0.0, 1.0);
    }

    if (TransparentPass)
    {
        vec2 screenUv = gl_FragCoord.xy / max(ScreenSize, vec2(1.0));
        float sceneDepth0 = texture(SceneDepth, screenUv).r;
        float fragLin = LinearizeDepth(gl_FragCoord.z);
        float sceneLin0 = LinearizeDepth(sceneDepth0);
        float depthDiff = max(sceneLin0 - fragLin, 0.0);

        float depthFadeFactor = 1.0;
        if (EnableDepthFade)
        {
            depthFadeFactor = LinearStep(0.0, max(DepthFadeDistance, 0.0001), depthDiff);
        }

        float foamMaskForRefraction = clamp(foam, 0.0, 1.0);
        float fresnelAlpha = FresnelAlphaMin + (FresnelAlphaMax - FresnelAlphaMin) * pow(1.0 - nv, 5.0);
        float opacityForRefraction = base.a * mix(fresnelAlpha, 1.0, foamMaskForRefraction);

        vec2 refractedUvOffset = nt.xy * vec2(-RefractionScaleX, RefractionScaleY) * (1.0 - foamMaskForRefraction);
        float depthScale = clamp(depthDiff / max(RefractionDepthBias, 0.0001), 0.0, 1.0);
        refractedUvOffset *= depthScale;

        vec2 refractUv = clamp(screenUv + refractedUvOffset, vec2(0.001), vec2(0.999));
        float sceneDepth1 = texture(SceneDepth, refractUv).r;
        if (sceneDepth1 < gl_FragCoord.z)
        {
            refractUv = screenUv;
            sceneDepth1 = sceneDepth0;
        }

        float sceneLin1 = LinearizeDepth(sceneDepth1);
        float refrDepthDiff = max(sceneLin1 - fragLin, 0.0);

        if (EnableDepthFade)
        {
            opacityForRefraction *= LinearStep(0.0, max(DepthFadeDistance, 0.0001), refrDepthDiff);
            float depthDiffV = refrDepthDiff * abs(viewVec.y);
            float opaqueFactor = LinearStep(WaterOpaqueDistanceStart, WaterOpaqueDistance, depthDiffV);
            opacityForRefraction = mix(opacityForRefraction, 1.0, pow(clamp(opaqueFactor, 0.0, 1.0), max(WaterOpaquePower, 0.0001)));
        }

        vec3 sceneColor = texture(SceneColor, refractUv).rgb;
        float opacityMix = clamp(opacityForRefraction, 0.0, 1.0);
        vec3 waterColor = mix(sceneColor, litColor, opacityMix);

        if (EnableScreenSpaceReflection)
        {
            vec2 reflectedUvOffset = nt.xy * vec2(ReflectionScaleX, ReflectionScaleY);
            vec2 reflectedUv = clamp(screenUv + reflectedUvOffset, vec2(0.001), vec2(0.999));
            vec3 ssr = texture(SceneColor, reflectedUv).rgb;
            vec3 F0 = vec3(0.04);
            vec3 dfg = EnvBRDFApprox(F0, rough, nv);
            waterColor += ssr * (dfg * max(EnvIntensity, 0.0)) * (1.0 - foamMaskForRefraction) * (1.0 - opacityMix);
        }

        if (EnableDepthFade)
        {
            float foamFade = depthFadeFactor;
            if (EnableConservativeFoamFade)
            {
                foamFade = LinearStep(0.0, max(DepthFadeDistance, 0.0001), depthDiff * abs(viewVec.y));
            }

            float denom = max(FoamEdgeRatio, 0.000001);
            float edgeMask = 1.0 - clamp((foamFade - SoftEdgeRatio) / denom, 0.0, 1.0) * foamMask;
            float foamIntensity = clamp((1.0 - foamFade) / max(1.0 - SoftEdgeRatio, 0.0001), 0.0, 1.0);
            waterColor += max(FoamColor.rgb * pow(foamIntensity, max(FoamSharpness, 0.0001)) * edgeMask, vec3(0.0));
        }

        float outAlpha = 1.0;
        if (EnableVertexAlpha)
        {
            outAlpha *= clamp(Color.a, 0.0, 1.0);
        }

        gAlbedo = vec4(waterColor + emission, outAlpha);
        gNormal = vec4(0.0);
        gSpecular = vec4(0.0);
        gAO = vec4(0.0);
        return;
    }

    float refl = 1.0;
    gAlbedo = vec4(litColor + emission, rough);
    gNormal = vec4(n * 0.5 + 0.5, refl);
    gSpecular = vec4(1.0, 0.0, 0.0, 0.0);
    gAO = vec4(0.0);
}
