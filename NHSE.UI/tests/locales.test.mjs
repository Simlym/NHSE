import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const base = new URL('../src/locales/', import.meta.url);
const english = JSON.parse(fs.readFileSync(new URL('en.json', base), 'utf8'));
for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
  test(`${code}: every UI and native dialog message is translated`, () => {
    const locale = JSON.parse(fs.readFileSync(new URL(`${code}.json`, base), 'utf8'));
    assert.deepEqual(Object.keys(locale).sort(), Object.keys(english).sort());
    for (const [key, value] of Object.entries(locale))
      assert.ok(typeof value === 'string' && value.trim().length > 0, key);
  });
}
