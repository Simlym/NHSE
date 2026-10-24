import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
const text = (code: string) =>
  JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
async function openHousing(page: import('@playwright/test').Page) {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  await page.getByRole('button', { name: '玩家房屋', exact: true }).click();
  await expect(page.locator('.housing-tile')).toHaveCount(400);
}

test('house fields and signed flags apply on navigation, survive language changes and undo', async ({
  page,
}) => {
  await openHousing(page);
  const settings = page.locator('.housing-settings');
  await settings.getByRole('spinbutton', { name: '房屋等级', exact: true }).fill('7');
  await settings.getByRole('spinbutton', { name: '房贷', exact: true }).fill('4294967295');
  await settings.locator('.housing-flags summary').click();
  await settings.getByRole('spinbutton', { name: '房屋事件标记 #127', exact: true }).fill('-32768');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(settings.getByRole('spinbutton', { name: 'House level', exact: true })).toHaveValue('7');
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.getByRole('combobox', { name: '选择房屋', exact: true }).selectOption('1');
  await expect(settings.getByRole('spinbutton', { name: '房屋等级', exact: true })).toHaveValue('1');
  await page.getByRole('combobox', { name: '选择房屋', exact: true }).selectOption('0');
  await expect(settings.getByRole('spinbutton', { name: '房屋等级', exact: true })).toHaveValue('7');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(settings.getByRole('spinbutton', { name: '房贷', exact: true })).toHaveValue('100');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(settings.getByRole('spinbutton', { name: '房屋事件标记 #127', exact: true })).toHaveValue(
    '-32768',
  );
});

test('room grid uses the item gallery, preserves raw parameters and separates six rooms and eight layers', async ({
  page,
}) => {
  await openHousing(page);
  const editor = page.locator('.housing-tile-editor');
  await page.locator('.housing-tile').nth(21).click();
  await editor.getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '木材', exact: true }).click();
  await editor.getByRole('spinbutton', { name: '系统参数', exact: true }).fill('129');
  await editor.getByRole('spinbutton', { name: '自由参数', exact: true }).fill('-2147483648');
  await editor.getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.locator('.housing-tile').nth(21)).not.toHaveClass(/empty/);
  await page.getByRole('combobox', { name: '图层', exact: true }).selectOption('7');
  await expect(page.locator('.housing-tile').nth(21)).toHaveClass(/empty/);
  await page.getByRole('combobox', { name: '图层', exact: true }).selectOption('0');
  await expect(editor.getByRole('spinbutton', { name: '系统参数', exact: true })).toHaveValue('129');
  await page.getByRole('combobox', { name: '房间', exact: true }).selectOption('5');
  await expect(page.locator('.housing-tile').nth(21)).toHaveClass(/empty/);
  await page.getByRole('combobox', { name: '房间', exact: true }).selectOption('0');
  await editor.getByRole('button', { name: '移除此物品', exact: true }).click();
  await expect(page.locator('.housing-tile').nth(21)).toHaveClass(/empty/);
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('spinbutton', { name: '自由参数', exact: true })).toHaveValue('-2147483648');
  await page.locator('.housing-room').evaluate((el) => el.scrollIntoView({ block: 'start' }));
  await page.screenshot({ path: 'test-results/housing-room-zhs.png' });
  for (const width of [1180, 800, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
});

test('campsite and fruit forms retain dates, validate fields and isolate player profiles in every language', async ({
  page,
}) => {
  await openHousing(page);
  await page.getByRole('button', { name: '露营地', exact: true }).click();
  const form = page.locator('.housing-settings');
  await expect(form.getByRole('textbox', { name: '访问日期', exact: true })).toHaveValue('2020-03-20');
  await form.getByRole('checkbox', { name: '营地有访客', exact: true }).check();
  await form.getByRole('combobox', { name: '访客', exact: true }).selectOption('4');
  await form.getByRole('textbox', { name: '访问日期', exact: true }).fill('invalid');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('setHousing'),
    ),
  ).toBe(false);
  await form.getByRole('textbox', { name: '访问日期', exact: true }).fill('2020-03-20');
  await page.getByRole('button', { name: '果树花卉', exact: true }).click();
  await form.getByRole('combobox', { name: '玩家资料水果', exact: true }).selectOption('2214');
  await form.getByRole('combobox', { name: '岛屿特产水果', exact: true }).selectOption('2287');
  await page.locator('.player-chip').nth(1).click();
  await expect(form.getByRole('combobox', { name: '玩家资料水果', exact: true })).toHaveValue('2287');
  await page.locator('.player-chip').first().click();
  await expect(form.getByRole('combobox', { name: '玩家资料水果', exact: true })).toHaveValue('2214');
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(text(code).housing);
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
});

test('resident house shortcut follows the assigned house slot and opens no native editor', async ({
  page,
}) => {
  await openHousing(page);
  await page.locator('.sidebar nav').getByRole('button', { name: '岛民管理', exact: true }).click();
  await page.getByRole('button', { name: '编辑岛民房屋', exact: true }).click();
  await expect(page.getByRole('combobox', { name: '选择房屋', exact: true })).toHaveValue('5');
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.some(
        (x) => x === 'tool' || x === 'legacy',
      ),
    ),
  ).toBe(false);
});
