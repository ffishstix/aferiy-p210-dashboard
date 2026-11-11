import test from 'node:test';
import assert from 'node:assert/strict';
import { constants, createHash, createPublicKey, verify } from 'node:crypto';
import { readFileSync } from 'node:fs';

const page = readFileSync(new URL('../public/download.html', import.meta.url), 'utf8');
const manifest = JSON.parse(readFileSync(new URL('../public/updates/latest.json', import.meta.url), 'utf8'));

test('all advertised Windows binaries exist, match their architecture and published checksum', () => {
  for (const [name, machine] of [['win-x64', 0x8664], ['win-arm64', 0xaa64], ['win-x86', 0x014c]]) {
    const bytes = readFileSync(new URL(`../public/downloads/${name}/BatteryWatch.exe`, import.meta.url));
    const header = bytes.readUInt32LE(0x3c);
    assert.equal(bytes.readUInt16LE(header + 4), machine);
    const hash = createHash('sha256').update(bytes).digest('hex').toUpperCase();
    assert.ok(page.includes(`/downloads/${name}/BatteryWatch.exe`));
    assert.ok(page.includes(hash));
    assert.equal(manifest.sha256[name], hash);
  }
});

test('update manifest is signed by the pinned release key and matches the app version', () => {
  const project = readFileSync(new URL('../../desktop/BatteryWatch.csproj', import.meta.url), 'utf8');
  assert.equal(manifest.version, project.match(/<Version>([^<]+)<\/Version>/)?.[1]);
  const canonical = `${manifest.version}\n${['win-x64', 'win-arm64', 'win-x86'].map(name => `${name}:${manifest.sha256[name]}\n`).join('')}`;
  const key = createPublicKey(readFileSync(new URL('../../desktop/update-public.pem', import.meta.url), 'utf8'));
  assert.equal(verify('sha256', Buffer.from(canonical), { key, padding: constants.RSA_PKCS1_PSS_PADDING, saltLength: constants.RSA_PSS_SALTLEN_DIGEST }, Buffer.from(manifest.signature, 'base64')), true);
});
