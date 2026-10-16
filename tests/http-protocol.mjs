import assert from 'node:assert/strict';
import net from 'node:net';
import {performance} from 'node:perf_hooks';

export async function checkHttpProtocol(origin, {slow = true} = {}) {
  const url = new URL(origin);
  assert.equal(url.hostname, '127.0.0.1', 'Protocol tests require the isolated loopback service');
  const host = url.host, results = [], failures = [];
  function exchange(request, drip = false) {
    return new Promise((resolve, reject) => {
      const socket = net.createConnection({host: url.hostname, port: Number(url.port)});
      const started = performance.now(), chunks = []; let timer;
      const timeout = setTimeout(() => socket.destroy(new Error('Protocol test exceeded 13 seconds')), 13000);
      socket.on('connect', () => {
        socket.write(request);
        if (drip) timer = setInterval(() => socket.write('a'), 150);
      });
      socket.on('data', data => chunks.push(data));
      socket.on('error', error => { if (!['ECONNRESET', 'EPIPE'].includes(error.code)) reject(error); });
      socket.on('close', () => {
        clearInterval(timer); clearTimeout(timeout);
        resolve({status: Number(Buffer.concat(chunks).toString('ascii').match(/^HTTP\/1\.1 (\d+)/)?.[1] || 0), elapsedMs: performance.now() - started});
      });
    });
  }
  const cases = [
    ['valid request', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\n\r\n`, 200],
    ['duplicate Host', `GET /api/health HTTP/1.1\r\nHost: other.example\r\nHost: ${host}\r\n\r\n`, 400],
    ['duplicate Origin', `GET /api/settings HTTP/1.1\r\nHost: ${host}\r\nX-Luna: 1\r\nOrigin: https://other.example\r\nOrigin: ${origin}\r\n\r\n`, 400],
    ['duplicate length', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\nContent-Length: 9\r\nContent-Length: 0\r\n\r\n`, 400],
    ['invalid HTTP version', `GET /api/health HTTP/9.9\r\nHost: ${host}\r\n\r\n`, 400],
    ['space before colon', `GET /api/health HTTP/1.1\r\nHost : ${host}\r\n\r\n`, 400],
    ['header without colon', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\nmalformed\r\n\r\n`, 400],
    ['folded header', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\n continuation\r\n\r\n`, 400],
    ['control character', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\nX-Test: a\u0000b\r\n\r\n`, 400],
    ['oversized header', `GET /api/health HTTP/1.1\r\nHost: ${host}\r\nX-Test: ${'a'.repeat(16400)}\r\n\r\n`, 400],
  ];
  for (const [name, request, expected] of cases) {
    const actual = await exchange(request);
    results.push({name, expected, ...actual});
    if (actual.status !== expected) failures.push(`${name}: expected ${expected}, received ${actual.status}`);
  }
  if (slow) {
    try {
      const actual = await exchange(`GET /api/health HTTP/1.1\r\nHost: ${host}\r\nX-Slow: `, true);
      results.push({name: 'absolute header deadline', ...actual});
      assert.ok(actual.elapsedMs < 12000, 'A trickle request retained its connection slot beyond the header deadline');
      assert.equal(actual.status, 408, 'The slow header was not timed out');
    } catch (error) { failures.push(error.message); }
  }
  console.log(JSON.stringify({protocolChecks: results, failures}));
  assert.equal(failures.length, 0, failures.join('\n'));
  return results;
}
if (process.argv[1]?.endsWith('http-protocol.mjs')) await checkHttpProtocol(process.argv[2], {slow: !process.argv.includes('--fast')});
