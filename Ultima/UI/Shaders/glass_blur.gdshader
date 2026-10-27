shader_type canvas_item;

// Verre ULTIMA, base = shader du jeu game/pck/shaders/dark_blur.gdshader
// (hint_screen_texture + filter_linear_mipmap + échantillonnage au lod, mix vers une teinte).
// Décision chef D11/G1 : 3x3 taps AU LOD, offsets en pixels écran indépendants de la taille
// du panneau -> flou lisse sans fantôme, identique pendant le Tween et sur le rail de 8 px.
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float lod : hint_range(0.0, 5.0) = 3.5;
uniform float blur_amount : hint_range(0.0, 6.0) = 2.0;
uniform vec3  tint : source_color = vec3(0.098, 0.161, 0.188); // #192930
uniform float mix_percentage : hint_range(0.0, 1.0) = 0.78;    // charte §3 : 72-80 %
uniform vec2  rect_size = vec2(260.0, 200.0);  // taille du ColorRect (signal Resized, jamais par frame)
uniform float corner_radius = 10.0;            // = UltimaStyles.CornerRadius (liseré GlassEdge)

// Distance signée à un rectangle arrondi centré (négative à l'intérieur).
float rounded_rect_sdf(vec2 p, vec2 half_size, float r) {
    vec2 q = abs(p) - half_size + vec2(r);
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
    vec2 step_uv = blur_amount * 0.5 * SCREEN_PIXEL_SIZE * exp2(lod);
    vec4 acc = vec4(0.0);
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            acc += texture(screen_texture, SCREEN_UV + vec2(float(x), float(y)) * step_uv, lod);
        }
    }
    vec3 mixed = mix(acc.rgb / 9.0, tint, mix_percentage);
    // highlight interne blanc 6 % : bord HAUT seulement (G2, correctif QA du §3)
    float top = 1.0 - smoothstep(0.0, 0.10, UV.y);
    mixed += vec3(0.06) * top;
    // coins arrondis sur le vrai verre (G3) : masque SDF, anticrénelage 1 px
    vec2 half_size = rect_size * 0.5;
    float r = min(corner_radius, min(half_size.x, half_size.y));
    float d = rounded_rect_sdf(UV * rect_size - half_size, half_size, r);
    float mask = clamp(0.5 - d, 0.0, 1.0);
    COLOR = vec4(mixed, 0.88 * mask);
}
