import { test, expect, type Page, type Locator } from '@playwright/test';
import { mockHost } from './mock-host';
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
async function openMap(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  return page.locator('.map-page');
}
async function rectangle(page: Page, grid: Locator, cols: number, start: [number, number], end: [number, number]) {
  await grid.scrollIntoViewIfNeeded(); const box = (await grid.boundingBox())!;
  await page.mouse.move(box.x + box.width * (start[0] + .5) / cols, box.y + box.height * (start[1] + .5) / cols);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * (end[0] + .5) / cols, box.y + box.height * (end[1] + .5) / cols);
  await page.mouse.up();
}
test('terrain rectangle copy/paste, additive selection, keyboard navigation and text shortcuts stay separate', async ({ page }) => {
  const map = await openMap(page), grid = map.locator('.map-grid');
  await map.getByRole('spinbutton', { name: '高度', exact: true }).fill('2');
  await map.getByRole('button', { name: '应用修改', exact: true }).click();
  await rectangle(page, grid, 16, [0, 0], [1, 1]);
  await expect(map.getByText('已选 4 格', { exact: true })).toBeVisible();
  await page.keyboard.press('Control+c');
  await expect(map.getByText('剪贴板：4 条 · 2 × 2', { exact: true })).toBeVisible();
  expect((await payloads(page, 'copyGrid'))[0]).toMatchObject({ selection: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }] });
  await page.evaluate(() => Object.assign(window, { nhseDelayMapNext: 150 }));
  await grid.getByRole('button', { name: '4, 4', exact: true }).click(); await page.keyboard.press('Control+v');
  await expect(map.getByRole('spinbutton', { name: '高度', exact: true })).toHaveValue('2');
  expect((await payloads(page, 'pasteGrid'))[0]).toMatchObject({ anchor: { x: 4, y: 4 }, noOverwrite: true });
  await page.keyboard.press('Control+z');
  await expect(map.getByRole('spinbutton', { name: '高度', exact: true })).toHaveValue('0');
  await grid.getByRole('button', { name: '0, 0', exact: true }).click();
  await grid.getByRole('button', { name: '2, 0', exact: true }).click({ modifiers: ['Control'] });
  await expect(map.getByText('已选 2 格', { exact: true })).toBeVisible();
  await grid.getByRole('button', { name: '3, 1', exact: true }).click({ modifiers: ['Shift'] });
  await expect(map.getByText('已选 4 格', { exact: true })).toBeVisible();
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Shift+ArrowDown');
  await expect(map.getByText('已选 2 格', { exact: true })).toBeVisible();
  await page.keyboard.press('Meta+c');
  await expect.poll(async () => (await payloads(page, 'copyGrid')).length).toBe(2);
  await page.keyboard.press('Control+a'); await expect(map.getByText('已选 256 格', { exact: true })).toBeVisible();
  await page.keyboard.press('r');
  await expect.poll(async () => (await payloads(page, 'editGrid')).length).toBe(1);
  expect((await payloads(page, 'editGrid'))[0]).toMatchObject({ operation: 'rotate' });
  const field = map.getByRole('spinbutton', { name: '高度', exact: true });
  await field.fill('12'); await field.press('Control+a'); await field.press('Control+c');
  expect(await payloads(page, 'copyGrid')).toHaveLength(2);
  await field.press('Backspace'); expect(await payloads(page, 'editGrid')).toHaveLength(1);
  await field.fill('0');
});

test('item clipboard is shared with rooms and layers, and clearing all room cells is one command', async ({ page }) => {
  const map = await openMap(page);
  await map.getByRole('button', { name: '物品', exact: true }).click();
  await map.getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '木材', exact: true }).click();
  await map.getByRole('button', { name: '应用修改', exact: true }).click();
  await map.locator('.map-grid').focus(); await page.keyboard.press('Control+c');
  await expect(map.getByText(/剪贴板：1 条/)).toBeVisible();
  await page.locator('.sidebar nav').getByRole('button', { name: '房屋与岛屿生活', exact: true }).click();
  const room = page.locator('.housing-grid'); await room.locator('button').first().click();
  await page.keyboard.press('Control+v');
  await expect(page.locator('.housing-tile-editor input[type=number]').first()).toHaveValue('1000');
  expect((await payloads(page, 'pasteGrid'))[0]).toMatchObject({ target: { kind: 'roomItems', house: 0, room: 0, layer: 0 } });
  await page.keyboard.press('Control+c');
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '房屋与岛屿生活', exact: true }).click();
  await expect(page.locator('.housing-tile-editor input[type=number]').first()).toHaveValue('1000');
  await page.getByRole('combobox', { name: '图层', exact: true }).selectOption('7');
  expect(await payloads(page, 'setRoomItem')).toHaveLength(0);
  await room.focus(); await page.keyboard.press('Control+v');
  await expect(page.locator('.housing-tile.empty')).toHaveCount(399);
  await page.keyboard.press('Control+a');
  await expect(page.getByText('已选 400 格', { exact: true })).toBeVisible();
  await page.keyboard.press('Delete');
  await expect(page.locator('.housing-tile.empty')).toHaveCount(400);
  const edits = await payloads(page, 'editGrid'); expect(edits).toHaveLength(1);
  expect((edits[0]!.selection as unknown[]).length).toBe(400);
  await page.keyboard.press('Control+z'); await expect(page.locator('.housing-tile.empty')).toHaveCount(399);
});

for (const width of [800, 390]) {
  test(`selection rectangle uses scaled coordinates and cancels pointer loss at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const map = await openMap(page), grid = map.locator('.map-grid');
    await rectangle(page, grid, 16, [0, 0], [1, 1]);
    await expect(map.getByText('已选 4 格', { exact: true })).toBeVisible();
    await page.keyboard.press('Escape'); await expect(map.getByText('已选 0 格', { exact: true })).toBeVisible();
    const box = (await grid.boundingBox())!;
    await page.mouse.move(box.x + box.width / 32, box.y + box.height / 32); await page.mouse.down();
    await grid.dispatchEvent('pointercancel', { pointerId: 1 }); await page.mouse.up();
    await expect(map.getByText('已选 0 格', { exact: true })).toBeVisible();
    expect(await payloads(page, 'editGrid')).toHaveLength(0);
    const dimensions = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
    expect(dimensions[0]).toBeLessThanOrEqual(dimensions[1]!);
  });
}
