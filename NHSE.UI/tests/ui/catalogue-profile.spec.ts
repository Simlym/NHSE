import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
async function open(page: Page, section: string) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: section, exact: true }).click();
}
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);

test('catalogue and variant flags reach the final row, apply drafts and undo across players', async ({ page }) => {
  await open(page, '玩家进度');
  await page.getByRole('button', { name: '物品图鉴', exact: true }).click();
  await page.getByRole('searchbox').fill('15999'); await page.locator('.progress-entry').click();
  const collected = page.locator('.progress-editor').getByRole('checkbox', { name: '已收集', exact: true });
  await collected.check();
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(collected).toHaveCount(0);
  await expect(page.locator('.progress-editor input[type=checkbox]')).toBeChecked();
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.getByRole('button', { name: '改造款式图鉴', exact: true }).click();
  expect((await payloads(page, 'setCollection')).at(-1)).toMatchObject({ section: 'collection', index: 15999, known: true });
  await page.getByRole('searchbox').fill('15999'); await page.locator('.progress-entry').click(); await collected.check();
  await page.locator('.player-chip').nth(1).click(); await expect(collected).not.toBeChecked();
  await page.locator('.player-chip').first().click();
  await page.getByRole('searchbox').fill('15999'); await page.locator('.progress-entry').click(); await expect(collected).toBeChecked();
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(collected).not.toBeChecked();
  await page.getByRole('button', { name: '重做', exact: true }).click(); await expect(collected).toBeChecked();
});

test('catalogue batch review freezes scope, cancels safely and sends variants-only as one action', async ({ page }) => {
  await open(page, '玩家进度'); await page.getByRole('button', { name: '物品图鉴', exact: true }).click();
  await page.getByRole('searchbox').fill('15999');
  await page.getByRole('combobox', { name: '批量范围', exact: true }).selectOption('furniture');
  await page.getByRole('checkbox', { name: '仅修改本体款式', exact: true }).check();
  await page.getByRole('button', { name: '标记已收集', exact: true }).click();
  await expect(page.locator('.collection-confirm')).toContainText('家具');
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  expect(await payloads(page, 'collectionBatch')).toHaveLength(0);
  await page.getByRole('button', { name: '标记已收集', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page, 'collectionBatch'))[0]).toMatchObject({ player: 0, category: 'furniture', remakeOnly: true, known: true });
  await page.locator('.progress-entry').click();
  await expect(page.locator('.progress-editor input[type=checkbox]')).not.toBeChecked();
  await page.getByRole('button', { name: '改造款式图鉴', exact: true }).click();
  await expect(page.locator('.progress-editor input[type=checkbox]')).toBeChecked();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.locator('.progress-editor input[type=checkbox]')).not.toBeChecked();
});

test('profile dates validate, apply before player switching, and join save and undo history', async ({ page }) => {
  await open(page, '玩家资料');
  const form = page.locator('.misc-profile');
  await form.getByRole('spinbutton', { name: '生日月份', exact: true }).fill('2');
  await form.getByRole('spinbutton', { name: '生日日期', exact: true }).fill('30');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(form.getByRole('alert')).toBeVisible(); expect(await payloads(page, 'setMiscProfile')).toHaveLength(0);
  await form.getByRole('spinbutton', { name: '生日日期', exact: true }).fill('29');
  await form.getByRole('textbox', { name: '资料日期', exact: true }).fill('2023-02-29');
  await page.locator('.player-chip').nth(1).click();
  expect(await payloads(page, 'setMiscProfile')).toHaveLength(0);
  await form.getByRole('textbox', { name: '资料日期', exact: true }).fill('2024-02-29');
  await form.getByRole('checkbox').check();
  await page.locator('.player-chip').nth(1).click();
  expect((await payloads(page, 'setMiscProfile')).at(-1)).toMatchObject({ player: 0, month: 2, day: 29, timestamp: '2024-02-29', madeVillage: true });
  await expect(form.getByRole('spinbutton', { name: '生日月份', exact: true })).toHaveValue('1');
  await page.locator('.player-chip').first().click();
  await expect(form.getByRole('spinbutton', { name: '生日月份', exact: true })).toHaveValue('2');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(form.getByRole('spinbutton', { name: '生日月份', exact: true })).toHaveValue('1');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(form.getByRole('spinbutton', { name: '生日月份', exact: true })).toHaveValue('2');
  await form.getByRole('textbox', { name: '资料日期', exact: true }).fill('');
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  expect((await payloads(page, 'setMiscProfile')).at(-1)).toMatchObject({ timestamp: '' });
});

test('new catalogue and profile forms remain localized and fit narrow screens', async ({ page }) => {
  await open(page, '玩家资料'); await page.setViewportSize({ width: 390, height: 900 });
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.locator('.misc-profile h2')).toHaveText(text.miscProfile);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.locator('.sidebar nav').getByRole('button', { name: text.progress, exact: true }).click();
    await page.getByRole('button', { name: text.progressCollection, exact: true }).click();
    await expect(page.getByRole('combobox', { name: text.collectionScope, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.locator('.sidebar nav').getByRole('button', { name: text.player, exact: true }).click();
  }
});
