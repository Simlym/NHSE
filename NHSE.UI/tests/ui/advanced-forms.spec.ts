import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';

async function open(page: Page, kind = 'mailbox') {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '高级编辑', exact: true }).click();
  await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption(kind);
}
const field = (page: Page, key: string) => page.locator(`[data-field-key="${key}"]`).locator('input, textarea, select');
const section = (page: Page, id: number) => page.locator(`.advanced-section[data-group="${id}"]`);
const messages = (page: Page, command = 'setAdvanced') => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
const apply = (page: Page) => page.locator('.advanced-fields').getByRole('button', { name: '应用修改', exact: true }).click();

test('mail content has a live Unicode counter and preview without changing the save', async ({ page }) => {
  await open(page);
  await expect(page.locator('.advanced-field')).toHaveCount(30);
  await expect(section(page, 2)).not.toHaveAttribute('open');
  await expect(section(page, 5)).not.toHaveAttribute('open');
  await field(page, 'To').fill('小岛居民'); await field(page, 'Message').fill('您好😀\n欢迎回家'); await field(page, 'From').fill('朋友');
  await expect(page.locator('[data-field-key="Message"] .character-count')).toHaveText('9 / 239');
  await page.locator('.mail-preview > summary').click();
  await expect(page.locator('.mail-preview article')).toContainText('您好😀\n欢迎回家');
  await expect(page.locator('.draft-status')).toBeVisible();
  expect(await messages(page)).toHaveLength(0);
  await apply(page);
  expect((await messages(page))[0]).toMatchObject({ values: { To: '小岛居民', Message: '您好😀\n欢迎回家', From: '朋友', Year: '65535', Month: '255', Day: '255', SenderIslandID: '4294967295' } });
});

test('search exposes raw and sender fields and preserves unrecognized mail values', async ({ page }) => {
  await open(page);
  const search = page.getByRole('searchbox', { name: '搜索字段', exact: true });
  await search.fill('SenderIslandID'); await expect(field(page, 'SenderIslandID')).toBeVisible();
  await expect(section(page, 2)).toContainText('共用存储位置');
  await field(page, 'SenderIslandID').fill('4294967294');
  await search.fill('MailType01'); await field(page, 'MailType01').fill('253');
  await search.fill('not-a-real-field'); await expect(page.locator('.advanced-fields [role="status"]').last()).toContainText('没有匹配的字段');
  await apply(page);
  expect((await messages(page))[0]).toMatchObject({ values: { SenderIslandID: '4294967294', MailType01: '253', Year: '65535', Month: '255', Day: '255', Language: '' } });
});

test('invalid hidden drafts reveal their field and block both navigation and submission', async ({ page }) => {
  await open(page);
  await section(page, 5).locator('summary').click();
  await field(page, 'SortIndex').fill('65536');
  await section(page, 5).locator('summary').click();
  await page.getByRole('searchbox', { name: '搜索字段', exact: true }).fill('Message');
  const kind = page.getByRole('combobox', { name: '编辑类别', exact: true });
  await kind.selectOption('resident'); await expect(kind).toHaveValue('mailbox');
  await expect(field(page, 'SortIndex')).toBeVisible(); await expect(field(page, 'SortIndex')).toBeFocused();
  await expect(page.locator('.advanced-fields [role="alert"]')).toContainText('65535');
  expect(await messages(page)).toHaveLength(0);
  await field(page, 'SortIndex').fill('123');
  await kind.selectOption('resident'); await expect(kind).toHaveValue('resident');
  expect((await messages(page))[0]).toMatchObject({ target: { kind: 'mailbox' }, values: { SortIndex: '123' } });
});

test('attachment selection stays a draft and uses the existing item catalogue', async ({ page }) => {
  await open(page);
  await section(page, 1).getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '测试物品002', exact: true }).click();
  await expect(field(page, 'ItemId')).toHaveValue('1001');
  expect(await messages(page)).toHaveLength(0);
  await apply(page);
  expect((await messages(page))[0]).toMatchObject({ values: { ItemId: '1001', SystemParam: '0', AdditionalParam: '0', FreeParam: '0' } });
});

test('task groups keep exact 64-bit item drafts and memory fields editable', async ({ page }) => {
  await open(page, 'item');
  await expect(section(page, 11)).toBeVisible();
  await field(page, 'RawValue').fill('18446744073709551615'); await apply(page);
  expect((await messages(page))[0]).toMatchObject({ values: { RawValue: '18446744073709551615' } });
  await page.getByRole('combobox', { name: '编辑类别', exact: true }).selectOption('memory');
  await expect(section(page, 8)).toBeVisible(); await expect(section(page, 9)).toBeVisible(); await expect(section(page, 10)).toBeVisible();
  await field(page, 'Greeting.10').fill('问候😀'); await apply(page);
  expect((await messages(page)).at(-1)).toMatchObject({ values: { 'Greeting.10': '问候😀' } });
});

test('mail task groups remain usable at mobile width in all nine languages', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 }); await open(page);
  await field(page, 'Message').fill('草稿😀');
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(section(page, 0).locator('summary')).toContainText(text.advancedGroupNames.split('|')[0]);
    await expect(field(page, 'Message')).toHaveValue('草稿😀');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
