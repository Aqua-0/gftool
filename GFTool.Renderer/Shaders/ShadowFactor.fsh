#version 420 core

layout (location = 0) in vec2 inTexCoord;
layout (location = 0) out float outShadow;

uniform sampler2D normalTexture;
uniform sampler2D depthTexture;
uniform sampler2DArrayShadow shadowMap;

uniform mat4 InvView;
uniform mat4 InvProjection;
uniform mat4 View;
uniform mat4 Projection;
uniform vec3 CameraPos;
uniform vec3 LightDirection;
uniform vec4 CascadeSplits;
uniform mat4 ShadowMatrices[4];
uniform float ShadowDepthBias;
uniform float ShadowNormalBias;
uniform float ShadowPcfRadius;
uniform vec2 ShadowMapTexelSize;

uniform bool EnableScreenSpaceShadows;
uniform int ScreenSpaceShadowSteps;
uniform float ScreenSpaceShadowStepSize;
uniform float ScreenSpaceShadowThickness;
uniform float CameraNear;
uniform float CameraFar;

float LinearizeDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    float nearPlane = max(CameraNear, 0.0001);
    float farPlane = max(CameraFar, nearPlane + 0.001);
    return (2.0 * nearPlane * farPlane) / (farPlane + nearPlane - z * (farPlane - nearPlane));
}

vec3 ReconstructViewPos(vec2 uv, float depth)
{
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = InvProjection * ndc;
    viewPos.xyz /= max(viewPos.w, 0.00001);
    return viewPos.xyz;
}

vec3 ReconstructWorldPos(vec2 uv, float depth)
{
    vec3 viewPos = ReconstructViewPos(uv, depth);
    vec4 worldPos = InvView * vec4(viewPos, 1.0);
    return worldPos.xyz;
}

int SelectCascade(float cameraDistance)
{
    int idx = 0;
    if (cameraDistance > CascadeSplits.x) idx = 1;
    if (cameraDistance > CascadeSplits.y) idx = 2;
    if (cameraDistance > CascadeSplits.z) idx = 3;
    return idx;
}

float SampleShadowPcf(vec3 worldPos, vec3 normal, int cascadeIndex)
{
    mat4 m = ShadowMatrices[cascadeIndex];
    vec4 sp = m * vec4(worldPos, 1.0);
    sp.xyz /= max(sp.w, 0.00001);
    vec3 uvz = sp.xyz * 0.5 + 0.5;
    if (uvz.x <= 0.0 || uvz.x >= 1.0 || uvz.y <= 0.0 || uvz.y >= 1.0 || uvz.z <= 0.0 || uvz.z >= 1.0)
    {
        return 1.0;
    }

    vec3 lightDir = normalize(-LightDirection);
    float ndotl = clamp(dot(normal, lightDir), 0.0, 1.0);
    float slope = 1.0 - ndotl;
    float bias = ShadowDepthBias + ShadowNormalBias * slope;
    float refZ = uvz.z - bias;

    float r = max(0.0, ShadowPcfRadius);
    vec2 stepUv = ShadowMapTexelSize * r;

    float sum = 0.0;
    sum += texture(shadowMap, vec4(uvz.xy + vec2(-1.0, -1.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 0.0, -1.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 1.0, -1.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2(-1.0,  0.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 0.0,  0.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 1.0,  0.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2(-1.0,  1.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 0.0,  1.0) * stepUv, float(cascadeIndex), refZ));
    sum += texture(shadowMap, vec4(uvz.xy + vec2( 1.0,  1.0) * stepUv, float(cascadeIndex), refZ));
    return sum / 9.0;
}

float ScreenSpaceShadow(vec3 viewPos, vec3 normalWorld)
{
    vec3 lightDirWorld = normalize(-LightDirection);
    vec3 lightDirView = normalize((View * vec4(lightDirWorld, 0.0)).xyz);

    float cameraFade = clamp(length(viewPos) / max(CascadeSplits.w, 1.0), 0.0, 1.0);
    float factor = 1.0;

    vec3 stepVec = lightDirView * ScreenSpaceShadowStepSize;
    vec3 p = viewPos;
    float thickness = max(0.0001, ScreenSpaceShadowThickness);

    for (int i = 0; i < ScreenSpaceShadowSteps; i++)
    {
        p += stepVec;
        vec4 clip = Projection * vec4(p, 1.0);
        if (clip.w <= 0.00001) break;
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = ndc.xy * 0.5 + 0.5;
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) break;

        float d = texture(depthTexture, uv).r;
        if (d <= 0.0 || d >= 1.0) continue;
        float sceneZ = LinearizeDepth(d);
        float testZ = -p.z;
        float delta = sceneZ - testZ;
        if (delta > 0.0 && delta < thickness)
        {
            factor = 0.0;
            break;
        }
    }

    return mix(1.0, factor, 1.0 - cameraFade);
}

void main()
{
    float depth = texture(depthTexture, inTexCoord).r;
    if (depth >= 1.0)
    {
        outShadow = 1.0;
        return;
    }

    vec3 normal = normalize(texture(normalTexture, inTexCoord).rgb * 2.0 - 1.0);
    vec3 worldPos = ReconstructWorldPos(inTexCoord, depth);
    float cameraDistance = length(CameraPos - worldPos);
    int cascadeIndex = SelectCascade(cameraDistance);
    float shadowMapFactor = SampleShadowPcf(worldPos, normal, cascadeIndex);

    float sss = 1.0;
    if (EnableScreenSpaceShadows)
    {
        vec3 viewPos = ReconstructViewPos(inTexCoord, depth);
        sss = ScreenSpaceShadow(viewPos, normal);
    }

    outShadow = min(shadowMapFactor, sss);
}
