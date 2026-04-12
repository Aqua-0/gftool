#version 330 core
layout(location = 0) in vec3 in_pos;
layout(location = 1) in vec4 in_col;
layout(location = 2) in vec2 in_uv;
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
out vec4 v_col;
out vec2 v_uv;
void main()
{
    gl_Position = projection * view * model * vec4(in_pos, 1.0);
    v_col = in_col;
    v_uv = in_uv;
}
