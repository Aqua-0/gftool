#version 330 core

in vec2 vUv;
in vec4 vColor;

out vec4 FragColor;

uniform sampler2D Tex;

void main()
{
    vec4 t = texture(Tex, vUv);
    FragColor = vec4(vColor.rgb * t.rgb, vColor.a * t.a);
}
