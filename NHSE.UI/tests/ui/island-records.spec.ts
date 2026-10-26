import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
async function open(page: Page, section = '公告栏') {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  await page.getByRole('button', { name: section, exact: true }).click();
}
const field = (page: Page, key: string) => page.locator('.record-field').filter({ has: page.locator(`label[for="record-field-${key}"]`) }).locator('input,textarea,select').first();
async function expandAdvanced(page: Page) {
  const details = page.locator('.record-editor > details').last();
  if (await details.getAttribute('open') === null) await details.locator('summary').click();
}
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);

test('bulletin text preserves raw dates, searches all thirty slots, applies drafts and undoes separately', async ({ page }) => {
  await open(page); await expect(page.locator('.record-entry')).toHaveCount(30);
  await expect(page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(field(page, 'Date')).toHaveValue('65535-255-255');
  await field(page, 'Body').fill('Hello\n您好😀');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(field(page, 'Body')).toHaveValue('Hello\n您好😀');
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.getByRole('searchbox', { name: '搜索正文、日期或槽位', exact: true }).fill('29');
  await page.locator('.record-entry').click();
  expect((await payloads(page, 'setIslandRecords')).at(-1)).toMatchObject({ section: 'bulletin', index: 0, values: { Body: 'Hello\n您好😀', Date: '65535-255-255' } });
  await expect(page.locator('.record-editor h2')).toHaveText('公告栏 · #29');
  await field(page, 'Body').fill('末条公告'); await field(page, 'Footer').fill('落款😀'); await field(page, 'Date').fill('2024-02-29');
  await page.getByRole('button', { name: '访客日程', exact: true }).click();
  await page.getByRole('button', { name: '公告栏', exact: true }).click();
  await page.getByRole('searchbox').fill('29'); await page.locator('.record-entry').click();
  await expect(field(page, 'Body')).toHaveValue('末条公告');
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(field(page, 'Body')).toHaveValue('');
  await page.getByRole('button', { name: '重做', exact: true }).click(); await expect(field(page, 'Body')).toHaveValue('末条公告');
  await page.getByText('文字预览', { exact: true }).click(); await expect(page.locator('.record-preview article')).toContainText('落款😀');
  expect(await payloads(page, 'legacy')).toHaveLength(0); expect(await payloads(page, 'tool')).toHaveLength(0);
});

test('invalid bulletin fields block navigation and full unsigned IDs and board fields save exactly', async ({ page }) => {
  await open(page); await field(page, 'Date').fill('2023-02-29');
  await page.getByRole('button', { name: '访客日程', exact: true }).click();
  await expect(page.locator('.record-editor [role=alert]')).toBeVisible(); await expect(page.locator('.record-editor h2')).toHaveText('公告栏 · #0');
  expect(await payloads(page, 'setIslandRecords')).toHaveLength(0);
  await field(page, 'Date').fill('0001-01-01');
  await expandAdvanced(page);
  await field(page, 'PopId').fill('18446744073709551616');
  await page.getByRole('button', { name: '应用修改', exact: true }).click(); expect(await payloads(page, 'setIslandRecords')).toHaveLength(0);
  await field(page, 'PopId').fill('18446744073709551615');
  await page.getByText('公告栏全局字段', { exact: true }).click(); await field(page, 'IndexTable.29').fill('-2147483648');
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  expect((await payloads(page, 'setIslandRecords')).at(-1)).toMatchObject({ values: { Date: '0001-01-01', PopId: '18446744073709551615', 'IndexTable.29': '-2147483648' } });
  await page.getByText('手写数据', { exact: true }).click(); await field(page, 'HandWrite.Palette').fill('GG000000');
  await page.getByRole('button', { name: '应用修改', exact: true }).click(); expect(await payloads(page, 'setIslandRecords')).toHaveLength(1);
  await field(page, 'HandWrite.Palette').fill('01020304'); await page.getByRole('button', { name: '应用修改', exact: true }).click();
  expect((await payloads(page, 'setIslandRecords')).at(-1)).toMatchObject({ values: { 'HandWrite.Palette': '01020304' } });
});

test('visitor schedules preserve unknown IDs, initial schedules, special days and untouched NaN coordinates', async ({ page }) => {
  await open(page, '访客日程'); await expect(page.locator('.record-day')).toHaveCount(7);
  await expect(field(page, 'VisitorNPC.0').getByRole('option', { name: '1 · 吕游', exact: true })).toHaveCount(1);
  await expect(field(page, 'VisitorNPC.6')).toHaveValue('999'); await field(page, 'VisitorNPC.6').selectOption('11');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(field(page, 'VisitorNPC.6')).toHaveValue('11');
  await expect(field(page, 'VisitorNPC.0').getByRole('option', { name: '1 · Gulliver', exact: true })).toHaveCount(1);
  await page.locator('.icon-select select').first().selectOption('zhs');
  await field(page, 'InitSchedule.6').selectOption('10'); await field(page, 'IsBirthdayLive.6').selectOption('True');
  await field(page, 'DayWisp').fill('-2147483648'); await field(page, 'DayCeleste').fill('2147483647');
  await expandAdvanced(page);
  await expect(field(page, 'CreatePos.X')).toHaveValue('NaN'); await field(page, 'CreatePos.Z').fill('123.25');
  await page.getByRole('button', { name: '公告栏', exact: true }).click();
  expect((await payloads(page, 'setIslandRecords')).at(-1)).toMatchObject({ section: 'visitors', values: { 'VisitorNPC.6': '11', 'InitSchedule.6': '10', 'IsBirthdayLive.6': 'True', 'CreatePos.X': 'NaN', 'CreatePos.Z': '123.25', DayWisp: '-2147483648', DayCeleste: '2147483647' } });
  await page.getByRole('button', { name: '访客日程', exact: true }).click();
  await expect(field(page, 'VisitorNPC.6')).toHaveValue('11');
  await page.getByRole('button', { name: '撤销', exact: true }).click(); await expect(field(page, 'VisitorNPC.6')).toHaveValue('999');
  await page.getByRole('button', { name: '重做', exact: true }).click(); await expect(field(page, 'VisitorNPC.6')).toHaveValue('11');
  await expandAdvanced(page); await field(page, 'CreatePos.Z').fill('Infinity');
  await page.getByRole('button', { name: '公告栏', exact: true }).click(); await expect(page.locator('.record-editor h2')).toHaveText('访客日程');
  expect(await payloads(page, 'setIslandRecords')).toHaveLength(1);
});

test('failed record navigation retains the actual section and page exit applies valid drafts', async ({ page }) => {
  await open(page); await page.evaluate(() => Object.assign(window, { nhseRecordNavigationErrorNext: true }));
  await page.getByRole('button', { name: '访客日程', exact: true }).click();
  await expect(page.getByRole('button', { name: '公告栏', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('.record-editor h2')).toHaveText('公告栏 · #0');
  await field(page, 'Body').fill('离开前应用');
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  expect((await payloads(page, 'setIslandRecords')).at(-1)).toMatchObject({ section: 'bulletin', index: 0, values: { Body: '离开前应用' } });
});

test('island records fit compact widths and retain drafts in all nine languages', async ({ page }) => {
  await open(page); await field(page, 'Body').fill('草稿😀');
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.locator('h1')).toHaveText(text.islandRecords); await expect(field(page, 'Body')).toHaveValue('草稿😀');
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
  }
  await page.getByRole('button', { name: '訪客日程', exact: true }).click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
