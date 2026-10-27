import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';
async function open(page: Page, section = '高级编辑') {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: section, exact: true }).click();
}
const writes = (page: Page) => page.evaluate(() => (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.filter(command => command === 'setAdvanced'));
const valueField = (page: Page) => page.locator('[data-field-key="Value"] input');
test('the footer exposes unapplied drafts and restores them without adding history', async ({ page }) => {
  await open(page);
  await valueField(page).fill('12');
  await expect(page.locator('.statusbar .status-text')).toHaveText('草稿待应用');
  await page.getByRole('button', { name: '还原本页草稿', exact: true }).click();
  await expect(valueField(page)).toHaveValue('0');
  await expect(page.locator('.statusbar .status-text')).toHaveText('修改已保存');
  expect(await writes(page)).toHaveLength(0);
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
});
test('auto-apply reports unsaved changes and undo/redo describe the last real edit', async ({ page }) => {
  await open(page); await valueField(page).fill('23');
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  await expect(page.locator('.toast')).toContainText('修改已应用，尚未保存');
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toHaveAttribute('title', /修改详情/);
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('button', { name: '重做', exact: true })).toHaveAttribute('title', /修改详情/);
  await expect(page.locator('.statusbar .status-text')).toHaveText('修改已保存');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(page.locator('.statusbar .status-text')).not.toHaveText('修改已保存');
});
test('advanced search and collapsed sections survive navigation, then clear for a new save session', async ({ page }) => {
  await open(page);
  await page.getByRole('searchbox', { name: '搜索条目', exact: true }).fill('1023');
  await page.locator('.advanced-entry').click();
  await page.getByRole('searchbox', { name: '搜索字段', exact: true }).fill('Value');
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '高级编辑', exact: true }).click();
  await expect(page.getByRole('searchbox', { name: '搜索条目', exact: true })).toHaveValue('1023');
  await expect(page.getByRole('searchbox', { name: '搜索字段', exact: true })).toHaveValue('Value');
  await page.getByRole('button', { name: '关闭存档', exact: true }).click();
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '高级编辑', exact: true }).click();
  await expect(page.getByRole('searchbox', { name: '搜索条目', exact: true })).toHaveValue('');
});
test('comfortable density persists, keeps nine-language navigation within the viewport', async ({ page }) => {
  await open(page, '岛屿概览');
  await page.getByRole('combobox', { name: '显示密度', exact: true }).selectOption('comfortable');
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
  for (const width of [1180, 800, 390]) {
    await page.setViewportSize({ width, height: 900 });
    for (const code of ['en','jp','de','es','fr','it','ko','zhs','zht']) {
      await page.locator('.icon-select select').first().selectOption(code);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
  }
  await page.reload(); await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable');
});
