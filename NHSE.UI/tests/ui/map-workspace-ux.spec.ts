import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';
async function openMap(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page'); await expect(map).toBeVisible(); return map;
}
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
for (const width of [1180, 390]) test(`paste overlay preserves the canvas geometry at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  const map = await openMap(page), grid = map.locator('.map-grid');
  await map.getByRole('spinbutton', { name: '高度', exact: true }).fill('2');
  await map.getByRole('button', { name: '应用修改', exact: true }).click();
  await grid.focus(); await page.keyboard.press('Control+c');
  await grid.getByRole('button', { name: '4, 4', exact: true }).click();
  const before = await map.locator('.map-viewport').boundingBox();
  await page.keyboard.press('Control+v');
  await expect(map.locator('.map-paste-preview').getByRole('button', { name: /确认粘贴/ })).toBeEnabled();
  const after = await map.locator('.map-viewport').boundingBox();
  expect(Math.abs(after!.width - before!.width)).toBeLessThan(1); expect(Math.abs(after!.x - before!.x)).toBeLessThan(1); expect(Math.abs(after!.y - before!.y)).toBeLessThan(1);
  await page.keyboard.press('Escape'); expect(await payloads(page, 'pasteGrid')).toHaveLength(0);
});
test('focus map and independent panels are reversible view operations', async ({ page }) => {
  const map = await openMap(page);
  await map.getByRole('button', { name: '专注地图', exact: true }).click();
  await expect(map.locator('.map-navigator')).toBeHidden(); await expect(map.locator('.map-inspector')).toBeHidden();
  await expect(map.locator('.map-grid')).toBeVisible();
  await map.getByRole('button', { name: '显示或隐藏属性栏', exact: true }).click();
  await expect(map.locator('.map-inspector')).toBeVisible(); await expect(map.locator('.map-navigator')).toBeHidden();
  expect(await payloads(page, 'editMap')).toHaveLength(0);
});
for (const width of [1180, 390]) test(`wheel zoom keeps the pointer world position at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  const map = await openMap(page), canvas = map.locator('.map-canvas-scroll');
  await canvas.scrollIntoViewIfNeeded(); const box = (await canvas.boundingBox())!;
  const x = box.x + box.width * .65, y = box.y + box.height * .42;
  const world = () => map.locator('.map-viewport').evaluate((el, point) => { const b = el.getBoundingClientRect(); return [(point.x - b.left - el.clientLeft) / el.clientWidth, (point.y - b.top - el.clientTop) / el.clientHeight]; }, { x, y });
  const before = await world(); await page.mouse.move(x, y); await page.keyboard.down('Control'); await page.mouse.wheel(0, -100); await page.keyboard.up('Control');
  await expect(map.locator('.canvas-zoom output')).toHaveText('125%');
  const after = await world(); expect(Math.abs(after[0]! - before[0]!)).toBeLessThan(.015); expect(Math.abs(after[1]! - before[1]!)).toBeLessThan(.015);
  expect(await payloads(page, 'editMap')).toHaveLength(0);
});
test('brush templates and selected drafts remain separate across tool switches', async ({ page }) => {
  const map = await openMap(page), height = map.getByRole('spinbutton', { name: '高度', exact: true });
  await height.fill('2'); await map.getByRole('button', { name: '拖拽地形笔刷', exact: true }).click();
  await expect(map.locator('.draft-badge')).toContainText('笔刷模板');
  await height.fill('3'); await map.getByRole('button', { name: '选择格子', exact: true }).click(); await expect(height).toHaveValue('2');
  await map.getByRole('button', { name: '拖拽地形笔刷', exact: true }).click(); await expect(height).toHaveValue('3');
  await map.getByRole('button', { name: '选择格子', exact: true }).click();
  await map.getByRole('button', { name: '还原当前格草稿', exact: true }).click(); await expect(height).toHaveValue('0');
  expect(await payloads(page, 'editMap')).toHaveLength(0);
});
test('large brush shows its full mask before writing and writes a single stroke', async ({ page }) => {
  const map = await openMap(page), grid = map.locator('.map-grid');
  await map.getByRole('button', { name: '拖拽地形笔刷', exact: true }).click(); await expect(grid).toHaveAttribute('aria-busy', 'false');
  await map.getByRole('spinbutton', { name: '笔刷大小', exact: true }).fill('3');
  await grid.getByRole('button', { name: '4, 4', exact: true }).hover();
  await expect(grid.locator('.brush-hover')).toHaveCount(13); expect(await payloads(page, 'paintMapStroke')).toHaveLength(0);
  await page.mouse.down(); await expect(grid.locator('.stroke-preview')).toHaveCount(13); await page.mouse.up();
  await expect.poll(async () => (await payloads(page, 'paintMapStroke')).length).toBe(1);
});
test('island overview uses a read-only modal and keyboard acre navigation', async ({ page }) => {
  const map = await openMap(page); await map.getByRole('button', { name: '全岛概览', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '全岛概览', exact: true }); await expect(dialog).toBeVisible();
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter');
  await expect(dialog).not.toBeVisible(); await expect(map.getByRole('combobox', { name: '分区', exact: true })).toHaveValue('11');
  expect(await payloads(page, 'editMap')).toHaveLength(0);
});
test('returning to the map restores the same zoom and world view, and reopening clears session view state', async ({ page }) => {
  const map = await openMap(page);
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  const center = () => page.locator('.map-canvas-scroll').evaluate(element => {
    const frame = element.getBoundingClientRect(), viewport = element.querySelector<HTMLElement>('.map-viewport')!, box = viewport.getBoundingClientRect();
    return { x: (frame.left + element.clientWidth / 2 - box.left - viewport.clientLeft) / viewport.clientWidth, y: (frame.top + element.clientHeight / 2 - box.top - viewport.clientTop) / viewport.clientHeight };
  });
  const before = await center();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿概览', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  await expect(map.locator('.canvas-zoom output')).toHaveText('150%');
  await expect.poll(async () => Math.abs((await center()).x - before.x)).toBeLessThan(.02);
  await expect.poll(async () => Math.abs((await center()).y - before.y)).toBeLessThan(.02);
  await page.getByRole('button', { name: '关闭存档', exact: true }).click();
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  await expect(map.locator('.canvas-zoom output')).toHaveText('100%');
});
