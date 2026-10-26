import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';

const text = (code: string) => JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
const pageKeys = ['overview', 'player', 'inventory', 'progress', 'designs', 'mapEditor', 'housing', 'villagers', 'island', 'museum', 'tools', 'advancedEditor', 'consoleTools'];
async function open(page: Page) {
  await mockHost(page); await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
}

test('grouped navigation preserves every entry and its translated accessible group across widths', async ({ page }) => {
  await open(page);
  const nav = page.locator('.sidebar nav');
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    await page.locator('.icon-select select').first().selectOption(code);
    const t = text(code);
    await expect(nav.getByRole('button')).toHaveCount(13);
    for (const key of pageKeys) await expect(nav.getByRole('button', { name: t[key], exact: true })).toHaveCount(1);
    for (const key of ['navPlayerGroup', 'navIslandGroup', 'navToolsGroup']) await expect(nav.getByRole('group', { name: t[key], exact: true })).toHaveCount(1);
    await expect(nav.locator('[aria-current=page]')).toHaveCount(1);
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 800 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      if (width === 390) {
        const sidebar = await page.locator('.sidebar').boundingBox();
        expect(sidebar!.height).toBeLessThan(145);
      }
    }
  }
});

test('compact navigation scrolls focused entries into view and keeps the active page visible', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 });
  await open(page);
  const nav = page.locator('.sidebar nav');
  const consoleTools = nav.getByRole('button', { name: text('zhs').consoleTools, exact: true });
  await consoleTools.focus(); await page.keyboard.press('Enter');
  await expect(consoleTools).toHaveAttribute('aria-current', 'page');
  const bounds = await consoleTools.boundingBox(), viewport = await nav.boundingBox();
  expect(bounds!.x).toBeGreaterThanOrEqual(viewport!.x - 1);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport!.x + viewport!.width + 1);
  await nav.getByRole('button', { name: text('zhs').overview, exact: true }).click();
  await expect(nav.getByRole('button', { name: text('zhs').overview, exact: true })).toHaveAttribute('aria-current', 'page');
  expect(await nav.evaluate(el => el.scrollLeft)).toBeLessThan(5);
  await page.screenshot({ path: 'test-results/navigation-compact-zhs.png' });
});

test('cross-group navigation keeps invalid drafts on the actual page and applies valid drafts', async ({ page }) => {
  await open(page);
  const nav = page.locator('.sidebar nav');
  await nav.getByRole('button', { name: '玩家资料', exact: true }).click();
  const name = page.getByRole('textbox', { name: '玩家名字', exact: true });
  await name.fill('');
  await nav.getByRole('button', { name: '岛屿工具', exact: true }).click();
  await expect(nav.getByRole('button', { name: '玩家资料', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(name).toHaveValue('');
  await name.fill('新玩家');
  await nav.getByRole('button', { name: '岛屿工具', exact: true }).click();
  await expect(nav.getByRole('button', { name: '岛屿工具', exact: true })).toHaveAttribute('aria-current', 'page');
  await nav.getByRole('button', { name: '玩家资料', exact: true }).click();
  await expect(name).toHaveValue('新玩家');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(name).toHaveValue('夏夏');
});
