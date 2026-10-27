import { test, expect, type Page, type Locator } from '@playwright/test';
import { mockHost } from './mock-host';
const payloads = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
async function open(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page'); await map.getByRole('button', { name: '物品', exact: true }).click();
  return { map, grid: map.locator('.map-grid'), tools: map.locator('.map-placement-tools') };
}
async function stamp(page: Page, map: Locator, tools: Locator) {
  await map.locator('.canvas-direct-tools').getByRole('button', { name: '连续放置', exact: true }).click();
  await tools.getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '木材', exact: true }).click();
}
async function drag(page: Page, grid: Locator, from: [number, number], to: [number, number]) {
  await grid.scrollIntoViewIfNeeded(); const box = (await grid.boundingBox())!;
  await page.mouse.move(box.x + box.width * (from[0] + .5) / 32, box.y + box.height * (from[1] + .5) / 32); await page.mouse.down();
  await page.mouse.move(box.x + box.width * (to[0] + .5) / 32, box.y + box.height * (to[1] + .5) / 32, { steps: 5 });
  await page.mouse.up();
}
test('continuous placement retains its template, R rotates, each click is one command and Escape exits', async ({ page }) => {
  const { map, grid, tools } = await open(page); await stamp(page, map, tools);
  await grid.focus(); await page.keyboard.press('r');
  await grid.getByRole('button', { name: '4, 4', exact: true }).click();
  await expect.poll(async () => (await payloads(page, 'placeMap')).length).toBe(1);
  expect((await payloads(page, 'placeMap'))[0]).toMatchObject({ target: { x: 4, y: 4 }, current: { itemId: 1000, systemParam: 1 } });
  await grid.getByRole('button', { name: '6, 4', exact: true }).click();
  await expect.poll(async () => (await payloads(page, 'placeMap')).length).toBe(2);
  await expect(map.locator('.canvas-direct-tools').getByRole('button', { name: '连续放置', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await grid.focus(); await page.keyboard.press('Control+z');
  await expect(grid.getByRole('button', { name: '6, 4', exact: true })).toHaveAttribute('aria-label', '6, 4');
  await page.keyboard.press('Escape');
  await expect(map.locator('.canvas-direct-tools').getByRole('button', { name: '连续放置', exact: true })).toHaveAttribute('aria-pressed', 'false');
  expect(await payloads(page, 'editMap')).toHaveLength(0);
});
test('dragging occupied cells moves one selection atomically and protected destinations reject', async ({ page }) => {
  const { map, grid, tools } = await open(page); await stamp(page, map, tools);
  await grid.getByRole('button', { name: '2, 2', exact: true }).click(); await expect.poll(async () => (await payloads(page, 'placeMap')).length).toBe(1);
  await grid.getByRole('button', { name: '6, 2', exact: true }).click(); await expect.poll(async () => (await payloads(page, 'placeMap')).length).toBe(2);
  await map.locator('.canvas-direct-tools').getByRole('button', { name: '移动物品', exact: true }).click();
  await drag(page, grid, [2, 2], [4, 4]);
  await expect.poll(async () => (await payloads(page, 'moveMap')).length).toBe(1);
  expect((await payloads(page, 'moveMap'))[0]).toMatchObject({ selection: [{ x: 2, y: 2 }], anchor: { x: 4, y: 4 }, snapFieldItems: true });
  await expect(grid.getByRole('button', { name: '2, 2', exact: true })).toHaveCount(1);
  await drag(page, grid, [4, 4], [6, 2]);
  await expect(tools.getByText(/空间|重叠|放置|占用/).last()).toBeVisible();
  expect(await payloads(page, 'moveMap')).toHaveLength(1);
});
test('Escape, pointer loss and window blur cancel placement without writes', async ({ page }) => {
  const { map, grid, tools } = await open(page); await stamp(page, map, tools);
  for (const cancel of ['escape', 'pointer', 'blur', 'undo']) {
    await map.locator('.canvas-direct-tools').getByRole('button', { name: '连续放置', exact: true }).click();
    await grid.scrollIntoViewIfNeeded(); const box = (await grid.boundingBox())!;
    await page.mouse.move(box.x + box.width * 4.5 / 32, box.y + box.height * 4.5 / 32); await page.mouse.down();
    if (cancel === 'escape') await page.keyboard.press('Escape');
    else if (cancel === 'undo') await page.keyboard.press('Control+z');
    else if (cancel === 'pointer') await grid.dispatchEvent('pointercancel', { pointerId: 1 });
    else await page.evaluate(() => window.dispatchEvent(new Event('blur')));
    await page.mouse.up();
  }
  expect(await payloads(page, 'placeMap')).toHaveLength(0); expect(await payloads(page, 'moveMap')).toHaveLength(0);
});
test('invalid or unapplied inspector drafts block direct tools and item selection stays a template', async ({ page }) => {
  const { map, tools } = await open(page);
  await map.locator('#map-property-form input').first().fill('1000');
  await expect(map.locator('.canvas-direct-tools').getByRole('button', { name: '连续放置', exact: true })).toBeDisabled();
  await expect(tools.getByText(/请先应用或还原/)).toBeVisible();
  expect(await payloads(page, 'placeMap')).toHaveLength(0);
});
for (const width of [1180, 390]) test(`building dragging uses label-relative coordinates and Escape preserves position at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 }); const { map } = await open(page);
  await map.getByRole('button', { name: '建筑', exact: true }).click();
  await map.locator('.map-building-list button').first().click();
  const label = map.locator('.map-building-label[aria-pressed=true]'); await label.scrollIntoViewIfNeeded();
  const viewport = (await map.locator('.map-viewport').boundingBox())!, box = (await label.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + viewport.width * 3 / 32, box.y + box.height / 2 + viewport.height * 2 / 32, { steps: 4 }); await page.mouse.up();
  await expect.poll(async () => (await payloads(page, 'editMap')).length).toBe(1);
  expect((await payloads(page, 'editMap'))[0]).toMatchObject({ values: { 'Map.X': 83, 'Map.Y': 66 } });
  await label.scrollIntoViewIfNeeded(); const next = (await label.boundingBox())!;
  await page.mouse.move(next.x + next.width / 2, next.y + next.height / 2); await page.mouse.down();
  await page.mouse.move(next.x + next.width / 2 + viewport.width / 8, next.y + next.height / 2);
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect(await payloads(page, 'editMap')).toHaveLength(1);
});
