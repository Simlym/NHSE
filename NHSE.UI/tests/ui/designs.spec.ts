import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
const text = (code: string) =>
  JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
async function openDesigns(page: Page) {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page
    .locator('.sidebar nav')
    .getByRole('button', { name: text('zhs').island, exact: true })
    .click();
  await page.getByRole('button', { name: text('zhs').designToolNames.split('|')[0], exact: true }).click();
  await expect(page.locator('.design-entry')).toHaveCount(100);
}

test('design gallery reaches every slot and renders all PRO sheets with correct channels and transparency', async ({
  page,
}) => {
  await openDesigns(page);
  await page.getByRole('searchbox', { name: text('zhs').designSearch, exact: true }).fill('patterns 100');
  await expect(page.locator('.design-entry')).toHaveCount(1);
  await page.locator('.design-entry').click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 100');
  await page.getByRole('button', { name: text('zhs').closeSave, exact: true }).click();
  await page.getByRole('button', { name: text('zhs').openSave, exact: true }).click();
  await page
    .locator('.sidebar nav')
    .getByRole('button', { name: text('zhs').designs, exact: true })
    .click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 1');

  await page
    .locator('.design-tabs')
    .getByRole('button', { name: text('zhs').designToolNames.split('|')[1], exact: true })
    .click();
  await expect(page.locator('.design-sheet')).toHaveCount(4);
  for (let sheet = 0; sheet < 4; sheet++) {
    const pixels = await page
      .locator('.design-sheet canvas')
      .nth(sheet)
      .evaluate((canvas) => {
        const context = (canvas as HTMLCanvasElement).getContext('2d')!;
        return {
          first: [...context.getImageData(0, 0, 1, 1).data],
          last: [...context.getImageData(31, 31, 1, 1).data],
        };
      });
    expect(pixels.first).toEqual([17 + sheet, 34 + sheet, 51 + sheet, 255]);
    expect(pixels.last[3]).toBe(sheet === 3 ? 0 : 255);
  }
  await expect(page.locator('.design-palette > span')).toHaveCount(16);
  await page.locator('.design-sheet').nth(3).click();
  await page.getByRole('button', { name: text('zhs').designSavePng, exact: true }).click();
  await page.getByRole('button', { name: text('zhs').designSavePalette, exact: true }).click();
  const png = await page.evaluate(() =>
    (
      window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }
    ).nhseTestPayloads
      .filter((x) => x.command === 'designPng')
      .map((x) => x.payload),
  );
  expect(png.map((p) => [p.kind, p.sheet, p.palette])).toEqual([
    ['proDesigns', 3, false],
    ['proDesigns', 3, true],
  ]);
  await page
    .locator('.design-tabs')
    .getByRole('button', { name: text('zhs').designToolNames.split('|')[2], exact: true })
    .click();
  await expect(page.locator('.design-entry')).toHaveCount(1);
  await expect(page.locator('.design-sheet')).toHaveCount(1);
  await page
    .locator('.design-tabs')
    .getByRole('button', { name: text('zhs').designToolNames.split('|')[3], exact: true })
    .click();
  await expect(page.locator('.design-entry')).toHaveCount(8);
  await page.setViewportSize({ width: 1180, height: 550 });
  const lastNavigation = page.locator('.sidebar nav button').last();
  await lastNavigation.scrollIntoViewIfNeeded();
  const navigationBounds = await lastNavigation.boundingBox();
  expect(navigationBounds!.y + navigationBounds!.height).toBeLessThanOrEqual(550);
  await page.setViewportSize({ width: 1180, height: 800 });
  await page.screenshot({ path: 'test-results/designs-tailor-zhs.png' });
});

test('design imports respect selected player, cancellation, batch confirmation and shared undo', async ({
  page,
}) => {
  await openDesigns(page);
  await page.locator('.player-chip').nth(1).click();
  await page.getByRole('combobox', { name: text('zhs').designOwnership, exact: true }).selectOption('true');
  await page.evaluate(() => Object.assign(window, { nhseDesignCancelNext: true }));
  await page.getByRole('button', { name: text('zhs').designImport, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 1');
  await expect(page.getByRole('button', { name: text('zhs').undo, exact: true })).toBeDisabled();
  await page.getByRole('button', { name: text('zhs').designImport, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('Imported 1');
  await expect(page.locator('.design-meta')).toContainText('小雨');
  await page.getByRole('button', { name: text('zhs').undo, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 1');
  await page.getByRole('button', { name: text('zhs').redo, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('Imported 1');
  await page.getByRole('button', { name: text('zhs').designImportAll, exact: true }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: text('zhs').cancel, exact: true })
    .click();
  expect(
    await page.evaluate(() =>
      (
        window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }
      ).nhseTestPayloads.some((x) => x.command === 'designImport' && x.payload.all),
    ),
  ).toBe(false);
  await page.getByRole('button', { name: text('zhs').designImportAll, exact: true }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: text('zhs').designChooseFolder, exact: true })
    .click();
  await page.locator('.design-entry').nth(1).click();
  await expect(page.locator('.design-detail h2')).toHaveText('Imported 2');
  await page.getByRole('button', { name: text('zhs').undo, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 2');
  await page.getByRole('button', { name: text('zhs').designExport, exact: true }).click();
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 2');
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.some(
        (x) => x === 'tool' || x === 'legacy',
      ),
    ),
  ).toBe(false);
});

test('design preferences survive language changes and failed imports leave previews unchanged', async ({
  page,
}) => {
  await openDesigns(page);
  await page.getByRole('combobox', { name: text('zhs').designOwnership, exact: true }).selectOption('true');
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(text(code).designs);
    await expect(page.getByRole('combobox', { name: text(code).designOwnership, exact: true })).toHaveValue(
      'true',
    );
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
  await page.evaluate(() => Object.assign(window, { nhseDesignErrorNext: 'designFileSize' }));
  await page.getByRole('button', { name: text('zht').designImport, exact: true }).click();
  await expect(page.getByRole('alert')).toContainText(text('zht').designFileSize);
  await expect(page.locator('.design-detail h2')).toHaveText('patterns 1');
  await expect(page.getByRole('button', { name: text('zht').undo, exact: true })).toBeDisabled();
});
