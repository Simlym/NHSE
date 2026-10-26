import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
async function boot(page: Page, section?: string) {
  await mockHost(page); await page.goto('/');
  if (section) { await page.getByRole('button', { name: '打开存档', exact: true }).click(); await page.locator('.sidebar nav').getByRole('button', { name: section, exact: true }).click(); }
}
async function settings(page: Page) { await page.locator('.top-actions').getByRole('button', { name: '应用设置', exact: true }).click(); }

test('settings work before opening a save, validate drafts and persist exact normalized defaults', async ({ page }) => {
  await boot(page); await settings(page); const form = page.locator('.settings-page');
  await form.getByRole('checkbox', { name: '打开存档时自动备份', exact: true }).uncheck();
  await form.getByRole('spinbutton', { name: '端口', exact: true }).fill('0');
  await form.getByRole('button', { name: '保存设置', exact: true }).click(); expect(await payloads(page, 'setPreferences')).toHaveLength(0);
  await form.getByRole('spinbutton', { name: '端口', exact: true }).fill('65535');
  await form.getByRole('textbox', { name: '背包偏移（十六进制）', exact: true }).fill('ffffffff');
  await form.getByRole('textbox', { name: '通用偏移（十六进制）', exact: true }).fill('0');
  await form.getByRole('combobox', { name: '外观', exact: true }).selectOption('dark');
  await page.keyboard.press('Control+s');
  await expect(form.getByRole('button', { name: '保存设置', exact: true })).toBeDisabled();
  await expect(form.getByRole('textbox', { name: '背包偏移（十六进制）', exact: true })).toHaveValue('FFFFFFFF');
  expect((await payloads(page, 'setPreferences')).at(-1)).toMatchObject({ automaticBackup: false, backupPrompted: true, port: 65535, pouchOffset: 'ffffffff', genericOffset: '0', theme: 'dark' });
  expect(await page.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');
  expect(await payloads(page, 'save')).toHaveLength(0);
});

test('language and theme synchronization preserve unrelated settings drafts and restore failed selectors', async ({ page }) => {
  await boot(page); await settings(page); const form = page.locator('.settings-page');
  await form.getByRole('checkbox', { name: '打开存档时自动备份', exact: true }).uncheck();
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(form.getByRole('checkbox', { name: 'Automatically back up saves when opening', exact: true })).not.toBeChecked();
  await page.evaluate(() => Object.assign(window, { nhsePreferencesErrorNext: true }));
  await page.locator('.icon-select select').first().selectOption('jp');
  await expect(page.locator('.icon-select select').first()).toHaveValue('en');
  await page.evaluate(() => Object.assign(window, { nhsePreferencesErrorNext: true }));
  await page.locator('.theme-select select').selectOption('dark');
  await expect(page.locator('.theme-select select')).toHaveValue('system');
  await expect(form.getByRole('combobox', { name: 'Appearance', exact: true })).toHaveValue('system');
  await form.getByRole('button', { name: 'Discard draft', exact: true }).click();
  await expect(form.getByRole('checkbox', { name: 'Automatically back up saves when opening', exact: true })).toBeChecked();
  expect(await payloads(page, 'setPreferences')).toHaveLength(0);
});

test('settings apply on leaving, persistence failures retain drafts and never join save undo history', async ({ page }) => {
  await boot(page, '岛屿概览'); await settings(page); const form = page.locator('.settings-page');
  await form.getByRole('checkbox', { name: '打开存档时自动备份', exact: true }).uncheck();
  await page.evaluate(() => Object.assign(window, { nhsePreferencesErrorNext: true }));
  await form.getByRole('button', { name: '保存设置', exact: true }).click();
  await expect(form.getByRole('button', { name: '保存设置', exact: true })).toBeEnabled();
  await expect(form.getByRole('checkbox', { name: '打开存档时自动备份', exact: true })).not.toBeChecked();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await settings(page); await expect(form.getByRole('checkbox', { name: '打开存档时自动备份', exact: true })).not.toBeChecked();
});

test('save utilities review imports, preserve history on cancel and error, and report dirty memory hashes', async ({ page }) => {
  await boot(page, '岛屿工具');
  const undo = page.getByRole('button', { name: '撤销', exact: true });
  await page.getByRole('button', { name: '检查哈希', exact: true }).click();
  await expect(page.locator('.utility-hashes')).toContainText('全部哈希匹配'); await expect(undo).toBeDisabled();
  await page.getByRole('button', { name: '导出解密文件', exact: true }).click();
  await page.locator('.utility-photo').getByRole('combobox').selectOption('1');
  await page.getByRole('button', { name: '导出玩家照片 PNG', exact: true }).click();
  expect((await payloads(page, 'utilityExportPhoto'))[0]).toMatchObject({ player: 1 }); await expect(undo).toBeDisabled();
  const request = async () => { await page.getByRole('button', { name: '导入解密文件', exact: true }).click(); };
  await request(); await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  expect(await payloads(page, 'utilityImportDecrypted')).toHaveLength(0);
  await page.evaluate(() => Object.assign(window, { nhseUtilityCancelNext: true })); await request();
  await page.getByRole('dialog').getByRole('button', { name: '选择文件', exact: true }).click(); await expect(undo).toBeDisabled();
  await page.evaluate(() => Object.assign(window, { nhseUtilityErrorNext: true })); await request();
  await page.getByRole('dialog').getByRole('button', { name: '选择文件', exact: true }).click(); await expect(undo).toBeDisabled();
  await expect(page.getByRole('alert')).toContainText('文件大小或文件集合');
  await request(); await page.getByRole('dialog').getByRole('button', { name: '选择文件', exact: true }).click(); await expect(undo).toBeEnabled();
  await page.getByRole('button', { name: '检查哈希', exact: true }).click();
  await expect(page.locator('.utility-hashes')).toContainText('未保存的修改'); await expect(page.locator('.utility-hashes')).toContainText('main.dat');
  await undo.click(); await expect(undo).toBeDisabled(); await expect(page.locator('.utility-hashes')).toHaveCount(0);
  await page.getByRole('button', { name: '检查哈希', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '背包与仓库', exact: true }).click();
  await page.locator('.item-slot').first().click(); await page.getByRole('spinbutton', { name: '数量', exact: true }).fill('20');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿工具', exact: true }).click();
  await expect(page.locator('.utility-hashes')).toHaveCount(0);
  await page.locator('.task-grid').getByRole('button', { name: '地图与建筑', exact: true }).click();
  await expect(page.locator('.map-page')).toBeVisible();
});

test('house flags and all-house exports use the selected scope, apply drafts first and undo file changes', async ({ page }) => {
  await boot(page, '房屋与岛屿生活'); await page.getByRole('combobox', { name: '选择房屋', exact: true }).selectOption('7');
  await page.locator('.housing-settings').getByRole('spinbutton', { name: '房屋等级', exact: true }).fill('7');
  await page.getByRole('button', { name: '导出本类别全部房屋', exact: true }).click();
  expect((await payloads(page, 'setHousing')).at(-1)).toMatchObject({ index: 7, values: { HouseLevel: 7 } });
  expect((await payloads(page, 'housingExportAll')).at(-1)).toMatchObject({ kind: 'playerHouses', index: 7 });
  await page.getByRole('button', { name: '导出房屋标记', exact: true }).click();
  await page.evaluate(() => Object.assign(window, { nhseUtilityCancelNext: true })); await page.getByRole('button', { name: '导入房屋标记', exact: true }).click();
  await page.getByRole('button', { name: '导入房屋标记', exact: true }).click();
  await page.locator('.housing-flags summary').click();
  const flag = page.locator('.housing-flags input'); await expect(flag).toHaveValue('-32768');
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(flag).toHaveValue('0');
  await expect(page.locator('.housing-settings').getByRole('spinbutton', { name: '房屋等级', exact: true })).toHaveValue('7');
  await page.getByRole('button', { name: '居民房屋', exact: true }).click();
  await expect(page.getByRole('button', { name: '导入房屋标记', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: '导出本类别全部房屋', exact: true }).click();
  expect((await payloads(page, 'housingExportAll')).at(-1)).toMatchObject({ kind: 'residentHouses' });
});

test('settings and new utility controls stay localized and fit narrow screens', async ({ page }) => {
  await boot(page, '岛屿工具'); await page.setViewportSize({ width: 390, height: 900 });
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await page.locator('.top-actions').getByRole('button', { name: text.settings, exact: true }).click();
    await expect(page.locator('.settings-page').getByRole('button', { name: text.preferencesSave, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.locator('.sidebar nav').getByRole('button', { name: text.tools, exact: true }).click();
    await expect(page.getByRole('button', { name: text.utilityExportDecrypted, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
