import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
async function open(page: Page, section = '高级编辑') {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: section, exact: true }).click();
}
const messages = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
const field = (page: Page, key: string) => page.locator('.advanced-field').filter({ has: page.locator('small', { hasText: new RegExp(`^${key.replaceAll('.', '\\.')}($| ·)`) }) }).locator('input, textarea, select');

test('event flags reach the final slot, preserve signed values and keep drafts across languages', async ({ page }) => {
  await open(page);
  await page.getByRole('searchbox', { name: '搜索条目', exact: true }).fill('1023');
  await page.locator('.advanced-entry').click();
  await field(page, 'Value').fill('-32768');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(field(page, 'Value')).toHaveValue('-32768');
  await page.getByRole('combobox', { name: 'Editor', exact: true }).selectOption('flagsLand');
  expect((await messages(page, 'setAdvanced'))[0]).toMatchObject({ target: { kind: 'flagsPlayer', index: 1023 }, values: { Value: '-32768' } });
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await page.getByRole('combobox', { name: 'Editor', exact: true }).selectOption('flagsPlayer');
  await page.getByRole('spinbutton', { name: 'Slot', exact: true }).fill('1023');
  await page.getByRole('spinbutton', { name: 'Slot', exact: true }).press('Tab');
  await expect(field(page, 'Value')).toHaveValue('0');
});

test('resident advanced details, final memory slot and resident designs open in Vue', async ({ page }) => {
  await open(page);
  await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('memory');
  const slot = page.getByRole('spinbutton', { name: '格子', exact: true });
  await slot.fill('7'); await slot.press('Tab');
  await field(page, 'Friendship').fill('255'); await field(page, 'Greeting.10').fill('您好😀');
  await page.locator('.advanced-fields').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await messages(page, 'setAdvanced')).at(-1)).toMatchObject({ target: { kind: 'memory', index: 7 }, values: { Friendship: '255', 'Greeting.10': '您好😀' } });
  await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('resident');
  await page.getByRole('button', { name: '居民设计', exact: true }).click();
  await expect(page.locator('.design-sheet')).toHaveCount(4);
  expect((await messages(page, 'designs')).at(-1)).toMatchObject({ kind: 'residentDesigns', index: 0 });
  expect(await messages(page, 'legacy')).toHaveLength(0);
});

test('mail slot 299, Unicode text and sort indexes survive player switches and undo', async ({ page }) => {
  await open(page);
  await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('mailbox');
  const slot = page.getByRole('spinbutton', { name: '格子', exact: true }); await slot.fill('299'); await slot.press('Tab');
  await field(page, 'Message').fill('Hello\n您好😀'); await field(page, 'SortIndex').fill('299'); await field(page, 'Language').fill('en-GB');
  await page.locator('.advanced-fields').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await messages(page, 'setAdvanced')).at(-1)).toMatchObject({ target: { kind: 'mailbox', index: 299 }, values: { Message: 'Hello\n您好😀', SortIndex: '299', Language: 'en-GB' } });
  await page.getByRole('combobox', { name: '玩家资料', exact: true }).selectOption('1');
  await expect(field(page, 'Message')).toHaveValue('');
  await page.getByRole('combobox', { name: '玩家资料', exact: true }).selectOption('0');
  await slot.fill('299'); await slot.press('Tab'); await expect(field(page, 'Message')).toHaveValue('Hello\n您好😀');
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(field(page, 'Message')).toHaveValue('');
});

test('full 64-bit item details, alias validation and file cancellation preserve history', async ({ page }) => {
  await open(page); await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('item');
  await field(page, 'RawValue').fill('18446744073709551615');
  await page.locator('.advanced-fields').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await messages(page, 'setAdvanced'))[0]).toMatchObject({ values: { RawValue: '18446744073709551615' } });
  await field(page, 'Rotation').fill('4'); await page.locator('.advanced-fields').getByRole('button', { name: '应用修改', exact: true }).click();
  expect(await messages(page, 'setAdvanced')).toHaveLength(1);
  await field(page, 'Rotation').fill('0');
  await page.getByRole('button', { name: '导出所选', exact: true }).click();
  expect((await messages(page, 'advancedExport'))[0]).toMatchObject({ all: false });
  await page.evaluate(() => Object.assign(window, { nhseAdvancedCancelNext: true }));
  await page.getByRole('button', { name: '导入整组物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(field(page, 'RawValue')).toHaveValue('1000');
});

test('text inventory batches show scope, support cancelling and submit one whole-container action', async ({ page }) => {
  await open(page); await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('item');
  await page.getByRole('textbox', { name: '文本批处理', exact: true }).fill('=Count=*UseCount\n.Count=$4,7\n;\n.FreeParam=3');
  await page.getByRole('button', { name: '检查操作', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  expect(await messages(page, 'advancedTextBatch')).toHaveLength(0);
  await page.getByRole('button', { name: '检查操作', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await messages(page, 'advancedTextBatch'))[0]).toMatchObject({ target: { kind: 'item', container: 'pocket' }, text: '=Count=*UseCount\n.Count=$4,7\n;\n.FreeParam=3' });
});

test('advanced editors stay localized and usable on compact screens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 }); await open(page);
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('combobox', { name: text.advancedSection, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});

test('console write requires review, reads preserve history and pocket reads join undo', async ({ page }) => {
  await open(page, '主机工具');
  await page.getByRole('button', { name: '连接', exact: true }).click();
  await page.getByRole('button', { name: '读取内存', exact: true }).click();
  const bytes = page.getByRole('textbox', { name: '十六进制字节', exact: true }); await expect(bytes).toHaveValue('01020304');
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await bytes.fill('AABBCCDD'); await page.getByRole('button', { name: '写入内存', exact: true }).click();
  expect(await messages(page, 'consoleWrite')).toHaveLength(0);
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await page.getByRole('button', { name: '写入内存', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await messages(page, 'consoleWrite'))[0]).toMatchObject({ data: 'AABBCCDD', offset: '00001000' });
  await page.getByRole('button', { name: '读取主机背包', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
});

test('console repetitions stop on errors and page exit without sending unapproved writes', async ({ page }) => {
  await open(page, '主机工具');
  await page.getByRole('button', { name: '连接', exact: true }).click();
  await page.getByRole('combobox', { name: '重复操作', exact: true }).selectOption('consoleRead');
  await page.getByRole('spinbutton', { name: '间隔（毫秒）', exact: true }).fill('1000');
  await page.getByRole('button', { name: '开始重复', exact: true }).click();
  await expect.poll(async () => (await messages(page, 'consoleRead')).length).toBeGreaterThan(0);
  await page.evaluate(() => Object.assign(window, { nhseConsoleErrorNext: true }));
  await expect(page.getByRole('button', { name: '停止', exact: true })).toBeDisabled();
  expect(await messages(page, 'consoleWrite')).toHaveLength(0);
  await page.getByRole('button', { name: '连接', exact: true }).click();
  await page.getByRole('button', { name: '开始重复', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  const count = (await messages(page, 'consoleRead')).length;
  await page.waitForTimeout(1200); expect(await messages(page, 'consoleRead')).toHaveLength(count);
});

test('blocked advanced navigation restores every control to the actual target', async ({ page }) => {
  await open(page);
  const kind = page.getByRole('combobox', { name: '编辑类别', exact: true });
  const player = page.getByRole('combobox', { name: '玩家资料', exact: true });
  const slot = page.getByRole('spinbutton', { name: '格子', exact: true });
  await field(page, 'Value').fill('abc');
  await kind.selectOption('mailbox');
  await expect(kind).toHaveValue('flagsPlayer');
  await expect(page.locator('.advanced-fields h2')).toHaveText('玩家事件标记 · #0');
  await player.selectOption('1'); await expect(player).toHaveValue('0');
  await slot.fill('9'); await slot.press('Tab'); await expect(slot).toHaveValue('0');
  expect(await messages(page, 'setAdvanced')).toHaveLength(0);
  await field(page, 'Value').fill('12'); await kind.selectOption('flagsLand');
  await expect(kind).toHaveValue('flagsLand');
  await expect(page.locator('.advanced-target').getByRole('combobox', { name: '玩家资料', exact: true })).toHaveCount(0);
  expect((await messages(page, 'setAdvanced')).at(-1)).toMatchObject({ target: { kind: 'flagsPlayer', index: 0 }, values: { Value: '12' } });
});

test('console validation failure keeps the host connection and permits retry', async ({ page }) => {
  await open(page, '主机工具');
  await page.getByRole('button', { name: '连接', exact: true }).click();
  await page.evaluate(() => Object.assign(window, { nhseConsoleValidationNext: true }));
  await page.getByRole('button', { name: '读取主机背包', exact: true }).click();
  await expect(page.locator('.console-page [role=status]')).toContainText('已连接');
  await expect(page.getByRole('button', { name: '读取主机背包', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: '读取主机背包', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeEnabled();
  expect(await messages(page, 'consoleConnect')).toHaveLength(1);
});
