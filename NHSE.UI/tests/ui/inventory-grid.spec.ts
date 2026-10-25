import { test, expect } from '@playwright/test';
import { mockHost } from './mock-host';

for (const width of [1180, 390]) {
  test(`inventory selection clipboard undo and locked slots at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockHost(page); await page.goto('/');
    await page.getByRole('button', { name: '打开存档', exact: true }).click();
    await page.locator('.sidebar nav').getByRole('button', { name: '背包与仓库', exact: true }).click();
    const grid = page.locator('.inventory-selection-grid');
    await grid.locator('button').first().click(); await page.keyboard.press('Control+c');
    await expect(page.getByText('剪贴板：1 条 · 1 × 1', { exact: true })).toBeVisible();
    await grid.locator('button').nth(1).click();
    await grid.locator('button').nth(2).click({ modifiers: ['Control'] });
    await expect(page.getByText('已选 2 格', { exact: true })).toBeVisible();
    await page.keyboard.press('Control+v');
    await expect(grid.locator('.is-empty')).toHaveCount(37);
    await page.keyboard.press('Control+z'); await expect(grid.locator('.is-empty')).toHaveCount(39);
    await page.keyboard.press('Control+y'); await expect(grid.locator('.is-empty')).toHaveCount(37);
    await grid.focus(); await page.keyboard.press('Control+a');
    await expect(page.getByText('已选 30 格', { exact: true })).toBeVisible();
    await expect(grid.locator('button:disabled')).toHaveCount(10);
    await page.keyboard.press('Delete'); await expect(grid.locator('.is-empty')).toHaveCount(40);
    await page.keyboard.press('Control+z'); await expect(grid.locator('.is-empty')).toHaveCount(37);
    await grid.focus(); await page.keyboard.press('Escape');
    await expect(page.getByText('已选 0 格', { exact: true })).toBeVisible();
    const first = (await grid.locator('button').first().boundingBox())!;
    const columns = Number(await grid.getAttribute('data-columns'));
    const last = (await grid.locator('button').nth(columns + 1).boundingBox())!;
    await page.mouse.move(first.x + first.width / 2, first.y + first.height / 2); await page.mouse.down();
    await page.mouse.move(last.x + last.width / 2, last.y + last.height / 2); await page.mouse.up();
    await expect(page.getByText('已选 4 格', { exact: true })).toBeVisible();
    const search = page.getByRole('searchbox', { name: '查找背包或仓库中的物品' });
    await search.fill('木材'); await search.press('Control+a'); await search.press('Backspace');
    await expect(grid.locator('.is-empty')).toHaveCount(37);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
