// node --import ./overlay/gnome/tests/shell-harness/register.mjs … : redirige gi:// et resource://
// vers des bouchons (stubs.mjs). Aucun GNOME Shell, aucune session, aucun D-Bus.
import {register} from 'node:module';
register('./loader.mjs', import.meta.url);
