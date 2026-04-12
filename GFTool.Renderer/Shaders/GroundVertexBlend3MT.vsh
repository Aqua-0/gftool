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
uniform bool EnableWorldXzUv;
uniform float UVRotation;

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;
out vec4 UV01;
out vec4 Color;
out vec3 Tangent;
out vec3 Bitangent;
out vec3 Binormal;

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
    FragPos = worldPos4.xyz;

    mat3 nm = transpose(inverse(mat3(model)));
    Normal = normalize(nm * n0);
    vec3 tw = normalize(nm * t0);
    float s = (aTangent.w < 0.0) ? -1.0 : 1.0;
    vec3 bn = normalize(nm * b0);

    vec2 uv0 = aTexCoord;
    if (EnableWorldXzUv)
    {
        float ang = UVRotation * 6.28318530718;
        float cs = cos(ang);
        float sn = sin(ang);
        uv0 = mat2(cs, sn, -sn, cs) * worldPos4.xz;

        vec3 axis = vec3(cs, 0.0, -sn);
        vec3 worldBinormal = cross(axis, Normal);
        if (dot(worldBinormal, worldBinormal) < 1e-8)
        {
            worldBinormal = vec3(sn, 0.0, cs);
        }
        bn = normalize(worldBinormal);
        tw = normalize(cross(Normal, bn));
        s = -1.0;
    }

    Tangent = tw;
    Bitangent = normalize(cross(Normal, tw) * s);
    Binormal = bn;

    TexCoord = uv0;
    UV01 = vec4(uv0, aTexCoord2);
    Color = aColor;

    gl_Position = projection * view * vec4(FragPos, 1.0);
}
