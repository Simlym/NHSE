import { test, expect } from '@playwright/test';
import { mockHost } from './mock-host';

test('map drafts apply before cell navigation, undo restores values, and both item layers are accessible', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page');
  await expect(map.locator('.map-grid button')).toHaveCount(256);
  await map.getByText('高度', { exact: true }).locator('..').locator('input').fill('2');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(map.getByRole('spinbutton', { name: 'Elevation', exact: true })).toHaveValue('2');
  await page.locator('.icon-select select').first().selectOption('zhs');
  await map.locator('.map-grid button').nth(35).click();
  await expect(map.locator('.map-grid button.selected')).toHaveAttribute('aria-label', '3, 2');
  await map.locator('.map-grid button').first().click();
  await expect(map.getByText('高度', { exact: true }).locator('..').locator('input')).toHaveValue('2');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(map.getByText('高度', { exact: true }).locator('..').locator('input')).toHaveValue('0');
  await map.getByRole('button', { name: '物品', exact: true }).click();
  await expect(map.locator('.map-grid button')).toHaveCount(1024);
  await map.getByRole('combobox', { name: '物品图层', exact: true }).selectOption('1');
  await map.getByRole('combobox', { name: '户外对象', exact: true }).selectOption('60000');
  await map.getByRole('button', { name: '应用修改', exact: true }).click();
  const targets = await page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string; payload: { target: { layer: number } } }[] }).nhseTestPayloads.filter(m => m.command === 'editMap').map(m => m.payload.target));
  expect(targets.at(-1)?.layer).toBe(1);
  await map.getByRole('button', { name: '建筑', exact: true }).click();
  await map.getByRole('combobox', { name: '建筑', exact: true }).selectOption('45');
  await expect(map.getByText('标志位', { exact: true }).locator('..').locator('input')).toHaveAttribute('min', '-128');
});

for (const width of [1180, 800, 390]) {
  test(`map selection follows scaled overview at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockHost(page);
    await page.goto('/');
    await page.getByRole('button', { name: '打开存档', exact: true }).click();
    await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
    const overview = page.locator('.map-overview');
    const box = (await overview.boundingBox())!;
    await overview.click({ position: { x: box.width - 1, y: box.height - 1 } });
    await expect(page.locator('.map-controls label').filter({ hasText: '分区' }).locator('select')).toHaveValue('71');
    const dimensions = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
    expect(dimensions[0]).toBeLessThanOrEqual(dimensions[1]!);
  });
}
