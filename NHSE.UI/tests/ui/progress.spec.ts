import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
const text = (code: string) =>
  JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));

async function openProgress(page: import('@playwright/test').Page) {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '玩家进度', exact: true }).click();
  await expect(page.locator('.progress-entry')).toHaveCount(60);
}

test('recipe drafts survive language changes, auto-apply on player switching and undo independently', async ({
  page,
}) => {
  await openProgress(page);
  await page.getByRole('searchbox', { name: '搜索名称或编号', exact: true }).fill('2047');
  await expect(page.locator('.progress-entry')).toHaveCount(1);
  await page.locator('.progress-entry').click();
  const editor = page.locator('.progress-editor');
  for (const name of ['已学习', '已制作', '新标记', '收藏'])
    await editor.getByRole('checkbox', { name, exact: true }).check();
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(editor.getByRole('checkbox', { name: 'Favorite', exact: true })).toBeChecked();
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.locator('.player-chip').nth(1).click();
  await expect(editor.getByRole('checkbox', { name: '已学习', exact: true })).not.toBeChecked();
  await page.locator('.player-chip').first().click();
  await page.getByRole('searchbox').fill('2047');
  await page.locator('.progress-entry').click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).toBeChecked();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).not.toBeChecked();
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).toBeChecked();
  await page.getByRole('button', { name: '全部清空', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).toBeChecked();
  await page.getByRole('button', { name: '全部清空', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).not.toBeChecked();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('checkbox', { name: '收藏', exact: true })).toBeChecked();
});

test('reaction wheel and learned slots preserve separate drafts and batch edits remain reversible', async ({
  page,
}) => {
  await openProgress(page);
  await page.getByRole('button', { name: '反应', exact: true }).click();
  const editor = page.locator('.progress-editor');
  await editor.getByRole('combobox', { name: '反应编号', exact: true }).selectOption('255');
  await editor.getByRole('checkbox', { name: '新标记', exact: true }).check();
  await page.getByRole('searchbox').fill('263');
  await page.locator('.progress-entry').click();
  await expect(editor.getByRole('checkbox')).toHaveCount(0);
  await editor.getByRole('combobox', { name: '反应编号', exact: true }).selectOption('3');
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('combobox')).toHaveValue('0');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(editor.getByRole('combobox')).toHaveValue('3');
  await page.getByRole('button', { name: '全部清空', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(editor.getByRole('combobox')).toHaveValue('0');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('combobox')).toHaveValue('3');
  await page.getByRole('searchbox').fill('');
  await page.locator('.progress-entry').first().click();
  await expect(editor.getByRole('combobox')).toHaveValue('255');
  await expect(editor.getByRole('checkbox', { name: '新标记', exact: true })).toBeChecked();
});

test('achievement counts, dates and read flags validate, autosave and stay usable in nine languages', async ({
  page,
}) => {
  await openProgress(page);
  await page.getByRole('button', { name: '成就', exact: true }).click();
  const editor = page.locator('.progress-editor');
  await expect(editor.locator('.progress-milestone')).toHaveCount(6);
  await editor.getByRole('spinbutton', { name: '计数', exact: true }).fill('4294967296');
  await editor.getByRole('button', { name: '应用修改', exact: true }).click();
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('setAchievement'),
    ),
  ).toBe(false);
  await editor.getByRole('spinbutton', { name: '计数', exact: true }).fill('4294967295');
  await editor.getByRole('spinbutton', { name: '原始标记', exact: true }).fill('255');
  await editor.getByRole('textbox', { name: '日期', exact: true }).first().fill('2024-02-29');
  await editor.getByRole('checkbox', { name: '已读', exact: true }).first().check();
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(editor.getByRole('textbox', { name: '日期', exact: true }).first()).toHaveValue('');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(editor.getByRole('textbox', { name: '日期', exact: true }).first()).toHaveValue('2024-02-29');
  await page.setViewportSize({ width: 1180, height: 1000 });
  expect((await editor.getByRole('checkbox').first().boundingBox())!.width).toBeLessThan(30);
  await editor.getByRole('heading').scrollIntoViewIfNeeded();
  await page.screenshot({ path: 'test-results/achievement-editor-zhs.png' });
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(text(code).progress);
    await expect(editor.getByRole('spinbutton', { name: text(code).progressCount, exact: true })).toHaveValue(
      '4294967295',
    );
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 1000 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
});
