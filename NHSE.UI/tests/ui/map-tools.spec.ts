import { test, expect, type Page } from '@playwright/test';
import { mockHost } from './mock-host';
const payloads = (page: Page, command: string) => page.evaluate(command =>
  (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command).map(m => m.payload), command);
async function openMap(page: Page) {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  return page.locator('.map-page');
}
test('brush uses pending terrain fields, batches require confirmation, and activity flags share undo', async ({ page }) => {
  const map = await openMap(page);
  await map.getByRole('spinbutton', { name: '高度', exact: true }).fill('3');
  await map.locator('.map-brush summary').click();
  await map.getByRole('spinbutton', { name: '笔刷大小', exact: true }).fill('2');
  await map.getByRole('button', { name: '应用笔刷', exact: true }).click();
  expect((await payloads(page, 'paintMap'))[0]).toMatchObject({ radius: 2, values: { 'Map.Elevation': 3 } });
  await map.locator('.map-batch summary').click();
  await map.locator('.map-batch').getByRole('combobox', { name: '操作', exact: true }).selectOption('zeroElevation');
  await map.locator('.map-batch').getByRole('combobox', { name: '范围', exact: true }).selectOption({ label: '整个图层' });
  await map.getByRole('checkbox', { name: '跳过地形最外围一圈分区', exact: true }).uncheck();
  await map.getByRole('button', { name: '检查操作', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  expect(await payloads(page, 'batchMap')).toHaveLength(0);
  await map.getByRole('button', { name: '检查操作', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('整个图层');
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(map.getByRole('spinbutton', { name: '高度', exact: true })).toHaveValue('0');
  expect((await payloads(page, 'batchMap'))[0]).toMatchObject({ whole: true, interiorOnly: false, operation: 'zeroElevation' });
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(map.getByRole('spinbutton', { name: '高度', exact: true })).toHaveValue('3');
  await map.getByRole('button', { name: '物品', exact: true }).click();
  const active = map.getByRole('checkbox', { name: '物品活动标记', exact: true });
  await active.check(); await expect(active).toBeChecked();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(active).not.toBeChecked();
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(active).toBeChecked();
  await map.locator('.map-controls').getByRole('combobox', { name: '物品图层', exact: true }).selectOption('1');
  await expect(active).not.toBeChecked();
});
test('map files cover six formats, preserve selected layer, and cancellation or errors leave history unchanged', async ({ page }) => {
  const map = await openMap(page);
  const files = map.locator('.map-files');
  await files.locator('summary').click();
  for (const resource of ['items', 'terrain', 'buildings', 'acres', 'flags', 'designs']) {
    await files.getByRole('combobox', { name: '数据', exact: true }).selectOption(resource);
    await files.getByRole('button', { name: '导出地图文件', exact: true }).click();
  }
  expect((await payloads(page, 'mapExport')).map(p => p.resource)).toEqual(['items', 'terrain', 'buildings', 'acres', 'flags', 'designs']);
  await files.getByRole('combobox', { name: '数据', exact: true }).selectOption('flags');
  await files.getByRole('combobox', { name: '物品图层', exact: true }).selectOption('1');
  await page.evaluate(() => Object.assign(window, { nhseMapCancelNext: true }));
  await files.getByRole('button', { name: '导入地图文件', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  expect((await payloads(page, 'mapImport'))[0]).toMatchObject({ resource: 'flags', whole: true, target: { layer: 1 } });
  await page.evaluate(() => Object.assign(window, { nhseMapErrorNext: 'mapFileSize' }));
  await files.getByRole('button', { name: '导入地图文件', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.getByText('文件大小不符合支持的地图格式。', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await files.getByRole('button', { name: '导入地图文件', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  await files.getByRole('button', { name: '保存地图 PNG', exact: true }).click();
  expect(await payloads(page, 'mapPng')).toHaveLength(1);
  await page.setViewportSize({ width: 390, height: 900 });
  const dimensions = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
  expect(dimensions[0]).toBeLessThanOrEqual(dimensions[1]!);
});
