#version 330 core
out vec4 FragColor;
uniform vec4 uColor;
in vec4 v_col;
in vec2 v_uv;
void main()
{
    FragColor = uColor * v_col;
}
