import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';

const codes = ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht'];
const text = (code: string) =>
  JSON.parse(
    fs.readFileSync(
      new URL(`../../src/locales/${code}.json`, import.meta.url),
      'utf8',
    ),
  );

async function mockHost(page: Page) {
  await page.addInitScript(() => {
    const messages: string[] = [];
    const listeners: ((event: { data: unknown }) => void)[] = [];
    let opened = false,
      locale = 'zhs',
      dirty = false;
    const item = {
      index: 0,
      itemId: 1000,
      name: '木材',
      empty: false,
      available: true,
      quantity: 10,
      maxQuantity: 30,
      image: null,
    };
    const snapshot = () => ({
      language: locale,
      recent: 'C:\\Example\\Save',
      fullScreen: false,
      session: opened
        ? {
            title: 'NHSE - Test',
            island: '忘忧',
            dirty,
            canUndo: dirty,
            canRedo: !dirty,
            players: [
              {
                index: 0,
                name: '夏夏',
                island: '忘忧',
                wallet: 100,
                bank: 200,
                miles: 300,
                earnedMiles: 400,
                pocketCapacity: 40,
                storageCapacity: 2400,
              },
            ],
          }
        : null,
    });
    Object.assign(window, { nhseTestMessages: messages });
    Object.assign(window, {
      chrome: {
        webview: {
          addEventListener: (_: string, fn: (event: { data: unknown }) => void) => listeners.push(fn),
          postMessage: (message: { id: string; command: string; payload: Record<string, unknown> }) => {
            messages.push(message.command);
            const p = message.payload;
            let data: unknown;
            if (message.command === 'language') locale = String(p.language);
            if (message.command === 'open' || message.command === 'openRecent') opened = true;
            if (message.command === 'setItem') {
              item.quantity = Number(p.quantity);
              dirty = true;
            }
            if (message.command === 'deleteItem') {
              item.empty = true;
              dirty = true;
            }
            if (message.command === 'undo') {
              item.quantity = 10;
              item.empty = false;
              dirty = false;
            }
            if (message.command === 'save') dirty = false;
            if (message.command === 'closeSave') opened = false;
            if (message.command === 'inventory') {
              const items = Array.from({ length: 40 }, (_, index) =>
                index === 0
                  ? { ...item }
                  : {
                      index,
                      itemId: 65534,
                      name: '',
                      empty: true,
                      available: index < 30,
                      quantity: 0,
                      maxQuantity: 1,
                      image: null,
                    },
              );
              data = { page: 0, pages: 1, total: 40, used: 1, matched: 40, items };
            } else if (message.command === 'catalog') data = [{ ...item }];
            else data = snapshot();
            queueMicrotask(() => listeners.forEach((fn) => fn({ data: { id: message.id, ok: true, data } })));
          },
        },
      },
    });
  });
}

test('home keeps all nine languages and remains usable at desktop / compact widths', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  for (const code of codes) {
    const t = text(code);
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(t.homeTitle);
    await expect(page.getByRole('button', { name: t.openSave, exact: true })).toBeEnabled();
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 850 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
  await page.setViewportSize({ width: 1180, height: 800 });
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.screenshot({ path: 'test-results/home-zhs.png', fullPage: true });
  await page.getByRole('button', { name: text('zhs').howTitle, exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('ordinary slot selection, quantity changes, undo, saved state and locked slots', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.getByRole('button', { name: '背包与仓库', exact: true }).click();
  await expect(page.locator('.item-slot')).toHaveCount(40);
  await expect(page.locator('.item-slot').nth(30)).toBeDisabled();
  await page.locator('.item-slot').first().click();
  await expect(page.locator('.inspector h3')).toHaveText('木材');
  await page.getByRole('spinbutton', { name: '数量', exact: true }).fill('15');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.locator('.statusbar')).toContainText('有未保存的修改');
  await expect(page.locator('.item-slot').first()).toContainText('15');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.locator('.item-slot').first()).toContainText('10');
  await page.getByRole('button', { name: '选择物品', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('button', { name: '木材', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await page.getByRole('button', { name: '专注模式', exact: true }).click();
  await expect(page.locator('.sidebar')).toHaveCount(0);
  await expect(page.locator('.focus-nav')).toBeVisible();
  await page.screenshot({ path: 'test-results/inventory-zhs.png', fullPage: true });
});

test('frontend does not invoke the host for an invalid quantity', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.getByRole('button', { name: '背包与仓库', exact: true }).click();
  await page.locator('.item-slot').first().click();
  await page.getByRole('spinbutton', { name: '数量', exact: true }).fill('999');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('setItem'),
    ),
  ).toBe(false);
});
