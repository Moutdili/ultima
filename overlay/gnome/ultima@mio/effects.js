// ULTIMA — effets GLSL du verre (niveaux `verre` et `loupe`), via Shell.GLSLEffect.
//
// API vérifiées dans GNOME Shell 50.6 (typelibs Shell-18 / Cogl-18 et sources de libshell-18.so) :
//   - Shell.GLSLEffect : add_glsl_snippet, get_uniform_location, set_uniform_float ; parent
//     Clutter.OffscreenEffect (Shell-18.typelib) ;
//   - modèle d'usage : ui/lightbox.js (RadialShaderEffect, vfunc_build_pipeline +
//     add_glsl_snippet(Cogl.SnippetHook.FRAGMENT, …, true)) et ui/messageList.js (FadeEffect :
//     « uniform sampler2D tex; » + texture2D(tex, cogl_tex_coord_in[0].xy)) ;
//   - Cogl.SnippetHook.FRAGMENT = 2048 (Cogl-18.typelib).
//
// Les classes sont enregistrées PARESSEUSEMENT (au premier usage) : si Shell.GLSLEffect manque,
// rien n'est enregistré et hud.js fait retomber le niveau à `eco` (aucune exception au chargement).
// Aucune uniforme ne dépend du temps : rien n'est animé, l'effet n'est repeint que si l'acteur l'est.

import Cogl from 'gi://Cogl';
import GObject from 'gi://GObject';
import Shell from 'gi://Shell';

// ---- reflet : spéculaire diagonal + liseré interne haut (blanc 6 %, docs/17 §3) + grain fixe ----
// Appliqué sur la plaque teintée (St : #192930 @ 0.72 + liseré or 1 px + coins 10). `rim` (loupe) :
// éclaircit la teinte sur une bande intérieure de 12 px pour laisser voir la réfraction du bord,
// SANS toucher la zone du texte (marge intérieure 16 px) : le contraste du texte reste ≥ 4,5:1.
const SHEEN_DECL = `
uniform sampler2D tex;
uniform vec2 size;
uniform float spec_strength;
uniform float rim;
float ultima_hash(vec2 p) {
  return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}
`;
const SHEEN_CODE = `
vec2 uv = cogl_tex_coord_in[0].xy;
vec4 base = texture2D(tex, uv);
vec2 px = uv * size;
float diag = dot(uv, vec2(0.62, 0.38));
float spec = smoothstep(0.02, 0.20, diag) * (1.0 - smoothstep(0.20, 0.55, diag)) * spec_strength;
float top = (1.0 - smoothstep(1.0, 10.0, px.y)) * 0.06;
float grain = (ultima_hash(floor(px)) - 0.5) * 0.025;
float edge = min(min(px.x, px.y), min(size.x - px.x, size.y - px.y));
float rim_mask = rim * step(2.0, edge) * (1.0 - smoothstep(4.0, 12.0, edge));
vec4 c = base * (1.0 - 0.35 * rim_mask);
c.rgb = clamp(c.rgb + vec3(spec + top + grain) * c.a, 0.0, 1.0);
c.rgb = min(c.rgb, vec3(c.a));
cogl_color_out = cogl_color_in * c;
`;

// ---- loupe : réfraction en barillet sur les bords + aberration chromatique + masque arrondi ----
// Appliqué sur le conteneur découpé qui porte le Clutter.Clone grossi du jeu. Les échantillons sont
// tirés VERS le centre près du bord (jamais hors de la texture), donc aucun bord vide.
const LENS_DECL = `
uniform sampler2D tex;
uniform vec2 size;
uniform float radius;
uniform float strength;
uniform float aberration;
float ultima_sd_round(vec2 p, vec2 b, float r) {
  vec2 q = abs(p) - b + vec2(r);
  return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}
`;
const LENS_CODE = `
vec2 uv = cogl_tex_coord_in[0].xy;
vec2 px = uv * size;
float d = ultima_sd_round(px - size * 0.5, size * 0.5, radius);
float edge = 1.0 - smoothstep(0.0, 30.0, -d);
vec2 c = uv - 0.5;
vec2 bent = 0.5 + c * (1.0 - strength * edge * edge);
vec2 shift = c * aberration * edge;
vec4 g = texture2D(tex, bent);
float r = texture2D(tex, bent + shift).r;
float b = texture2D(tex, bent - shift).b;
float mask = 1.0 - smoothstep(-1.0, 0.5, d);
cogl_color_out = cogl_color_in * vec4(r, g.g, b, g.a) * mask;
`;

let SheenEffect = null;
let LensEffect = null;

function uniformSetter(effect, name, n) {
    const loc = effect.get_uniform_location(name);
    return values => effect.set_uniform_float(loc, n, values);
}

export function createSheenEffect({width, height, rim = 0}) {
    if (!SheenEffect) {
        SheenEffect = GObject.registerClass(
            class UltimaSheenEffect extends Shell.GLSLEffect {
                vfunc_build_pipeline() {
                    this.add_glsl_snippet(Cogl.SnippetHook.FRAGMENT, SHEEN_DECL, SHEEN_CODE, true);
                }
            });
    }
    const e = new SheenEffect();
    uniformSetter(e, 'size', 2)([width, height]);
    uniformSetter(e, 'spec_strength', 1)([0.07]);
    uniformSetter(e, 'rim', 1)([rim]);
    return e;
}

export function createLensEffect({width, height, radius = 10, strength = 0.07, aberration = 0.012}) {
    if (!LensEffect) {
        LensEffect = GObject.registerClass(
            class UltimaLensEffect extends Shell.GLSLEffect {
                vfunc_build_pipeline() {
                    this.add_glsl_snippet(Cogl.SnippetHook.FRAGMENT, LENS_DECL, LENS_CODE, true);
                }
            });
    }
    const e = new LensEffect();
    uniformSetter(e, 'size', 2)([width, height]);
    uniformSetter(e, 'radius', 1)([radius]);
    uniformSetter(e, 'strength', 1)([strength]);
    uniformSetter(e, 'aberration', 1)([aberration]);
    return e;
}

// Exposés pour les tests (compilation hors session impossible ; on vérifie au moins la forme).
export const SHADERS = Object.freeze({SHEEN_DECL, SHEEN_CODE, LENS_DECL, LENS_CODE});
