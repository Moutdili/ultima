shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_amount : hint_range(0.0, 6.0) = 2.0;
uniform vec4  tint : source_color = vec4(0.098, 0.161, 0.188, 0.78); // #192930 @ 0.78
uniform vec2  panel_size = vec2(260.0, 200.0);

void fragment() {
    vec2 uv = SCREEN_UV;
    vec4 acc = vec4(0.0);
    float n = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 off = vec2(float(x), float(y)) * blur_amount / panel_size;
            acc += texture(screen_texture, uv + off);
            n += 1.0;
        }
    }
    vec4 blurred = acc / n;
    vec3 mixed = mix(blurred.rgb, tint.rgb, tint.a);
    // highlight interne en haut du panneau
    float top = smoothstep(0.0, 0.10, 1.0 - UV.y);
    mixed += vec3(0.06) * top;
    COLOR = vec4(mixed, 0.88);
}
