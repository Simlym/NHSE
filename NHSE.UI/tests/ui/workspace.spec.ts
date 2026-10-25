import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockHost } from './mock-host';

const codes = ['en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht'];
const text = (code: string) =>
  JSON.parse(fs.readFileSync(new URL(`../../src/locales/${code}.json`, import.meta.url), 'utf8'));

test('resident profile and embedded gallery apply edits, cancel, replace and undo in the workspace', async ({
  page,
}) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛民管理', exact: true }).click();
  await expect(page.locator('.resident-profile h2')).toHaveText('岛民001');
  await page.getByRole('textbox', { name: '口头禅', exact: true }).fill('新口头禅');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(page.getByRole('textbox', { name: 'Catchphrase', exact: true })).toHaveValue('新口头禅');
  await page.locator('.icon-select select').first().selectOption('zhs');
  await page.locator('.residents-page .resident-chip').nth(1).click();
  await expect(page.locator('.resident-profile h2')).toHaveText('岛民002');
  await page.locator('.residents-page .resident-chip').first().click();
  await expect(page.getByRole('textbox', { name: '口头禅', exact: true })).toHaveValue('新口头禅');
  await page.getByRole('button', { name: '选择岛民', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('replaceResident'),
    ),
  ).toBe(false);
  await page.getByRole('button', { name: '选择岛民', exact: true }).click();
  await page.getByRole('searchbox', { name: '搜索岛民名字或编号' }).fill('v0004');
  await expect(page.locator('.villager-tile')).toHaveCount(1);
  await page.locator('.villager-tile').click();
  await page.getByRole('button', { name: '确认替换', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.resident-profile h2')).toHaveText('岛民005');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.locator('.resident-profile h2')).toHaveText('岛民001');
  await expect(page.getByRole('textbox', { name: '口头禅', exact: true })).toHaveValue('新口头禅');
  await page.screenshot({ path: 'test-results/resident-workspace-zhs.png', fullPage: true });
});

test('island settings validate unsigned seeds, autosave drafts and support undo in every language', async ({
  page,
}) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  await page.getByRole('radio', { name: '绿色', exact: true }).check();
  await page.getByRole('combobox', { name: '岛屿所在半球', exact: false }).selectOption('1');
  const seed = page.getByRole('spinbutton', { name: '岛屿天气种子', exact: true });
  await seed.fill('777');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(page.getByRole('spinbutton', { name: 'Island weather seed', exact: true })).toHaveValue('777');
  await page.locator('.icon-select select').first().selectOption('zhs');
  await seed.fill('4294967296');
  await page.locator('.island-form').getByRole('button', { name: '应用修改', exact: true }).click();
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('setIsland'),
    ),
  ).toBe(false);
  await seed.fill('4294967295');
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(seed).toHaveValue('12345');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(seed).toHaveValue('4294967295');
  await page.locator('.workspace-content').evaluate((el) => el.scrollTo(0, 0));
  await page.screenshot({ path: 'test-results/island-workspace-zhs.png', fullPage: true });
  for (const code of codes) {
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(text(code).island);
    await expect(page.locator('.airport-colors label')).toHaveCount(4);
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
});

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
  await expect(page.getByRole('button', { name: '专注模式', exact: true })).toHaveCount(0);
  await expect(page.locator('.sidebar')).toBeVisible();
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

test('player names and island names remain editable with avatar switching and undo', async ({ page }) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await expect(page.locator('.player-chip')).toHaveCount(2);
  await expect(page.locator('.player-chip').first().locator('img')).toBeVisible();
  await expect(page.locator('.workspace-controls select')).toHaveCount(0);
  await page.getByRole('button', { name: '玩家资料', exact: true }).click();
  await page.getByRole('textbox', { name: '玩家名字', exact: true }).fill('新玩家');
  await page.getByRole('textbox', { name: '岛屿名称', exact: true }).fill('新岛屿');
  await page.getByRole('spinbutton', { name: '背包容量', exact: true }).fill('32');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.locator('.player-chip').first()).toContainText('新玩家');
  await expect(page.locator('.breadcrumb')).toContainText('新岛屿');
  await page.getByRole('button', { name: '玩家资料 小雨', exact: true }).click();
  await expect(page.getByRole('textbox', { name: '玩家名字', exact: true })).toHaveValue('小雨');
  await expect(page.getByRole('textbox', { name: '岛屿名称', exact: true })).toHaveValue('新岛屿');
  await page.getByRole('button', { name: '玩家资料 新玩家', exact: true }).click();
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.getByRole('textbox', { name: '玩家名字', exact: true })).toHaveValue('夏夏');
  await expect(page.getByRole('textbox', { name: '岛屿名称', exact: true })).toHaveValue('忘忧');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(page.getByRole('textbox', { name: '玩家名字', exact: true })).toHaveValue('新玩家');
  await page.getByRole('textbox', { name: '玩家名字', exact: true }).fill('保存时应用');
  await page.getByRole('button', { name: '保存存档', exact: true }).click();
  await expect(page.locator('.player-chip').first()).toContainText('保存时应用');
  await expect(page.locator('.statusbar')).toContainText('修改已保存');
  await expect(page.locator('.player-card')).toHaveCount(3);
  await expect(page.locator('.player-assets')).toContainText('可用狸端里程');
  await expect(page.locator('.player-capacity')).toContainText('仓库容量');
  await expect(page.locator('.player-apply-help')).toContainText('写入存档文件');
  await page.setViewportSize({ width: 1600, height: 1100 });
  await page.screenshot({ path: 'test-results/player-profile-zhs.png' });
  for (const width of [1180, 800, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    for (const input of await page.locator('.player-card input').all()) {
      const bounds = await input.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    }
  }
});

test('catalog exposes all pages, category filters, item-ID search and a dense picture grid', async ({
  page,
}) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.getByRole('button', { name: '背包与仓库', exact: true }).click();
  await page.locator('.item-slot').first().click();
  await page.getByRole('button', { name: '选择物品', exact: true }).click();
  const catalog = page.getByRole('dialog');
  await expect(catalog.locator('.catalog-item')).toHaveCount(96);
  await expect(catalog.locator('.catalog-summary')).toContainText('240');
  await catalog.getByRole('button', { name: '最后一页', exact: true }).click();
  await expect(catalog.locator('.catalog-item')).toHaveCount(48);
  await expect(catalog.getByRole('button', { name: '测试物品240', exact: true })).toBeAttached();
  await catalog.getByRole('button', { name: /服饰与配件/ }).click();
  await expect(catalog.locator('.catalog-item')).toHaveCount(80);
  await expect(catalog.locator('.catalog-summary')).toContainText('服饰与配件');
  await catalog.getByRole('button', { name: /^上装/ }).click();
  await expect(catalog.locator('.catalog-item')).toHaveCount(40);
  await expect(catalog.getByRole('button', { name: /^上装/ })).toHaveAttribute('aria-pressed', 'true');
  await catalog.getByRole('button', { name: /^鞋 / }).click();
  await expect(catalog.locator('.catalog-item')).toHaveCount(40);
  await catalog.getByRole('button', { name: /全部物品/ }).click();
  await catalog.getByRole('spinbutton', { name: '跳至页码', exact: true }).fill('2');
  await catalog.getByRole('button', { name: '前往', exact: true }).click();
  await expect(catalog.locator('.catalog-summary')).toContainText('2 / 3');
  await catalog.getByRole('searchbox', { name: '搜索物品名称或编号', exact: true }).fill('1239');
  await expect(catalog.locator('.catalog-item')).toHaveCount(1);
  await expect(catalog.getByRole('button', { name: '测试物品240', exact: true })).toBeVisible();
  await catalog.getByRole('searchbox', { name: '搜索物品名称或编号', exact: true }).fill('不存在的物品');
  await expect(catalog.locator('.empty-result')).toBeVisible();
  await catalog.getByRole('searchbox', { name: '搜索物品名称或编号', exact: true }).fill('');
  await expect(catalog.locator('.catalog-item')).toHaveCount(96);
  for (const width of [1180, 800, 390]) {
    await page.setViewportSize({ width, height: 850 });
    expect(await catalog.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.screenshot({ path: 'test-results/catalog-zhs.png' });
  await catalog.getByRole('button', { name: '最后一页', exact: true }).click();
  await catalog.getByRole('button', { name: '测试物品240', exact: true }).click();
  await expect(page.locator('.inspector h3')).toHaveText('测试物品240');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await expect(page.locator('.statusbar')).toContainText('有未保存的修改');
});

test('villager gallery filters portraits and previews replacements without mutating on selection', async ({
  page,
}) => {
  await mockHost(page, true);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '选择岛民', exact: true })).toBeVisible();
  await expect(page.locator('.resident-chip')).toHaveCount(2);
  await expect(page.locator('.villager-tile')).toHaveCount(60);
  await expect(page.getByRole('button', { name: '岛民002', exact: true })).toBeDisabled();
  await page
    .getByRole('navigation', { name: '种族', exact: true })
    .getByRole('button', { name: /^猫/ })
    .click();
  await expect(page.locator('.villager-tile')).toHaveCount(47);
  await page.locator('.villager-filters select').selectOption('0');
  await expect(page.locator('.villager-tile')).toHaveCount(6);
  await page.locator('.villager-filters select').selectOption('-1');
  await page.getByRole('searchbox').fill('v0003');
  await expect(page.locator('.villager-tile')).toHaveCount(1);
  await page.locator('.villager-tile').click();
  await expect(page.locator('.villager-preview h2')).toHaveText('岛民004');
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('pickVillager'),
    ),
  ).toBe(false);
  await page.getByRole('button', { name: '确认替换', exact: true }).click();
  expect(
    await page.evaluate(
      () =>
        (
          window as unknown as {
            nhseTestPayloads: { command: string; payload: { id: string; index: number } }[];
          }
        ).nhseTestPayloads.find((x) => x.command === 'pickVillager')?.payload,
    ),
  ).toEqual({ index: 0, id: 'v0003' });
  await page.screenshot({ path: 'test-results/villager-picker-zhs.png' });
  await page.locator('.resident-chip').nth(1).click();
  await page.getByRole('button', { name: '编辑所选岛民', exact: true }).click();
  expect(
    await page.evaluate(
      () =>
        (
          window as unknown as {
            nhseTestPayloads: { command: string; payload: { index: number } }[];
          }
        ).nhseTestPayloads.find((x) => x.command === 'pickResident')?.payload.index,
    ),
  ).toBe(1);
});

test('turnip table validates drafts, saves on navigation and supports undo in nine languages', async ({
  page,
}) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  const form = page.locator('#turnips-editor');
  await expect(form.locator('tbody input')).toHaveCount(14);
  const monday = form.getByRole('spinbutton', { name: '星期一 上午', exact: true });
  await monday.fill('4294967296');
  await form.getByRole('button', { name: '应用修改', exact: true }).click();
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.includes('setTurnips'),
    ),
  ).toBe(false);
  await monday.fill('650');
  await page.locator('.icon-select select').first().selectOption('en');
  await expect(form.getByRole('spinbutton', { name: 'Monday AM', exact: true })).toHaveValue('650');
  await page.keyboard.press('Control+s');
  await expect(page.locator('.statusbar')).toContainText(text('en').saved);
  await page.getByRole('button', { name: text('en').undo, exact: true }).click();
  await expect(form.getByRole('spinbutton', { name: 'Monday AM', exact: true })).toHaveValue('102');
  await page.getByRole('button', { name: text('en').redo, exact: true }).click();
  await expect(form.getByRole('spinbutton', { name: 'Monday AM', exact: true })).toHaveValue('650');
  for (const code of codes) {
    await page.locator('.icon-select select').first().selectOption(code);
    await expect(form.getByRole('heading')).toHaveText(text(code).turnips);
    for (const width of [1180, 800, 390]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  }
  await page.locator('.icon-select select').first().selectOption('zhs');
  await monday.fill('651');
  await page.getByRole('button', { name: '玩家资料', exact: true }).first().click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  await expect(monday).toHaveValue('651');
});

test('recycle shortcut uses the workspace grid, stays shared and supports removal and undo', async ({
  page,
}) => {
  await mockHost(page);
  await page.goto('/');
  await page.getByRole('button', { name: '打开存档', exact: true }).click();
  await page.locator('.sidebar nav').getByRole('button', { name: '岛屿设置', exact: true }).click();
  await page.getByRole('button', { name: '回收箱', exact: true }).click();
  await expect(page.locator('.item-slot')).toHaveCount(40);
  await expect(page.locator('.item-slot').last()).toBeEnabled();
  await page.locator('.item-slot').first().click();
  await page.getByRole('spinbutton', { name: '数量', exact: true }).fill('20');
  await page.getByRole('button', { name: '应用修改', exact: true }).click();
  await page.locator('.player-chip').nth(1).click();
  await expect(page.locator('.item-slot').first()).toContainText('20');
  await page.getByRole('button', { name: '撤销', exact: true }).click();
  await expect(page.locator('.item-slot').first()).toContainText('5');
  await page.getByRole('button', { name: '重做', exact: true }).click();
  await expect(page.locator('.item-slot').first()).toContainText('20');
  await page.locator('.item-slot').first().click();
  await page.getByRole('button', { name: text('zhs').delete, exact: true }).click();
  await expect(page.locator('.item-slot').first()).toHaveClass(/is-empty/);
  expect(
    await page.evaluate(() =>
      (window as unknown as { nhseTestMessages: string[] }).nhseTestMessages.some(
        (x) => x === 'legacy' || x === 'tool',
      ),
    ),
  ).toBe(false);
});
