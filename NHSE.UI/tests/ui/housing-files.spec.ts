import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
async function openHousing(page: Page) {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '房屋与岛屿生活', exact: true }).click();
  await expect(page.locator('.housing-tile')).toHaveCount(400);
}
const payloads = (page: Page) => page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads);

test('house and room imports preserve targets, apply drafts first and undo independently', async ({ page }) => {
  await openHousing(page);
  const level = page.locator('.housing-settings').getByRole('spinbutton', { name: '房屋等级', exact: true });
  await level.fill('7');
  await page.getByRole('button', { name: '导出房屋', exact: true }).click();
  const messages = await payloads(page);
  expect(messages.findIndex(m => m.command === 'setHousing')).toBeLessThan(messages.findIndex(m => m.command === 'housingExport'));
  expect(messages.find(m => m.command === 'housingExport')!.payload).toMatchObject({ kind: 'playerHouses', index: 0, room: 0, roomOnly: false });
  await page.getByRole('button', { name: '导入房屋', exact: true }).click();
  await expect(level).toHaveValue('9');
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(level).toHaveValue('7');
  await page.getByRole('combobox', { name: '房间', exact: true }).selectOption('5');
  await page.getByRole('button', { name: '导入当前房间', exact: true }).click();
  expect((await payloads(page)).find(m => m.command === 'housingImport' && m.payload.roomOnly)!.payload).toMatchObject({ room: 5, index: 0 });
  await page.getByRole('combobox', { name: '图层', exact: true }).selectOption('7');
  await expect(page.locator('.housing-tile.empty')).toHaveCount(399);
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.locator('.housing-tile.empty')).toHaveCount(400);
  await page.getByRole('button', { name: '居民房屋', exact: true }).click();
  await expect(page.getByRole('button', { name: '导入当前房间', exact: true })).toHaveCount(0);
  await page.getByRole('combobox', { name: '选择房屋', exact: true }).selectOption('9');
  await page.getByRole('button', { name: '导入房屋', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'housingImport').at(-1)!.payload).toMatchObject({ kind: 'residentHouses', index: 9, roomOnly: false });
});

test('file cancellation and invalid files preserve history; export is read-only', async ({ page }) => {
  await openHousing(page);
  const undo = page.getByRole('button', { name: '撤销', exact: true });
  await expect(undo).toBeDisabled();
  await page.getByRole('button', { name: '导出当前房间', exact: true }).click(); await expect(undo).toBeDisabled();
  await page.evaluate(() => Object.assign(window, { nhseHousingCancelNext: true }));
  await page.getByRole('button', { name: '导入房屋', exact: true }).click(); await expect(undo).toBeDisabled();
  await page.evaluate(() => Object.assign(window, { nhseHousingErrorNext: 'housingFileSize' }));
  await page.getByRole('button', { name: '导入当前房间', exact: true }).click();
  await expect(page.getByText('文件大小不符合兼容的房屋或房间格式。', { exact: true })).toBeVisible();
  await expect(undo).toBeDisabled(); await expect(page.locator('.housing-tile.empty')).toHaveCount(400);
});

test('file controls stay translated and usable on narrow screens and invalid drafts block file operations', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 }); await openHousing(page);
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('button', { name: text.housingImportHouse, exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: text.housingExportRoom, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.locator('.housing-settings').getByRole('spinbutton', { name: '房屋等级', exact: true }).fill('-1');
  await page.getByRole('button', { name: '导出房屋', exact: true }).click();
  expect((await payloads(page)).filter(m => ['housingImport', 'housingExport'].includes(m.command))).toHaveLength(0);
});
