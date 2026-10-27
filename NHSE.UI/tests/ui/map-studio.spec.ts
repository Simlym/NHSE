import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';

async function openMap(page: Page) {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  return page.locator('.map-page');
}
const writes = (page: Page) => page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string }[] }).nhseTestPayloads.filter(p => ['editMap', 'editGrid', 'paintMapStroke'].includes(p.command)));

test('canvas zoom, hand panning and visibility controls preserve the map and selected cell', async ({ page }) => {
  await page.setViewportSize({ width: 1180, height: 900 });
  const map = await openMap(page);
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  await map.getByRole('button', { name: '放大画布', exact: true }).click();
  await expect(map.locator('.canvas-zoom output')).toHaveText('200%');
  const scroll = map.locator('.map-canvas-scroll');
  const before = await scroll.evaluate(el => ({ left: el.scrollLeft, top: el.scrollTop }));
  await map.getByRole('button', { name: '拖动画布', exact: true }).click();
  const box = (await scroll.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 - 65, box.y + box.height / 2 - 50, { steps: 8 });
  await page.mouse.up();
  const after = await scroll.evaluate(el => ({ left: el.scrollLeft, top: el.scrollTop }));
  expect(after.left).toBeGreaterThan(before.left + 40);
  expect(after.top).toBeGreaterThan(before.top + 30);
  await expect(map.locator('.map-grid button.selected')).toHaveAttribute('aria-label', '0, 0');
  await map.getByRole('button', { name: '显示网格', exact: true }).click();
  await expect(map.locator('.map-grid-lines')).toBeHidden();
  await map.getByRole('button', { name: '显示物品图层', exact: true }).click();
  await expect(map.locator('.map-item-layer')).toBeHidden();
  expect(await writes(page)).toHaveLength(0);
  await map.getByRole('button', { name: '选择格子', exact: true }).click();
  await map.locator('.map-grid button').nth(85).click();
  await expect(map.locator('.map-grid button.selected')).toHaveAttribute('aria-label', '5, 5');
  await map.getByRole('button', { name: '适应当前分区', exact: true }).click();
  await expect(map.locator('.canvas-zoom output')).toHaveText('100%');
  const fit = await scroll.evaluate(el => ({ left: el.scrollLeft, top: el.scrollTop, width: el.clientWidth, height: el.clientHeight, map: el.querySelector('.map-viewport')!.getBoundingClientRect().width }));
  expect(fit.left).toBe(0); expect(fit.top).toBe(0);
  expect(fit.map).toBeLessThan(Math.min(fit.width, fit.height));
});

test('toolbar activates brush settings and building search follows the selected building', async ({ page }) => {
  const map = await openMap(page);
  await map.getByRole('button', { name: '拖拽地形笔刷', exact: true }).click();
  await expect(map.locator('.map-brush')).toHaveAttribute('open', '');
  await expect(map.getByRole('checkbox', { name: '拖拽地形笔刷', exact: true })).toBeChecked();
  await map.getByRole('button', { name: '建筑', exact: true }).click();
  await map.getByRole('searchbox', { name: '搜索建筑', exact: true }).fill('商店');
  await expect(map.locator('.map-building-list button')).toHaveCount(1);
  await map.locator('.map-building-list button').click();
  await expect(map.getByRole('combobox', { name: '建筑', exact: true })).toHaveValue('5');
  await expect(map.getByRole('combobox', { name: '分区', exact: true })).toHaveValue('30');
  await expect(map.locator('.selection-card')).toContainText('狸克商店');
  expect(await writes(page)).toHaveLength(0);
});
