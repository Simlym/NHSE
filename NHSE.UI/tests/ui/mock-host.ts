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
    type ProgressEntry = {
      index: number;
      name: string;
      known?: boolean;
      made?: boolean;
      fresh?: boolean;
      favorite?: boolean;
      value?: number;
      wheel?: boolean;
      slot?: number;
      count?: number;
      flag?: number;
      dates?: string[];
      read?: boolean[];
      milestones?: number;
      thresholds?: number[];
    };
    let progressRecords: Record<string, ProgressEntry[]>[] = [0, 1].map(() => ({
      recipes: Array.from({ length: 2048 }, (_, index) => ({
        index,
        name: `Recipe ${index}`,
        known: false,
        made: false,
        fresh: false,
        favorite: false,
      })),
      reactions: Array.from({ length: 264 }, (_, index) => ({
        index,
        name: 'None',
        value: 0,
        fresh: false,
        wheel: index >= 256,
        slot: index >= 256 ? index - 256 : index,
      })),
      achievements: Array.from({ length: 512 }, (_, index) => ({
        index,
        name: `Achievement ${index}`,
        count: 0,
        flag: 0,
        dates: Array(6).fill(''),
        read: Array(6).fill(false),
        milestones: 5,
        thresholds: [10, 100, 500, 2000, 5000, 0],
      })),
    }));
    type HousingValue = number | boolean | string;
    type HousingValues = Record<string, HousingValue>;
    let housingRecords = {
      houses: Object.fromEntries(
        ['playerHouses', 'residentHouses'].map((kind) => [
          kind,
          Array.from(
            { length: kind === 'playerHouses' ? 8 : 10 },
            () => ({ HouseLevel: 1, NPC1: 0, Loan: 100, 'EventFlag.127': 0 }) as HousingValues,
          ),
        ]),
      ),
      campsite: { Occupied: false, VisitorCode: 0, VisitDate: '2020-03-20' } as HousingValues,
      fruitFlowers: {
        SpecialtyFruit: 2213,
        SisterFruit: 2285,
        SpecialtyFlower: 0,
        SisterFlower: 1,
      } as HousingValues,
      profileFruits: [2213, 2287],
      tiles: {} as Record<
        string,
        { itemId: number; systemParam: number; additionalParam: number; freeParam: number }
      >,
    };
    const tileKey = (p: Record<string, unknown>, x: number, y: number) =>
      `${p.index}:${p.room}:${p.layer}:${x}:${y}`;
    const housing = (p: Record<string, unknown>) => {
      const kind = String(p.kind);
      const index = p.resident === undefined ? Number(p.index) : 5;
      p = { ...p, index };
      let values: HousingValues;
      if (kind === 'campsite') values = housingRecords.campsite;
      else if (kind === 'fruitFlowers')
        values = {
          ...housingRecords.fruitFlowers,
          ProfileFruit: housingRecords.profileFruits[Number(p.player)]!,
        };
      else values = housingRecords.houses[kind]![index]!;
      const fruits = [2213, 2214, 2285, 2286, 2287].map((id) => ({ id, name: String(id) }));
      const field = ([key, value]: [string, HousingValue]) => ({
        key,
        value,
        type: typeof value === 'boolean' ? 'boolean' : typeof value === 'string' ? 'date' : 'number',
        min: key === 'NPC1' ? -128 : key.startsWith('EventFlag') ? -32768 : 0,
        max: key === 'NPC1' ? 127 : key.startsWith('EventFlag') ? 32767 : 4294967295,
        options: key.includes('Fruit')
          ? fruits
          : key.includes('Flower')
            ? Array.from({ length: 8 }, (_, id) => ({ id, name: String(id) }))
            : key === 'VisitorCode'
              ? [
                  { id: 0, name: 'ant00' },
                  { id: 4, name: 'ant04' },
                ]
              : null,
      });
      return {
        kind,
        index,
        locked: false,
        entries: kind.includes('Houses')
          ? Array.from({ length: kind === 'playerHouses' ? 8 : 10 }, (_, index) => ({
              index,
              name: `House ${index + 1}`,
            }))
          : [],
        fields: Object.entries(values)
          .filter(([key]) => !key.startsWith('EventFlag'))
          .map(field),
        flags: Object.entries(values)
          .filter(([key]) => key.startsWith('EventFlag'))
          .map(field),
        room:
          kind === 'playerHouses'
            ? {
                room: Number(p.room),
                layer: Number(p.layer),
                tiles: Array.from({ length: 400 }, (_, i) => {
                  const x = i % 20,
                    y = Math.floor(i / 20);
                  const item = housingRecords.tiles[tileKey(p, x, y)] ?? {
                    itemId: 65534,
                    systemParam: 0,
                    additionalParam: 0,
                    freeParam: 0,
                  };
                  return {
                    x,
                    y,
                    rootX: x,
                    rootY: y,
                    item: { ...item },
                    empty: item.itemId === 65534,
                    extension: false,
                    name: item.itemId === 65534 ? '' : `Item ${item.itemId}`,
                    image: null,
                  };
                }),
              }
            : null,
      };
    };
    function designBitmap(sheet: number) {
      const bytes = new Uint8Array(4096);
      for (let i = 0; i < bytes.length; i += 4) {
        bytes[i] = 51 + sheet;
        bytes[i + 1] = 34 + sheet;
        bytes[i + 2] = 17 + sheet;
        bytes[i + 3] = 255;
      }
      if (sheet === 3) bytes[4095] = 0;
      return btoa(String.fromCharCode(...bytes));
    }
    let designRecords = Object.fromEntries(
      ['patterns', 'proDesigns', 'flag', 'tailor'].map((kind) => [
        kind,
        Array.from({ length: kind === 'flag' ? 1 : kind === 'tailor' ? 8 : 100 }, (_, index) => ({
          name: `${kind} ${index + 1}`,
          player: 'Author',
          town: 'Origin',
          compatibility: kind === 'patterns' || kind === 'flag' ? 0xee00 : 0xee01,
          sheets: Array.from({ length: kind === 'proDesigns' || kind === 'tailor' ? 4 : 1 }, (_, i) =>
            designBitmap(i),
          ),
          palette: Array.from(
            { length: 15 },
            (_, i) => '#' + [17 + i, 34 + i, 51 + i].map((v) => v.toString(16).padStart(2, '0')).join(''),
          ),
        })),
      ]),
    );
    type Snapshot = {
      designRecords: typeof designRecords;
      housingRecords: typeof housingRecords;
      progressRecords: typeof progressRecords;
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
      structuredClone({
        players,
        item,
        revision,
        residents,
        islandSettings,
        turnips,
        recycleItem,
        progressRecords,
        housingRecords,
        designRecords,
      });
    const restore = (value: Snapshot) => {
      turnips = value.turnips;
      recycleItem = value.recycleItem;
      progressRecords = value.progressRecords;
      housingRecords = value.housingRecords;
      designRecords = value.designRecords;
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
            if (['setRecipe', 'setReaction', 'setAchievement'].includes(message.command)) {
              remember();
              const section = (
                { setRecipe: 'recipes', setReaction: 'reactions', setAchievement: 'achievements' } as Record<
                  string,
                  string
                >
              )[message.command]!;
              const entry = progressRecords[Number(p.player)]![section]![Number(p.index)]!;
              for (const key of [
                'known',
                'made',
                'fresh',
                'favorite',
                'value',
                'count',
                'flag',
                'dates',
                'read',
              ]) {
                if (key in entry && key in p) Object.assign(entry, { [key]: structuredClone(p[key]) });
              }
              if (section === 'reactions') entry.name = String(p.value);
            }
            if (message.command === 'progressBatch') {
              remember();
              const section = String(p.section);
              const entries = progressRecords[Number(p.player)]![section]!;
              for (const entry of entries) {
                if (Number(p.index) !== -1 && entry.index !== Number(p.index)) continue;
                if (section === 'recipes') {
                  if (p.action === 'learnAll') {
                    entry.known = true;
                    entry.fresh = true;
                  }
                  if (p.action === 'clearAll') {
                    entry.known = false;
                    entry.made = false;
                    entry.fresh = false;
                    entry.favorite = false;
                  }
                  if (p.action === 'craftAll' && entry.known) {
                    entry.made = true;
                    entry.fresh = false;
                  }
                } else if (section === 'reactions') {
                  entry.value = p.action === 'clearAll' ? 0 : entry.wheel ? entry.value : 3;
                  entry.name = String(entry.value);
                  if (p.action === 'clearAll') entry.fresh = false;
                } else {
                  entry.count = p.action === 'clear' ? 0 : 5000;
                  entry.flag = p.action === 'clear' ? 0 : 1;
                  entry.dates = Array(6).fill(p.action === 'clear' ? '' : '2026-10-24');
                  entry.read = Array(6).fill(p.action !== 'clear');
                }
              }
            }
            if (message.command === 'setHousing') {
              remember();
              const kind = String(p.kind);
              const values = p.values as HousingValues;
              if (kind === 'fruitFlowers') {
                housingRecords.profileFruits[Number(p.player)] = Number(
                  values.ProfileFruit ?? housingRecords.profileFruits[Number(p.player)],
                );
                Object.assign(
                  housingRecords.fruitFlowers,
                  Object.fromEntries(Object.entries(values).filter(([key]) => key !== 'ProfileFruit')),
                );
              } else
                Object.assign(
                  kind === 'campsite'
                    ? housingRecords.campsite
                    : housingRecords.houses[kind]![Number(p.index)]!,
                  values,
                );
            }
            if (message.command === 'setRoomItem') {
              remember();
              housingRecords.tiles[tileKey(p, Number(p.x), Number(p.y))] = {
                itemId: p.delete ? 65534 : Number(p.itemId),
                systemParam: p.delete ? 0 : Number(p.systemParam),
                additionalParam: p.delete ? 0 : Number(p.additionalParam),
                freeParam: p.delete ? 0 : Number(p.freeParam),
              };
            }
            if (message.command === 'designImport') {
              const controls = window as unknown as {
                nhseDesignCancelNext?: boolean;
                nhseDesignErrorNext?: string;
              };
              if (controls.nhseDesignErrorNext) {
                const error = controls.nhseDesignErrorNext;
                controls.nhseDesignErrorNext = undefined;
                queueMicrotask(() =>
                  listeners.forEach((fn) => fn({ data: { id: message.id, ok: false, error } })),
                );
                return;
              }
              if (controls.nhseDesignCancelNext) controls.nhseDesignCancelNext = false;
              else {
                remember();
                const entries = designRecords[String(p.kind)]!;
                const start = p.all ? 0 : Number(p.index);
                const count = p.all ? Math.min(2, entries.length) : 1;
                for (let i = 0; i < count; i++) {
                  const entry = entries[start + i]!;
                  entry.name = `Imported ${i + 1}`;
                  if (p.useCurrentPlayer) {
                    entry.player = players[Number(p.player)]!.name;
                    entry.town = players[Number(p.player)]!.island;
                  }
                }
              }
            }
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
            if (message.command === 'designs') {
              const entries = designRecords[String(p.kind)]!;
              data = {
                kind: p.kind,
                index: p.index,
                entries: entries.map((entry, index) => ({
                  index,
                  name: entry.name,
                  preview: entry.sheets[0],
                })),
                selected: entries[Number(p.index)],
              };
            } else if (message.command === 'housing') {
              data = housing(p);
            } else if (message.command === 'progress') {
              data = {
                section: p.section,
                entries: progressRecords[Number(p.player)]![String(p.section)],
                options: Array.from({ length: 256 }, (_, id) => ({ id, name: String(id) })),
              };
            } else if (message.command === 'inventory') {
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
