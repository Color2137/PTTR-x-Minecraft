// Send commands to the PTRCraft plugin's debug console (127.0.0.1:47347), one per argument.
//   node tools/ptrdev.mjs "scene" "tree PTTRPlayer 3"
import net from 'node:net';
const port = Number(process.env.PTRCRAFT_DEV_PORT || 47347);
const cmds = process.argv.slice(2);
const sock = net.connect(port, '127.0.0.1');
let buf = '', i = 0;
sock.setEncoding('utf8');
sock.on('connect', () => sock.write(cmds[0] + '\n'));
sock.on('data', d => {
	buf += d;
	let k;
	while ((k = buf.indexOf('\n.\n')) >= 0) {
		console.log('> ' + cmds[i] + '\n' + buf.slice(0, k));
		buf = buf.slice(k + 3);
		if (++i < cmds.length) sock.write(cmds[i] + '\n'); else sock.end();
	}
});
sock.on('error', e => { console.log('ERROR ' + e.message); process.exit(1); });
