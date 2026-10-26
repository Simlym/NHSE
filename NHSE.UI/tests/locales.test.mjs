import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const base = new URL('../src/locales/', import.meta.url);
const english = JSON.parse(fs.readFileSync(new URL('en.json', base), 'utf8'));
for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
  test(`${code}: every UI and native dialog message is translated`, () => {
    const locale = JSON.parse(fs.readFileSync(new URL(`${code}.json`, base), 'utf8'));
    assert.equal(locale.villagerSpeciesNames.split('|').length, 35);
    assert.equal(locale.villagerPersonalityNames.split('|').length, 9);
    assert.equal(locale.airportColors.split('|').length, 4);
    assert.equal(locale.hemisphereNames.split('|').length, 2);
    assert.equal(locale.islandToolNames.split('|').length, 7);
    assert.equal(locale.designToolNames.split('|').length, 4);
    for (const [key, count] of Object.entries({ recordFieldNames: 26, collectionCategories: 6, mapSpawnSources: 4, mapArrangeNames: 3, mapParamNames: 24, mapBooleanNames: 2, mapPresetFamilies: 2, mapPresetCorners: 3 }))
      assert.equal(locale[key].split('|').length, count, key);
    assert.deepEqual(Object.keys(locale).sort(), Object.keys(english).sort());
    for (const [key, value] of Object.entries(locale))
      assert.ok(typeof value === 'string' && value.trim().length > 0, key);
  });
}
