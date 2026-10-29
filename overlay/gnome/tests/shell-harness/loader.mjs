// Chargeur Node : 'gi://St' -> module bouchon exportant stubs.gi.St par défaut ;
// 'resource:///org/gnome/shell/ui/main.js' et '.../extensions/extension.js' -> bouchons nommés.
const STUBS = new URL('./stubs.mjs', import.meta.url).href;

export async function resolve(specifier, context, next) {
    if (specifier.startsWith('gi://') || specifier.startsWith('resource:///'))
        return {url: `ultima-stub:${specifier}`, shortCircuit: true};
    return next(specifier, context);
}

export async function load(url, context, next) {
    if (!url.startsWith('ultima-stub:'))
        return next(url, context);
    const spec = url.slice('ultima-stub:'.length);
    let source;
    if (spec.startsWith('gi://')) {
        const name = spec.slice('gi://'.length).split('?')[0];
        source = `import {gi} from '${STUBS}'; if (!gi[${JSON.stringify(name)}]) throw new Error('bouchon gi manquant : ${name}'); export default gi[${JSON.stringify(name)}];`;
    } else if (spec.endsWith('/ui/main.js')) {
        source = `import {Main} from '${STUBS}'; export const layoutManager = Main.layoutManager; export const overview = Main.overview; export const wm = Main.wm;`;
    } else if (spec.endsWith('/extensions/extension.js')) {
        source = `import {Extension} from '${STUBS}'; export {Extension};`;
    } else {
        throw new Error(`ressource non bouchonnée : ${spec}`);
    }
    return {format: 'module', source, shortCircuit: true};
}
