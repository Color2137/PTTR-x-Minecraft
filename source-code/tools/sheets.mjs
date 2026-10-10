// Design sheets: the source of truth. Preflight lays every sheet over the others (every cell filled,
// every reference resolves, every system exists, every PTR member still exists in PTR's metadata)
// and gen turns rows into C# (one piece of code per row).
//   node tools/sheets.mjs preflight            check before a build (lists unverified rows)
//   node tools/sheets.mjs preflight --release  also fail on any unverified row
//   node tools/sheets.mjs gen                  write ptr-plugin/src/Gen/*.g.cs
// PTR's metadata dump (Il2CppDumper dump.cs) is read from PTRCRAFT_DUMP or %TEMP%/ptrmc/dump/dump.cs.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sheetDir = path.join(root, 'sheets');
const sheets = {};
for (const f of fs.readdirSync(sheetDir).filter(f => f.endsWith('.json')).sort()) {
	const s = JSON.parse(fs.readFileSync(path.join(sheetDir, f), 'utf8'));
	sheets[s.sheet] = s;
}
const rows = name => sheets[name]?.rows ?? [];
const ids = name => new Set(rows(name).map(r => r.id));
const EMPTY = new Set(['todo', 'tbd', '?', '...']);
const isEmpty = (v, col) => v === null || v === undefined || (typeof v === 'string' && (EMPTY.has(v.trim().toLowerCase()) || (v.trim() === '' && col !== 'default')));
const list = v => (typeof v === 'string' && v.trim() !== '-') ? v.split(',').map(x => x.trim()).filter(Boolean) : [];
const srcFiles = dir => fs.existsSync(dir) ? fs.readdirSync(dir, { recursive: true }).filter(f => f.endsWith('.cs')).map(f => fs.readFileSync(path.join(dir, f), 'utf8')).join('\n') : '';

// ---- PTR metadata (dump.cs) ---------------------------------------------------------------
function loadDump() {
	const p = process.env.PTRCRAFT_DUMP || path.join(os.tmpdir(), 'ptrmc', 'dump', 'dump.cs');
	if (!fs.existsSync(p)) return null;
	const text = fs.readFileSync(p, 'utf8');
	const classes = new Map(); // name -> [{body, base}] (several assemblies can have a class of the same name)
	// top-level classes only: "class Name : Base, IFace // TypeDefIndex: n" (not nested "Name.<Start>d__12")
	const re = /^(?:public |internal |private |protected )?(?:sealed |abstract |static )*class ([A-Za-z0-9_]+)(?: : ([A-Za-z0-9_.]+))?(?:, [^\n]*)? \/\/ TypeDefIndex[^\n]*\n\{/gm;
	let m;
	while ((m = re.exec(text))) {
		const start = m.index + m[0].length;
		const end = text.indexOf('\n}\n', start);
		if (!classes.has(m[1])) classes.set(m[1], []);
		classes.get(m[1]).push({ body: text.slice(start, end), base: m[2] });
	}
	return { path: p, classes };
}

function memberExists(dump, cls, member, depth = 0) {
	const ks = dump.classes.get(cls);
	if (!ks) return depth > 0 ? false : null;
	if (depth >= 8) return false;
	const w = new RegExp(`[ .]${member}(?:[ ;(<]|$)|get_${member}\\(`, 'm');
	// any class of that name (UnityEngine.Component and System.ComponentModel.Component, ...), then their bases
	for (const k of ks) if (w.test(k.body)) return true;
	for (const k of ks) if (k.base && memberExists(dump, k.base.split('.').pop(), member, depth + 1)) return true;
	return false;
}

function preflight(release) {
	const errors = [], unverified = [], notes = [];
	for (const [name, s] of Object.entries(sheets)) {
		const cols = Object.keys(s.columns);
		const seen = new Set();
		s.rows.forEach((r, i) => {
			const where = `${name}[${r.id ?? '#' + i}]`;
			if (seen.has(r.id)) errors.push(`${where}: duplicate id`);
			seen.add(r.id);
			for (const c of cols) if (isEmpty(r[c], c)) errors.push(`${where}.${c}: empty`);
			for (const k of Object.keys(r)) if (!cols.includes(k)) errors.push(`${where}.${k}: column not declared`);
			if (r.verified === 'no') unverified.push(where);
		});
	}
	// references between sheets
	const sys = ids('systems'), chans = ids('link_channels');
	for (const r of rows('ptr_hooks')) if (!sys.has(r.system)) errors.push(`ptr_hooks[${r.id}].system: '${r.system}' is not a systems id`);
	const used = new Set();
	for (const r of rows('systems')) for (const c of list(r.channels)) {
		if (!chans.has(c)) errors.push(`systems[${r.id}].channels: '${c}' is not a link_channels id`);
		used.add(c);
	}
	for (const c of chans) if (!used.has(c)) errors.push(`link_channels[${c}]: no system uses it`);
	const hooked = new Set(rows('ptr_hooks').map(r => r.system));
	// every system's file exists
	for (const r of rows('systems')) if (!fs.existsSync(path.join(root, r.file))) errors.push(`systems[${r.id}].file: ${r.file} missing (unimplemented)`);
	// every setting and combat rule is used by the plugin
	const src = srcFiles(path.join(root, 'ptr-plugin', 'src'));
	for (const r of rows('settings')) if (!new RegExp(`Settings\\.${r.id}\\b`).test(src.replace(/\/\/ <generated>[\s\S]*?\/\/ <\/generated>/g, ''))) errors.push(`settings[${r.id}]: not used by the plugin`);
	for (const r of rows('combat_rules')) if (!new RegExp(`CombatRules\\.${r.id}\\b`).test(src)) errors.push(`combat_rules[${r.id}]: not used by the plugin`);
	// every PTR member still exists in this PTR version
	const dump = loadDump();
	if (!dump) notes.push('PTR metadata dump not found: PTR members not checked (run Il2CppDumper, see MODLOG)');
	else {
		for (const r of rows('ptr_hooks')) {
			const [cls, member] = r.member.split('.');
			const ok = memberExists(dump, cls, member);
			if (ok === null) errors.push(`ptr_hooks[${r.id}].member: class ${cls} not in PTR's metadata`);
			else if (!ok) errors.push(`ptr_hooks[${r.id}].member: ${r.member} not in PTR's metadata`);
		}
	}
	for (const n of notes) console.log('NOTE   ' + n);
	for (const e of errors) console.log('ERROR  ' + e);
	for (const u of unverified) console.log((release ? 'ERROR  ' : 'UNVERIFIED ') + u);
	const total = Object.values(sheets).reduce((n, s) => n + s.rows.length * Object.keys(s.columns).length, 0);
	console.log(`${Object.keys(sheets).length} sheets, ${total} cells, ${errors.length} errors, ${unverified.length} unverified rows${dump ? ', PTR members checked against ' + dump.path : ''}`);
	return errors.length === 0 && (!release || unverified.length === 0);
}

// ---- code generation ----------------------------------------------------------------------
const cs = s => '"' + String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
const head = name => `// <generated> by tools/sheets.mjs from sheets/${name}.json. Edit the sheet, not this file.\n`;

function gen() {
	const out = path.join(root, 'ptr-plugin', 'src', 'Gen');
	fs.mkdirSync(out, { recursive: true });
	for (const f of fs.readdirSync(out)) if (f.endsWith('.g.cs')) fs.rmSync(path.join(out, f));

	// settings.json -> one typed ConfigEntry + Bind per row
	{
		const t = { bool: 'bool', int: 'int', float: 'float', string: 'string' };
		const lit = r => r.type === 'string' ? cs(r.default) : r.type === 'float' ? `${Number(r.default)}f` : r.type === 'bool' ? String(r.default === 'true') : String(parseInt(r.default, 10));
		let c = head('settings') + 'using BepInEx.Configuration;\n\nnamespace PTRCraft\n{\n\t/// <summary>BepInEx/config/dev.ptrcraft.cfg (sheets/settings.json).</summary>\n\tinternal static class Settings\n\t{\n';
		for (const r of rows('settings')) c += `\t\t/// <summary>${r.effect}</summary>\n\t\tpublic static ConfigEntry<${t[r.type]}> ${r.id};\n`;
		c += '\n\t\tpublic static void Load(ConfigFile c)\n\t\t{\n';
		for (const r of rows('settings')) c += `\t\t\t${r.id} = c.Bind(${cs(r.section)}, ${cs(r.key)}, ${lit(r)}, ${cs(r.effect)});\n`;
		c += '\t\t}\n\t}\n}\n// </generated>\n';
		fs.writeFileSync(path.join(out, 'Settings.g.cs'), c);
	}
	// combat_rules.json -> one constant per row
	{
		let c = head('combat_rules') + 'namespace PTRCraft\n{\n\tinternal static class CombatRules\n\t{\n';
		for (const r of rows('combat_rules')) c += `\t\t/// <summary>${r.rule} (${r.unit})</summary>\n\t\tpublic const float ${r.id} = ${Number(r.value)}f;\n`;
		c += '\t}\n}\n// </generated>\n';
		fs.writeFileSync(path.join(out, 'CombatRules.g.cs'), c);
	}
	// ptr_hooks.json -> one entry per row, checked against the running PTR at startup
	{
		let c = head('ptr_hooks') + 'namespace PTRCraft\n{\n\tinternal static class PtrHooks\n\t{\n\t\t/// <summary>(id, class, member, kind): everything the plugin touches in PTR.</summary>\n\t\tpublic static readonly (string id, string cls, string member, string kind)[] All =\n\t\t{\n';
		for (const r of rows('ptr_hooks')) {
			const [cls, member] = r.member.split('.');
			c += `\t\t\t(${cs(r.id)}, ${cs(cls)}, ${cs(member)}, ${cs(r.kind)}),\n`;
		}
		c += '\t\t};\n\t}\n}\n// </generated>\n';
		fs.writeFileSync(path.join(out, 'PtrHooks.g.cs'), c);
	}
	// input_map.json -> the routing table the debug console prints
	{
		let c = head('input_map') + 'namespace PTRCraft\n{\n\tinternal static class InputMap\n\t{\n\t\tpublic static readonly (string id, string input, string when, string goesTo)[] All =\n\t\t{\n';
		for (const r of rows('input_map')) c += `\t\t\t(${cs(r.id)}, ${cs(r.input)}, ${cs(r.when)}, ${cs(r.goesTo)}),\n`;
		c += '\t\t};\n\t}\n}\n// </generated>\n';
		fs.writeFileSync(path.join(out, 'InputMap.g.cs'), c);
	}
	console.log('generated ' + fs.readdirSync(out).filter(f => f.endsWith('.g.cs')).join(', '));
}

const [cmd, flag] = process.argv.slice(2);
if (cmd === 'gen') gen();
else if (cmd === 'preflight') process.exit(preflight(flag === '--release') ? 0 : 1);
else {
	console.log('usage: node tools/sheets.mjs preflight [--release] | gen');
	process.exit(2);
}
