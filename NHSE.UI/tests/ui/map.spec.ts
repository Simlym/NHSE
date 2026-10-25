import { test, expect } from '@playwright/test';
import { mockHost } from './mock-host';

test('building labels identify buildings and navigate to their acre', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page');
  await expect(map.locator('.map-building-pin')).toHaveCount(2);
  await map.locator('.map-overview').getByRole('button', { name: '3 · 博物馆', exact: true }).click();
  await expect(map.getByRole('combobox', { name: '建筑', exact: true })).toHaveValue('2');
  await expect(map.getByRole('combobox', { name: '分区', exact: true })).toHaveValue('20');
  await expect(map.locator('.map-building-label')).toHaveText('3 · 博物馆');
  await expect(map.locator('.map-building-pin.active')).toHaveText('3');
  await map.locator('.map-building-list').getByRole('button', { name: '6 · 狸克商店', exact: true }).click();
  await expect(map.getByRole('combobox', { name: '建筑', exact: true })).toHaveValue('5');
  await expect(map.getByRole('combobox', { name: '分区', exact: true })).toHaveValue('30');
});

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
  await map.getByRole('button', { name: '应用替换', exact: true }).click();
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
    let box = (await overview.boundingBox())!;
    await overview.click({ position: { x: box.width - 1, y: box.height - 1 } });
    await expect(page.locator('.map-controls label').filter({ hasText: '分区' }).locator('select')).toHaveValue('71');
    // Drag the current frame without invoking HTML image/file dragging.
    const image = page.locator('.map-overview-image');
    await image.scrollIntoViewIfNeeded();
    box = (await image.boundingBox())!;
    await page.mouse.move(box.x + box.width * 8.5 / 9, box.y + box.height * 7.5 / 8);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width * 4.5 / 9, box.y + box.height * 3.5 / 8, { steps: 8 });
    await expect(image).toHaveClass(/is-dragging/);
    await expect(page.locator('.acre-outline')).toHaveAttribute('style', /44.444/);
    await expect(page.locator('.drop-overlay')).toHaveCount(0);
    await page.mouse.up();
    await expect(page.locator('.map-controls label').filter({ hasText: '分区' }).locator('select')).toHaveValue('31');
    await page.mouse.move(box.x + 1, box.y + 1);
    await page.mouse.down();
    await image.dispatchEvent('pointercancel', { pointerId: 1 });
    await page.mouse.up();
    await expect(image).not.toHaveClass(/is-dragging/);
    await expect(page.locator('.map-controls label').filter({ hasText: '分区' }).locator('select')).toHaveValue('31');
    const dimensions = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
    expect(dimensions[0]).toBeLessThanOrEqual(dimensions[1]!);
  });
}

test('internal image drags do not open the save-file drop overlay', async ({ page }) => {
  await mockHost(page); await page.goto('/');
  const transfer = await page.evaluateHandle(() => {
    const transfer = new DataTransfer(); transfer.setData('text/uri-list', 'data:image/png;base64,AA=='); return transfer;
  });
  await page.locator('.app').dispatchEvent('dragover', { dataTransfer: transfer });
  await expect(page.locator('.drop-overlay')).toHaveCount(0);
  await page.locator('.app').dispatchEvent('drop', { dataTransfer: transfer });
  expect(await page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string }[] }).nhseTestPayloads.filter(p => p.command === 'drop'))).toHaveLength(0);
  const file = await page.evaluateHandle(() => {
    const transfer = new DataTransfer(); transfer.items.add(new File(['fixture'], 'main.dat')); return transfer;
  });
  await page.locator('.app').dispatchEvent('dragover', { dataTransfer: file });
  await expect(page.locator('.drop-overlay')).toBeVisible();
});

test('map replacement is visible and uses the selected item with the new fields', async ({ page }) => {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page');
  await map.getByRole('button', { name: '物品', exact: true }).click();
  await map.locator('.map-grid button').nth(67).click();
  await map.getByRole('combobox', { name: '户外对象', exact: true }).selectOption('60000');
  await map.getByRole('button', { name: '应用替换', exact: true }).click();
  await map.getByRole('button', { name: '选择物品', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '木材', exact: true }).click();
  const replace = map.locator('.map-replace');
  await expect(replace).toBeVisible();
  await replace.getByRole('combobox', { name: '范围', exact: true }).selectOption('true');
  await replace.getByRole('button', { name: '用当前字段替换匹配物品', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: '应用修改', exact: true }).click();
  const payload = await page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string; payload: unknown }[] }).nhseTestPayloads.find(p => p.command === 'replaceMapMatches')?.payload);
  expect(payload).toMatchObject({ target: { x: 3, y: 2, mode: 'items' }, whole: true, remove: false, current: { itemId: 1000 } });
});

test('rapid cell selection stays responsive during a delayed read and reuses map images', async ({ page }) => {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const map = page.locator('.map-page'), grid = map.locator('.map-grid');
  const initialImage = await map.locator('.map-viewport img').first().getAttribute('src');
  await page.evaluate(() => Object.assign(window, { nhseDelayMapNext: 700 }));
  await grid.getByRole('button', { name: '1, 1', exact: true }).click();
  await expect(grid.getByRole('button', { name: '2, 2', exact: true })).toBeEnabled();
  await grid.getByRole('button', { name: '2, 2', exact: true }).click();
  await grid.getByRole('button', { name: '3, 3', exact: true }).click();
  await expect(grid.locator('button.selected')).toHaveAttribute('aria-label', '3, 3');
  await expect(map.getByText('X 3 · Y 3', { exact: true })).toBeVisible();
  await expect(map.locator('.map-viewport img').first()).toHaveAttribute('src', initialImage!);
  const reads = await page.evaluate(() => (window as unknown as { nhseTestPayloads: { command: string; payload: { includeImages?: boolean; target: { x: number; y: number } } }[] }).nhseTestPayloads.filter(p => p.command === 'map').map(p => p.payload));
  expect(reads.at(-1)).toMatchObject({ includeImages: false, target: { x: 3, y: 3 } });
});

test('overview frame moves continuously inside an acre without loading or snapping back', async ({ page }) => {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click();
  const overview = page.locator('.map-overview-image');
  await overview.scrollIntoViewIfNeeded();
  const box = (await overview.boundingBox())!;
  await page.mouse.move(box.x + box.width * 1.5 / 9, box.y + box.height * 1.5 / 8);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 1.75 / 9, box.y + box.height * 1.5 / 8);
  await expect.poll(async () => parseFloat(await page.locator('.acre-outline').evaluate(el => (el as HTMLElement).style.left))).toBeCloseTo(1.25 / 9 * 100, 1);
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect(page.locator('.map-controls label').filter({ hasText: '分区' }).locator('select')).toHaveValue('10');
});
