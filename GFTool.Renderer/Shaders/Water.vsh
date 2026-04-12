#version 420 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;
layout (location = 8) in vec2 aTexCoord2;
layout (location = 3) in vec4 aColor;
layout (location = 4) in vec4 aTangent;
layout (location = 5) in vec3 aBinormal;
layout (location = 6) in vec4 aBlendIndices;
layout (location = 7) in vec4 aBlendWeights;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform bool EnableSkinning;
uniform bool SwapBlendOrder;
uniform mat4 Bones[192];
uniform int BoneCount;
uniform vec4 time_params;

uniform bool EnableWaveAnimation;
uniform bool EnableVertexAnimationMask;
uniform bool EnableVertexFoamMask;
uniform bool EnableVertexAlpha;
uniform bool EnableBinormalWaveDirection;

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

uniform float FoamWaveHeightMin;
uniform float FoamWaveHeightMax;

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;
out vec4 UV01;
out vec4 Color;
out vec3 Tangent;
out vec3 Bitangent;
out vec3 Binormal;

float LinearStep(float a, float b, float v)
{
    float d = max(b - a, 0.00001);
    return clamp((v - a) / d, 0.0, 1.0);
}

vec2 Hash2(vec2 p)
{
    vec2 q = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(q) * 43758.5453) * 2.0 - 1.0;
}

float GradientNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = dot(Hash2(i + vec2(0.0, 0.0)), f - vec2(0.0, 0.0));
    float b = dot(Hash2(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
    float c = dot(Hash2(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
    float d = dot(Hash2(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y) + 0.5;
}

float GradientNoiseHeight(vec2 position, float len, float speed, float amp, float time)
{
    float freq = sqrt(2.0 / max(len, 0.00001));
    float phase = speed * freq;
    float theta = GradientNoise(position) * freq - time * phase;
    return amp * sin(theta);
}

vec3 GerstnerWaveOffset(vec2 position, vec2 dir, float len, float speed, float amp, float steep, float time)
{
    float freq = sqrt(2.0 / max(len, 0.00001));
    float phase = speed * freq;
    float qi = steep / (amp * freq + 0.000001);
    float theta = dot(dir, position) * freq - time * phase;
    float s = sin(theta);
    float c = cos(theta);
    vec2 waveH = qi * amp * dir * c;
    return vec3(waveH.x, amp * s, waveH.y);
}

void main()
{
    vec4 p0 = vec4(aPos, 1.0);
    vec3 n0 = aNormal;
    vec3 t0 = aTangent.xyz;
    vec3 b0 = aBinormal;

    if (EnableSkinning && BoneCount > 0)
    {
        vec4 w = aBlendWeights;
        ivec4 bi = ivec4(aBlendIndices + 0.5);
        if (SwapBlendOrder)
        {
            w = w.wxyz;
            bi = ivec4(bi.w, bi.x, bi.y, bi.z);
        }
        float ws = w.x + w.y + w.z + w.w;
        if (ws > 0.0)
        {
            w /= ws;
        }
        bi = clamp(bi, ivec4(0), ivec4(BoneCount - 1));
        mat4 sm = w.x * Bones[bi.x]
                + w.y * Bones[bi.y]
                + w.z * Bones[bi.z]
                + w.w * Bones[bi.w];
        p0 = sm * vec4(aPos, 1.0);
        mat3 sm3 = mat3(sm);
        n0 = normalize(sm3 * aNormal);
        t0 = normalize(sm3 * aTangent.xyz);
        b0 = normalize(sm3 * aBinormal);
    }

    vec4 worldPos4 = model * p0;

    mat3 nm = transpose(inverse(mat3(model)));
    Normal = normalize(nm * n0);
    vec3 tw = normalize(nm * t0);
    float s = (aTangent.w < 0.0) ? -1.0 : 1.0;
    Tangent = tw;
    Bitangent = normalize(cross(Normal, tw) * s);
    Binormal = normalize(nm * b0);

    TexCoord = aTexCoord;
    UV01 = vec4(aTexCoord, aTexCoord2);
    float t = time_params.x;
    float foam = 1.0;
    if (EnableWaveAnimation)
    {
        vec3 worldNormal = Normal;
        vec3 worldTangent = Tangent;
        vec2 waveDir = normalize(vec2(WaveDirectionX0, WaveDirectionY0) + vec2(0.0001));
        if (EnableBinormalWaveDirection)
        {
            vec3 bin0 = (s * cross(worldNormal, worldTangent));
            vec3 worldTangent1 = worldTangent;
            vec3 bin1 = bin0;
            float blend = aColor.b;
            vec3 mixedBin = mix(bin0, bin1, clamp(blend, 0.0, 1.0));
            waveDir = mix(waveDir, normalize(mixedBin.xz + vec2(0.0001)), clamp(WaveBinormalInfluence, 0.0, 1.0));
        }
        vec3 off = GerstnerWaveOffset(worldPos4.xz, waveDir, WaveLength0, WaveSpeed0, WaveAmplitude0, WaveSteepness0, t);
        off.y += GradientNoiseHeight(worldPos4.xz, WaveLength1, WaveSpeed1, WaveAmplitude1, t);
        if (EnableVertexAnimationMask)
        {
            off *= aColor.g;
        }
        worldPos4.xyz += off;
        float ampSum = max(WaveAmplitude0 + WaveAmplitude1, 0.0001);
        float hMin = FoamWaveHeightMin * ampSum;
        float hMax = FoamWaveHeightMax * ampSum;
        foam = LinearStep(hMin, hMax, off.y);
        foam = clamp(1.0 - foam, 0.0, 1.0);
    }

    FragPos = worldPos4.xyz;

    float foamMask = EnableVertexFoamMask ? aColor.r : 1.0;
    float alpha = EnableVertexAlpha ? aColor.a : 1.0;
    Color = vec4(foamMask, foam, aColor.b, alpha);

    gl_Position = projection * view * vec4(FragPos, 1.0);
}
