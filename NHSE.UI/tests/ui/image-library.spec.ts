import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';
const messages = (page: Page, command: string) => page.evaluate(command => (window as unknown as { nhseTestPayloads: { command: string; payload: Record<string, unknown> }[] }).nhseTestPayloads.filter(m => m.command === command), command);
async function open(page: Page, save = false) {
  await mockHost(page); await page.goto('/');
  if (save) { await page.getByRole('button', { name: '打开存档', exact: true }).click(); await page.locator('.sidebar nav').getByRole('button', { name: '岛屿工具', exact: true }).click(); }
  else await page.locator('.top-actions').getByRole('button', { name: '应用设置', exact: true }).click();
}
async function start(page: Page) {
  await page.locator('.image-library-tools').getByRole('button', { name: '下载并安装', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '下载并安装', exact: true }).click();
}
async function emit(page: Page, phase: string, error?: string) { await page.evaluate(({ phase, error }) => (window as unknown as { nhseImageEmit: (phase: string, error?: string) => void }).nhseImageEmit(phase, error), { phase, error }); }

test('image library works before opening a save, checks metadata, and reviews downloads before starting', async ({ page }) => {
  await open(page); const library = page.locator('.image-library-tools');
  await expect(library).toContainText('已安装图片文件: 2');
  await library.getByRole('button', { name: '检查下载大小', exact: true }).click(); await expect(library).toContainText('2.1 MB');
  expect(await messages(page, 'imageStart')).toHaveLength(0);
  await library.getByRole('button', { name: '下载并安装', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('https://example.test/images.zip');
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click(); expect(await messages(page, 'imageStart')).toHaveLength(0);
  await page.evaluate(() => Object.assign(window, { nhseImagesUnknownTotal: true }));
  await start(page); await expect(library.getByRole('progressbar')).toBeVisible();
  await expect(library.getByRole('progressbar')).not.toHaveAttribute('value'); await expect(library.getByRole('button', { name: '取消下载', exact: true })).toBeEnabled();
});

test('download progress survives navigation and never changes save or undo state', async ({ page }) => {
  await open(page, true); await start(page); await emit(page, 'downloading');
  await expect(page.getByRole('progressbar')).toHaveAttribute('value', '50');
  await page.locator('.sidebar nav').getByRole('button', { name: '地图与建筑', exact: true }).click(); await expect(page.locator('.map-page')).toBeVisible();
  await page.locator('.top-actions').getByRole('button', { name: '图片下载进行中', exact: true }).click();
  await expect(page.getByRole('progressbar')).toHaveAttribute('value', '50');
  await emit(page, 'complete'); await expect(page.locator('.image-library-tools')).toContainText('已安装图片文件: 10');
  await expect(page.locator('.image-library-tools')).toContainText('重新进入物品或地图页面');
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled();
  expect(await messages(page, 'save')).toHaveLength(0); expect(await messages(page, 'legacy')).toHaveLength(0);
});

test('cancel and failed packages preserve installed counts and support retry', async ({ page }) => {
  await open(page); await start(page); const library = page.locator('.image-library-tools');
  await library.getByRole('button', { name: '取消下载', exact: true }).click(); await expect(library.locator('.images-status')).toContainText('已取消');
  await expect(library).toContainText('已安装图片文件: 2');
  await start(page); await emit(page, 'failed', 'imagesInvalidPackage'); await expect(library.getByRole('alert')).toContainText('现有图片库已保留');
  await expect(library.getByRole('button', { name: '下载并安装', exact: true })).toBeEnabled(); await expect(library).toContainText('已安装图片文件: 2');
  await start(page); await emit(page, 'installing'); await expect(library.getByRole('button', { name: '取消下载', exact: true })).toBeDisabled();
  await emit(page, 'complete'); await expect(library.locator('.images-status')).toContainText('安装完成');
});

test('library controls and phase messages stay localized and fit narrow screens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 950 }); await open(page);
  for (const code of ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht']) {
    const text = JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.locator('.image-library-tools h2')).toHaveText(text.imagesTitle);
    await expect(page.locator('.image-library-tools').getByRole('button', { name: text.imagesDownload, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});
