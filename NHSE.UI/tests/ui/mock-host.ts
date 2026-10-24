import type { Page } from '@playwright/test';

export async function mockHost(page: Page, picker = false) {
  await page.addInitScript((picker) => {
    const messages: string[] = [];
    const listeners: ((event: { data: unknown }) => void)[] = [];
    let opened = false,
      locale = 'zhs',
      revision = 0,
      nextRevision = 0,
      savedRevision = 0;
    const avatar =
      'data:image/svg+xml,' +
      encodeURIComponent(
        '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96"><rect width="96" height="96" fill="#80a787"/><circle cx="48" cy="35" r="18" fill="#fff5d4"/><ellipse cx="48" cy="90" rx="34" ry="30" fill="#fff5d4"/></svg>',
      );
    let players = [
      {
        index: 0,
        name: '夏夏',
        island: '忘忧',
        avatar,
        wallet: 100,
        bank: 200,
        miles: 300,
        earnedMiles: 400,
        pocketCapacity: 30,
        storageCapacity: 2400,
        maxPocketCapacity: 40,
        maxStorageCapacity: 5000,
        hotelTickets: null,
        poki: null,
      },
      {
        index: 1,
        name: '小雨',
        island: '忘忧',
        avatar: null,
        wallet: 11,
        bank: 22,
        miles: 33,
        earnedMiles: 44,
        pocketCapacity: 40,
        storageCapacity: 800,
        maxPocketCapacity: 40,
        maxStorageCapacity: 5000,
        hotelTickets: null,
        poki: null,
      },
    ];
    const catalog = Array.from({ length: 240 }, (_, index) => ({
      index: -1,
      itemId: 1000 + index,
      name: index === 0 ? '木材' : `测试物品${String(index + 1).padStart(3, '0')}`,
      empty: false,
      available: true,
      quantity: 1,
      maxQuantity: index === 0 ? 30 : 1,
      image: null,
      category: ['materials', 'clothing', 'other'][index % 3],
      subcategory: index % 2 ? 'tops' : 'shoes',
    }));
    let item = { ...catalog[0]!, index: 0, quantity: 10 };
    let residents = [0, 1].map((index) => ({
      index,
      id: 'v' + String(index).padStart(4, '0'),
      name: '岛民' + String(index + 1).padStart(3, '0'),
      species: [4, 31][index],
      personality: index,
      image: avatar,
      catchphrase: index ? '喵喵' : '你好',
      defaultCatchphrase: '默认',
      movingOut: false,
      empty: false,
      canReplace: true,
    }));
    let islandSettings = {
      airportColor: 0,
      hemisphere: 0,
      tourHemisphere: 0,
      weatherSeed: 12345,
      tourWeatherSeed: 54321,
    };
    let turnips = {
      buyPrice: 90,
      sellPrices: Array.from({ length: 14 }, (_, i) => 100 + i),
      pattern: 0,
      feverStart: 0,
    };
    let recycleItem = { ...item, quantity: 5 };
    type Snapshot = {
      turnips: typeof turnips;
      recycleItem: typeof recycleItem;
      players: typeof players;
      item: typeof item;
      revision: number;
      residents: typeof residents;
      islandSettings: typeof islandSettings;
    };
    const history: Snapshot[] = [],
      redo: Snapshot[] = [];
    const capture = (): Snapshot =>
      structuredClone({ players, item, revision, residents, islandSettings, turnips, recycleItem });
    const restore = (value: Snapshot) => {
      turnips = value.turnips;
      recycleItem = value.recycleItem;
      players = value.players;
      item = value.item;
      revision = value.revision;
      residents = value.residents;
      islandSettings = value.islandSettings;
    };
    const remember = () => {
      history.push(capture());
      redo.length = 0;
      revision = ++nextRevision;
    };
    const snapshot = () => ({
      villagerPicker: picker
        ? {
            index: 0,
            residents: [
              { index: 0, id: 'v0000', name: '岛民001', image: avatar, empty: false },
              { index: 1, id: 'v0001', name: '岛民002', image: avatar, empty: false },
            ],
          }
        : null,
      language: locale,
      recent: 'C:\\Example\\Save',
      fullScreen: false,
      session: opened
        ? {
            title: 'NHSE - Test',
            island: players[0]!.island,
            dirty: revision !== savedRevision,
            canUndo: history.length > 0,
            canRedo: redo.length > 0,
            players,
            residents,
            islandSettings,
            turnips,
          }
        : null,
    });
    Object.assign(window, { nhseTestMessages: messages, nhseTestPayloads: [] });
    Object.assign(window, {
      chrome: {
        webview: {
          addEventListener: (_: string, fn: (event: { data: unknown }) => void) => listeners.push(fn),
          postMessage: (message: { id: string; command: string; payload: Record<string, unknown> }) => {
            messages.push(message.command);
            (window as unknown as { nhseTestPayloads: unknown[] }).nhseTestPayloads.push(
              structuredClone(message),
            );
            const p = message.payload;
            let data: unknown;
            if (message.command === 'language') locale = String(p.language);
            if (message.command === 'open' || message.command === 'openRecent') opened = true;
            if (message.command === 'setPlayer') {
              remember();
              const player = players[Number(p.player)]!;
              const oldIsland = player.island;
              Object.assign(
                player,
                Object.fromEntries(Object.entries(p).filter(([key]) => key !== 'player')),
              );
              players
                .filter((player) => player.island === oldIsland)
                .forEach((player) => (player.island = String(p.island)));
            }
            if (message.command === 'setItem') {
              remember();
              const replacement = {
                ...catalog.find((entry) => entry.itemId === Number(p.itemId))!,
                index: 0,
                quantity: Number(p.quantity),
              };
              if (p.container === 'recycle') recycleItem = replacement;
              else item = replacement;
            }
            if (message.command === 'setTurnips') {
              remember();
              turnips = {
                buyPrice: Number(p.buyPrice),
                sellPrices: [...(p.sellPrices as number[])],
                pattern: Number(p.pattern),
                feverStart: Number(p.feverStart),
              };
            }
            if (message.command === 'setIsland') {
              remember();
              Object.assign(
                islandSettings,
                turnips,
                Object.fromEntries(Object.entries(p).filter(([key]) => key !== 'player')),
              );
            }
            if (message.command === 'setResident') {
              remember();
              Object.assign(residents[Number(p.index)]!, {
                personality: Number(p.personality),
                catchphrase: String(p.catchphrase),
                movingOut: Boolean(p.movingOut),
              });
            }
            if (message.command === 'replaceResident') {
              remember();
              const id = String(p.id);
              const i = Number(id.slice(1));
              Object.assign(residents[Number(p.index)]!, {
                id,
                name: '岛民' + String(i + 1).padStart(3, '0'),
                species: [4, 31, 34][i % 3],
                personality: i % 8,
                catchphrase: '默认',
                movingOut: false,
              });
            }
            if (message.command === 'deleteItem') {
              remember();
              if (p.container === 'recycle') recycleItem.empty = true;
              else item.empty = true;
            }
            if (message.command === 'undo' && history.length) {
              redo.push(capture());
              restore(history.pop()!);
            }
            if (message.command === 'redo' && redo.length) {
              history.push(capture());
              restore(redo.pop()!);
            }
            if (message.command === 'save') savedRevision = revision;
            if (message.command === 'closeSave') opened = false;
            if (message.command === 'inventory') {
              const items = Array.from({ length: 40 }, (_, index) =>
                index === 0
                  ? { ...(p.container === 'recycle' ? recycleItem : item) }
                  : {
                      index,
                      itemId: 65534,
                      name: '',
                      empty: true,
                      available:
                        p.container === 'recycle' || index < players[Number(p.player)]!.pocketCapacity,
                      quantity: 0,
                      maxQuantity: 1,
                      image: null,
                    },
              );
              data = { page: 0, pages: 1, total: 40, used: 1, matched: 40, items };
            } else if (message.command === 'catalog') {
              const query = String(p.query ?? '');
              const searched = catalog.filter(
                (item) => item.name.includes(query) || String(item.itemId).includes(query),
              );
              const category = String(p.category ?? 'all');
              const inCategory = searched.filter((item) => category === 'all' || item.category === category);
              const filtered = inCategory.filter(
                (item) =>
                  category !== 'clothing' ||
                  !p.subcategory ||
                  p.subcategory === 'all' ||
                  item.subcategory === p.subcategory,
              );
              const pages = Math.max(1, Math.ceil(filtered.length / 96));
              const page = Math.max(0, Math.min(Number(p.page ?? 0), pages - 1));
              data = {
                page,
                pages,
                total: filtered.length,
                totalAll: catalog.length,
                categories: ['all', 'materials', 'clothing', 'other'].map((id) => ({
                  id,
                  count:
                    id === 'all' ? searched.length : searched.filter((item) => item.category === id).length,
                })),
                subcategories:
                  category === 'clothing'
                    ? ['all', 'tops', 'shoes'].map((id) => ({
                        id,
                        count:
                          id === 'all'
                            ? inCategory.length
                            : inCategory.filter((item) => item.subcategory === id).length,
                      }))
                    : [],
                items: filtered.slice(page * 96, (page + 1) * 96),
              };
            } else if (message.command === 'villagerCatalog') {
              const all = Array.from({ length: 140 }, (_, i) => ({
                id: 'v' + String(i).padStart(4, '0'),
                name: '岛民' + String(i + 1).padStart(3, '0'),
                species: [4, 31, 34][i % 3],
                personality: i % 8,
                image: avatar,
              }));
              const matched = all.filter(
                (v) => v.name.includes(String(p.query ?? '')) || v.id.includes(String(p.query ?? '')),
              );
              const filtered = matched.filter(
                (v) =>
                  (Number(p.species) < 0 || v.species === Number(p.species)) &&
                  (Number(p.personality) < 0 || v.personality === Number(p.personality)),
              );
              const pages = Math.max(1, Math.ceil(filtered.length / 60));
              const pg = Math.min(Number(p.page ?? 0), pages - 1);
              data = {
                page: pg,
                pages,
                total: filtered.length,
                species: [4, 31, 34].map((id) => ({
                  id,
                  total: matched.filter(
                    (v) =>
                      v.species === id &&
                      (Number(p.personality) < 0 || v.personality === Number(p.personality)),
                  ).length,
                })),
                personalities: Array.from({ length: 9 }, (_, id) => ({
                  id,
                  total: matched.filter(
                    (v) => v.personality === id && (Number(p.species) < 0 || v.species === Number(p.species)),
                  ).length,
                })),
                items: filtered.slice(pg * 60, (pg + 1) * 60),
              };
            } else data = snapshot();
            const response = structuredClone({ id: message.id, ok: true, data });
            queueMicrotask(() => listeners.forEach((fn) => fn({ data: response })));
          },
        },
      },
    });
  }, picker);
}
