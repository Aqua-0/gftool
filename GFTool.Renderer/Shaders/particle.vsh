#version 330 core

layout(location = 0) in vec2 aCorner;
layout(location = 1) in vec2 aUv;

layout(location = 2) in vec3 iCenter;
layout(location = 3) in float iSize;
layout(location = 4) in vec4 iColor;
layout(location = 5) in float iRotation;

out vec2 vUv;
out vec4 vColor;

uniform mat4 view;
uniform mat4 proj;

void main()
{
    float s = sin(iRotation);
    float c = cos(iRotation);
    vec2 corner = vec2(c * aCorner.x - s * aCorner.y, s * aCorner.x + c * aCorner.y);

    vec4 centerVS = view * vec4(iCenter, 1.0);
    centerVS.xy += corner * iSize;
    gl_Position = proj * centerVS;

    vUv = aUv;
    vColor = iColor;
}
