import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';
const commands = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string }[] }).nhseTestPayloads.filter(x => x.command === command), command);
async function open(page: Page) {
  await mockHost(page); await page.goto('/'); await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  return page.locator('.map-page');
}
test('batch review reports impact and rejects blocked or unchanged operations without a write', async ({ page }) => {
  const map = await open(page);
  await map.locator('.map-batch summary').click();
  await map.locator('.map-batch').getByRole('button', { name: '检查操作', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('匹配 2 条 · 改动 2 格 · 涉及 1 个原有物品');
  expect(await commands(page, 'batchMap')).toHaveLength(0);
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
  for (const valid of [true, false]) {
    await page.evaluate(valid => Object.assign(window, { nhseBatchPreviewNext: { valid, error: valid ? undefined : 'roomPlacementBlocked', placements: 2, changedCells: 0, replacedItems: 0, cells: [] } }), valid);
    await map.locator('.map-batch').getByRole('button', { name: '检查操作', exact: true }).click();
    await expect(dialog.locator('.map-impact-review')).toHaveAttribute('aria-busy', 'false');
    await expect(dialog.getByRole('button', { name: '应用修改', exact: true })).toBeDisabled();
    await dialog.getByRole('button', { name: '取消', exact: true }).click();
  }
  expect(await commands(page, 'batchMap')).toHaveLength(0);
});
test('parameter edits and replacements obtain read-only impact before confirmation', async ({ page }) => {
  const map = await open(page);
  await map.getByRole('button', { name: '物品', exact: true }).click();
  await map.locator('.map-parameters summary').click();
  await map.locator('.map-parameters').getByRole('button', { name: '检查操作', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('匹配 2 条 · 改动 2 格');
  expect(await commands(page, 'editMapParameters')).toHaveLength(0);
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
  await map.locator('.map-replace').getByRole('button').first().click();
  await expect(dialog).toContainText('匹配 2 条 · 改动 2 格');
  expect(await commands(page, 'previewReplaceMapMatches')).toHaveLength(1);
  expect(await commands(page, 'replaceMapMatches')).toHaveLength(0);
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
});
for (const width of [1180, 390]) test(`paste conflicts keep canvas geometry stable at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  const map = await open(page), grid = map.locator('.map-grid');
  await map.getByRole('spinbutton', { name: '高度', exact: true }).fill('2');
  await map.getByRole('button', { name: '应用修改', exact: true }).click();
  await grid.focus(); await page.keyboard.press('Control+c');
  await expect(map.getByText(/剪贴板：1 条/)).toBeVisible();
  await grid.getByRole('button', { name: '4, 4', exact: true }).click();
  const before = await grid.boundingBox();
  await page.evaluate(() => Object.assign(window, { nhsePreviewNext: { valid: false, error: 'roomPlacementBlocked', placements: 0, changedCells: 0, replacedItems: 0, cells: [], conflicts: [{ x: 4, y: 4 }] } }));
  await page.keyboard.press('Control+v');
  const panel = map.locator('.map-paste-preview');
  await expect(panel).toHaveAttribute('aria-busy', 'false');
  await expect(panel.getByRole('button', { name: /定位冲突/ })).toBeVisible();
  await expect(grid.getByRole('button', { name: '4, 4', exact: true })).toHaveClass(/paste-conflict/);
  const after = await grid.boundingBox();
  for (const key of ['width', 'height', 'x', 'y'] as const) expect(after![key]).toBeCloseTo(before![key], 1);
  expect(await commands(page, 'pasteGrid')).toHaveLength(0);
  await page.keyboard.press('Escape'); await expect(grid).toBeFocused();
});
