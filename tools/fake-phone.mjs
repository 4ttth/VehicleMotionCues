// Pretends to be the iPhone page so you can test the laptop side without driving anywhere.
// The phone is simulated lying flat, top toward the front of the car.
//
//   node tools/fake-phone.mjs <pairingKey> [ws|post] [seconds] [ax] [ay]
//
// ax = sideways acceleration in m/s² (+ = car turning right), ay = forward (+ = speeding up).
// The pairing key is in %APPDATA%\VehicleMotionCues\settings.json.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'; // the bridge uses a self-signed certificate
const [key, mode = 'ws', secs = '6', ax = '0', ay = '2.5'] = process.argv.slice(2);
if (!key) {
  console.error('usage: node tools/fake-phone.mjs <pairingKey> [ws|post] [seconds] [ax] [ay]');
  process.exit(1);
}
const base = 'https://localhost:47800';

const page = await fetch(base + '/');
console.log('GET / ->', page.status);

const sample = t => [t, Number(ax), Number(ay), 0, 0, 0, 1];
const end = Date.now() + Number(secs) * 1000;
if (mode === 'ws') {
  const ws = new WebSocket(`wss://localhost:47800/ws?k=${key}`);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  console.log('WebSocket open, streaming…');
  while (Date.now() < end) { ws.send(JSON.stringify(sample(performance.now()))); await new Promise(r => setTimeout(r, 16)); }
  ws.close();
} else {
  console.log('Posting batches…');
  while (Date.now() < end) {
    const batch = Array.from({ length: 6 }, (_, i) => sample(performance.now() + i * 16));
    const r = await fetch(`${base}/m?k=${key}`, { method: 'POST', body: JSON.stringify(batch) });
    if (r.status !== 204) console.log('POST ->', r.status);
    await new Promise(r => setTimeout(r, 100));
  }
}
console.log('done');
