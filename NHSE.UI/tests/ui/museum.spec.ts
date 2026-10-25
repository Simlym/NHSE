import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
async function openMuseum(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '博物馆', exact: true }).click();
  await expect(page.locator('.museum-entry')).toHaveCount(60);
}
const payloads = (page: Page) => page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads);
test('museum drafts keep full item parameters through language changes and apply on selection and navigation', async ({ page }) => {
  await openMuseum(page);
  const form = page.locator('.museum-detail');
  await form.getByLabel('捐赠日期', { exact: true }).fill('2026-10-25');
  await form.getByRole('spinbutton', { name: '捐赠者索引（0–255）', exact: true }).fill('255');
  await form.getByRole('spinbutton', { name: '自由参数', exact: true }).fill('-2147483648');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(form.getByLabel('Donation date', { exact: true })).toHaveValue('2026-10-25');
  await expect(form.getByRole('spinbutton', { name: 'Free parameter', exact: true })).toHaveValue('-2147483648');
  await page.locator('.museum-entry').nth(1).click();
  expect((await payloads(page)).filter(m => m.command === 'setMuseumEntry')[0]!.payload).toMatchObject({ index: 0, date: '2026-10-25', donor: 255, item: { freeParam: -2147483648 } });
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await page.locator('.museum-entry').first().click();
  await expect(form.getByLabel('Donation date', { exact: true })).toHaveValue('2024-02-29');
  await form.getByLabel('Donor index (0–255)', { exact: true }).fill('12');
  await page.locator('.sidebar nav').getByRole('button', { name: 'Island overview', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'setMuseumEntry').at(-1)!.payload).toMatchObject({ donor: 12 });
});

test('museum search reaches the last slot, item gallery and validation protect drafts', async ({ page }) => {
  await openMuseum(page);
  await page.getByRole('searchbox', { name: '搜索记录、物品、日期或捐赠者', exact: true }).fill('1023');
  await expect(page.locator('.museum-entry')).toHaveCount(1); await page.locator('.museum-entry').click();
  const form = page.locator('.museum-detail');
  await form.getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '木材', exact: true }).click();
  await form.getByRole('spinbutton', { name: '捐赠者索引（0–255）', exact: true }).fill('256');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'setMuseumEntry')).toHaveLength(0);
  await form.getByRole('spinbutton', { name: '捐赠者索引（0–255）', exact: true }).fill('0');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'setMuseumEntry')[0]!.payload).toMatchObject({ index: 1023, date: '', item: { itemId: 1000 } });
  await form.getByRole('button', { name: '清空记录', exact: true }).click();
  await expect(form.getByRole('spinbutton', { name: '物品编号', exact: true })).toHaveValue('65534');
});

test('museum batches confirm whole-island scope, cancel and undo, file errors and exports preserve history', async ({ page }) => {
  await openMuseum(page);
  await page.getByRole('button', { name: '导出博物馆', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: '补齐缺失捐赠', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'museumBatch')).toHaveLength(0);
  await page.getByRole('button', { name: '补齐缺失捐赠', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page)).filter(m => m.command === 'museumBatch')[0]!.payload).toMatchObject({ action: 'giveAll' });
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await page.evaluate(() => Object.assign(window, { nhseMuseumCancelNext: true }));
  await page.getByRole('button', { name: '导入博物馆', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await page.evaluate(() => Object.assign(window, { nhseMuseumErrorNext: 'museumFileSize' }));
  await page.getByRole('button', { name: '导入博物馆', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.getByText('博物馆文件必须恰好为 13,316 字节。', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
});

test('museum controls translate in nine languages at compact width and level edits share undo', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 }); await openMuseum(page);
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('button', { name: text.museumImport, exact: true })).toBeVisible();
    await expect(page.locator('.museum-detail').getByLabel(text.museumDate, { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.getByRole('spinbutton', { name: '博物馆等级原始值', exact: true }).fill('-2147483648');
  await page.locator('.museum-level').getByRole('button', { name: '应用修改', exact: true }).click();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('spinbutton', { name: '博物馆等级原始值', exact: true })).toHaveValue('2');
});

test('editing a donor preserves unusual stored dates instead of clearing the sanitized date input', async ({ page }) => {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.evaluate(() => Object.assign(window, { nhseMuseumRawDateNext: true }));
  await page.locator('.sidebar nav').getByRole('button', { name: '博物馆', exact: true }).click();
  await page.locator('.museum-entry').nth(7).click();
  const form = page.locator('.museum-detail');
  await expect(form.getByLabel('捐赠日期', { exact: true })).toHaveValue('');
  await expect(form.getByText(/65535-255-255/)).toBeVisible();
  await form.getByRole('spinbutton', { name: '捐赠者索引（0–255）', exact: true }).fill('9');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page)).find(m => m.command === 'setMuseumEntry')!.payload).toMatchObject({ index: 7, date: '65535-255-255', donor: 9 });
});
