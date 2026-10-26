import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';
async function openMap(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  await expect(page.locator('.map-page')).toBeVisible();
}
const messages = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
const acre = (page: Page) => page.getByRole('combobox', { name: '分区', exact: true });
async function begin(page: Page, dx: number, dy: number, button: 'left' | 'middle' = 'left') {
  const box = (await page.locator('.map-canvas-scroll').boundingBox())!;
  const x = box.x + box.width * .65, y = box.y + box.height * .65;
  await page.mouse.move(x, y); await page.mouse.down({ button }); await page.mouse.move(x + dx, y + dy, { steps: 8 });
}
for (const width of [1180, 800, 390]) test(`main canvas pans across acres at fit zoom without touching the minimap at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); await openMap(page);
  await page.getByRole('button', { name: '拖动画布', exact: true }).click();
  await page.locator('.map-canvas-scroll').scrollIntoViewIfNeeded();
  const size = (await page.locator('.map-viewport').boundingBox())!.width;
  const initial = await page.locator('.map-viewport').evaluate(el => getComputedStyle(el).transform);
  await begin(page, -size * .65, 0);
  await expect(page.locator('.map-canvas-scroll')).toHaveClass(/panning/);
  expect(await page.locator('.map-viewport').evaluate(el => getComputedStyle(el).transform)).not.toBe(initial);
  await expect(acre(page)).toHaveValue('10'); // gesture preview stays local until release
  await page.mouse.up(); await expect(acre(page)).toHaveValue('11');
  await expect(page.locator('.map-canvas-scroll')).not.toHaveClass(/panning/);
  expect(await messages(page, 'editMap')).toHaveLength(0); expect(await messages(page, 'paintMapStroke')).toHaveLength(0);
  await page.getByRole('button', { name: '适应窗口', exact: true }).click();
  await expect(page.locator('.map-viewport')).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 0, 0)');
});

test('space and middle-button gestures pan temporarily, preserve brush mode and keep text shortcuts native', async ({ page }) => {
  await page.setViewportSize({ width: 1180, height: 1000 }); await openMap(page);
  await page.getByRole('button', { name: '拖拽地形笔刷', exact: true }).click();
  const size = (await page.locator('.map-viewport').boundingBox())!.width;
  const box = (await page.locator('.map-canvas-scroll').boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.keyboard.down('Space'); await begin(page, -size * .65, 0); await page.keyboard.up('Space'); await page.mouse.up();
  await expect(acre(page)).toHaveValue('11'); await expect(page.locator('.map-brush input[type=checkbox]').first()).toBeChecked();
  await page.getByRole('button', { name: '适应窗口', exact: true }).click();
  await begin(page, size * .65, 0, 'middle'); await page.mouse.up({ button: 'middle' }); await expect(acre(page)).toHaveValue('10');
  expect(await messages(page, 'paintMapStroke')).toHaveLength(0); expect(await messages(page, 'editMap')).toHaveLength(0);
  const input = page.locator('.map-property-form input').first(); await input.focus(); await page.keyboard.type('h');
  await expect(page.getByRole('button', { name: '拖动画布', exact: true })).toHaveAttribute('aria-pressed', 'false');
});

test('Escape, pointer cancellation and blur restore the camera without navigation or edits', async ({ page }) => {
  await openMap(page); await page.keyboard.press('h');
  const size = (await page.locator('.map-viewport').boundingBox())!.width;
  const count = (await messages(page, 'map')).length;
  await begin(page, -size * .7, 0); await page.keyboard.press('Escape'); await page.mouse.up();
  await expect(acre(page)).toHaveValue('10'); expect(await messages(page, 'map')).toHaveLength(count);
  await expect(page.locator('.map-viewport')).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 0, 0)');
  await begin(page, -size * .7, 0); await page.locator('.map-canvas-scroll').dispatchEvent('pointercancel', { pointerId: 1 }); await page.mouse.up();
  await expect(acre(page)).toHaveValue('10');
  await begin(page, -size * .7, 0); await page.evaluate(() => window.dispatchEvent(new Event('blur'))); await page.mouse.up();
  await expect(acre(page)).toHaveValue('10'); expect(await messages(page, 'editMap')).toHaveLength(0);
});

test('pan boundaries clamp to the island, failed navigation rolls back, and selection still works', async ({ page }) => {
  await openMap(page); await acre(page).selectOption('0'); await page.keyboard.press('h');
  const size = (await page.locator('.map-viewport').boundingBox())!.width;
  await begin(page, size * .8, size * .8); await page.mouse.up(); await expect(acre(page)).toHaveValue('0');
  await acre(page).selectOption('10');
  const elevation = page.locator('.map-property-form').getByRole('spinbutton', { name: '高度', exact: true });
  await elevation.fill('-1');
  await begin(page, -size * .7, 0); await page.mouse.up(); await expect(acre(page)).toHaveValue('10');
  await expect(page.locator('.map-viewport')).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 0, 0)');
  expect(await messages(page, 'editMap')).toHaveLength(0);
  await elevation.fill('0'); await page.getByRole('button', { name: '选择格子', exact: true }).click();
  await page.locator('.map-grid button').nth(34).click(); await expect(page.locator('.map-grid button.selected')).toHaveAttribute('aria-label', '2, 2');
});


test('clicking an adjacent acre revealed by panning selects its actual cell', async ({ page }) => {
  await page.setViewportSize({ width: 1180, height: 1000 }); await openMap(page); await page.keyboard.press('h');
  const size = (await page.locator('.map-grid').boundingBox())!.width;
  await begin(page, -size * .65, 0); await page.mouse.up(); await expect(acre(page)).toHaveValue('11');
  await page.keyboard.press('v');
  const box = (await page.locator('.map-grid').boundingBox())!;
  await page.mouse.click(box.x - size * .1, box.y + size * (8.5 / 16));
  await expect(acre(page)).toHaveValue('10');
  await expect(page.locator('.map-grid button.selected')).toHaveAttribute('aria-label', '14, 8');
  expect(await messages(page, 'editMap')).toHaveLength(0);
});
