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
    type AdvancedTarget = { kind: string; player: number; resident: number; index: number; container: string };
    let advancedRecords: Record<string, Record<string, string>> = {};
    const advancedKey = (target: AdvancedTarget) => `${target.kind}:${target.player}:${target.resident}:${target.container}:${target.index}`;
    const advanced = (target: AdvancedTarget) => {
      const kind = target.kind;
      const count = kind === 'mailbox' ? 300 : kind === 'memory' ? 8 : kind === 'wear' ? 24 : kind === 'furniture' ? 32 : kind.startsWith('flags') ? 1024 : kind === 'item' ? 40 : 1;
      const number = (key: string, value = '0', min = '0', max = '255') => ({ key, type: 'number', value, min, max, length: 0 });
      const text = (key: string, value = '', length = 10) => ({ key, type: 'text', value, min: '0', max: '0', length });
      const itemFields = [number('RawValue', '1000', '0', '18446744073709551615'), number('ItemId', '1000', '0', '65535'), number('SystemParam'), number('AdditionalParam'), number('FreeParam', '0', '-2147483648', '2147483647'), number('Count', '0', '0', '65535'), number('Rotation', '0', '0', '3'), number('U08', '0', '0', '4294967295')];
      const fields = kind.startsWith('flags') ? [number('Value', '0', kind === 'flagsResident' ? '0' : '-32768', kind === 'flagsResident' ? '65535' : '32767')]
        : kind === 'resident' ? [number('Species', '4'), number('BirthType'), number('DIYRecipeIndex', '0', '0', '65535'), text('CatchPhrase', '你好', 24)]
        : kind === 'memory' ? [number('Friendship'), text('NickName'), text('Greeting.10', '', 22), number('Flag.255')]
        : kind === 'room' ? [number('WallItem.UniqueID', '0', '0', '65535'), number('FloorDirection')]
        : kind === 'mailbox' ? [number('ReadStatus'), number('SortIndex', '0', '0', '65535'), text('To', '', 47), text('Message', '', 239), text('Language', '', 5), ...itemFields]
        : itemFields;
      const stored = advancedRecords[advancedKey(target)] ?? {};
      return { target, entries: Array.from({ length: count }, (_, index) => ({ index, name: `Entry ${index}`, available: kind !== 'item' || index < 30 })),
        fields: fields.map(field => ({ ...field, value: stored[field.key] ?? field.value })), extension: kind === 'mailbox' ? 'nhpb' : kind.startsWith('flags') ? 'nhfl' : 'nhi', size: kind === 'mailbox' ? 968 : 8 };
    };
    let consoleState = { connected: false, transport: 'network', host: '127.0.0.1', port: 6000, offset: '00001000', data: '' };
    type HousingValue = number | boolean | string;
    let museumRecords = { level: 2, entries: Array.from({ length: 1024 }, (_, index) => ({ index,
      name: index === 0 ? '木材' : '', date: index === 0 ? '2024-02-29' : '', player: index === 0 ? 1 : 0,
      item: { itemId: index === 0 ? 1000 : 65534, systemParam: 0, additionalParam: 0, freeParam: 0 } })) };
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
        ['patterns', 'proDesigns', 'flag', 'tailor', 'residentDesigns'].map((kind) => [
        kind,
        Array.from({ length: kind === 'flag' ? 1 : kind === 'tailor' ? 8 : kind === 'residentDesigns' ? 10 : 100 }, (_, index) => ({
          name: `${kind} ${index + 1}`,
          player: 'Author',
          town: 'Origin',
          compatibility: kind === 'patterns' || kind === 'flag' ? 0xee00 : 0xee01,
          sheets: Array.from({ length: ['proDesigns', 'tailor', 'residentDesigns'].includes(kind) ? 4 : 1 }, (_, i) =>
            designBitmap(i),
          ),
          palette: Array.from(
            { length: 15 },
            (_, i) => '#' + [17 + i, 34 + i, 51 + i].map((v) => v.toString(16).padStart(2, '0')).join(''),
          ),
        })),
      ]),
    );
    type GridPoint = { x: number; y: number };
    type GridTarget = { kind: string; map?: Record<string, unknown>; house?: number; room?: number; layer?: number; player?: number; container?: string; slots?: number[]; columns?: number };
    let inventoryRecords: Record<string, Record<string, number>> = {};
    const inventoryKey = (target: GridTarget, point: GridPoint) => `${target.container}:${target.player}:${target.slots![point.y * target.columns! + point.x]}`;
    let gridClipboard: { kind: string; count: number; width: number; height: number; entries: { x: number; y: number; values: Record<string, number> }[] } | null = null;
    const gridRead = (target: GridTarget, point: GridPoint): Record<string, number> => {
      if (target.kind === 'inventory') return { ...(inventoryRecords[inventoryKey(target, point)] ?? { itemId: target.slots![point.y * target.columns! + point.x] === 0 ? item.itemId : 65534, systemParam: 0, additionalParam: 0, freeParam: 0 }) };
      if (target.kind === 'roomItems') return { ...(housingRecords.tiles[tileKey({ index: target.house, room: target.room, layer: target.layer }, point.x, point.y)] ?? { itemId: 65534, systemParam: 0, additionalParam: 0, freeParam: 0 }) };
      const values = mapRecords[mapKey({ ...target.map, x: point.x, y: point.y })] ?? {};
      if (target.kind === 'terrain') return { ...values };
      return { itemId: values['Map.ItemId'] ?? 65534, systemParam: values['Map.SystemParam'] ?? 0, additionalParam: values['Map.AdditionalParam'] ?? 0, freeParam: values['Map.FreeParam'] ?? 0 };
    };
    const gridWrite = (target: GridTarget, point: GridPoint, values: Record<string, number>) => {
      if (target.kind === 'inventory') { inventoryRecords[inventoryKey(target, point)] = { ...values }; return; }
      if (target.kind === 'roomItems') housingRecords.tiles[tileKey({ index: target.house, room: target.room, layer: target.layer }, point.x, point.y)] = { itemId: values.itemId!, systemParam: values.systemParam!, additionalParam: values.additionalParam!, freeParam: values.freeParam! };
      else mapRecords[mapKey({ ...target.map, x: point.x, y: point.y })] = target.kind === 'terrain' ? { ...values } : { 'Map.ItemId': values.itemId!, 'Map.SystemParam': values.systemParam!, 'Map.AdditionalParam': values.additionalParam!, 'Map.FreeParam': values.freeParam! };
    };
    let mapFlags: Record<string, boolean> = {};
    let mapRecords: Record<string, Record<string, number>> = {};
    const mapKey = (target: Record<string, unknown>) => JSON.stringify(target);
    const mapData = (target: Record<string, unknown>) => {
      const mode = String(target.mode), gridSize = mode === 'items' ? 32 : 16;
      const names = mode === 'terrain' ? ['UnitModel', 'Variation', 'LandMakingAngle', 'UnitModelRoad', 'VariationRoad', 'LandMakingAngleRoad', 'Elevation']
        : mode === 'items' ? ['ItemId', 'SystemParam', 'AdditionalParam', 'FreeParam']
        : mode === 'buildings' ? ['BuildingType', 'X', 'Y', 'Angle', 'Bit', 'Type', 'TypeArg', 'UniqueID', 'Unused']
        : mode === 'acres' ? ['AcreTemplate'] : ['PlazaX', 'PlazaY', 'OutsideTemplate', 'MainTemplate', 'DesignValue'];
      const values = mapRecords[mapKey(target)] ?? {};
      return { target, gridSize, itemWidth: 288, itemHeight: 192, markerCount: 13824,
        acres: Array.from({ length: 72 }, (_, id) => ({ id, name: `Acre ${id}` })),
        buildings: Array.from({ length: 46 }, (_, id) => ({ id, name: `Building ${id}` })),
        fieldObjects: [{ id: 60000, name: 'Tree' }],
        fields: names.map(name => ({ key: 'Map.' + name, value: values['Map.' + name] ?? 0, type: 'number', min: name === 'Bit' ? -128 : 0, max: 65535, options: name === 'UnitModel' ? [{ id: 0, name: 'Base' }, { id: 21, name: 'River8A' }, { id: 17, name: 'River5B' }, { id: 11, name: 'River3B' }, { id: 12, name: 'River3C' }, { id: 20, name: 'River7A' }, { id: 42, name: 'Cliff5B' }, { id: 33, name: 'Cliff3B' }, { id: 34, name: 'Cliff3C' }, { id: 45, name: 'Cliff7A' }] : null })),
        parameters: ['ItemId', 'SystemParam', 'AdditionalParam', 'FreeParam', 'Rotation', 'RawValue', 'IsDropped', 'ExtensionX', 'IsRoot'].map(key => ({ key, type: ['IsDropped', 'IsRoot'].includes(key) ? 'boolean' : 'number', min: key === 'FreeParam' ? '-2147483648' : '0', max: key === 'RawValue' ? '18446744073709551615' : '65535', writable: !['ExtensionX', 'IsRoot'].includes(key) })),
        cells: Array.from({ length: gridSize ** 2 }, (_, i) => ({ x: i % gridSize, y: Math.floor(i / gridSize), relativeX: i % gridSize, relativeY: Math.floor(i / gridSize), valid: true, extension: false, rootX: 0, rootY: 0, name: '' })),
        overview: avatar, viewport: avatar, itemImage: null, active: mode === 'items' ? (mapFlags[mapKey(target)] ?? false) : null };
    };
    type Snapshot = {
      advancedRecords: typeof advancedRecords;
      museumRecords: typeof museumRecords;
      inventoryRecords: typeof inventoryRecords;
      mapFlags: typeof mapFlags;
      mapRecords: typeof mapRecords;
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
        advancedRecords,
        museumRecords,
        inventoryRecords,
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
        mapRecords,
        mapFlags,
      });
    const restore = (value: Snapshot) => {
      advancedRecords = value.advancedRecords;
      museumRecords = value.museumRecords;
      inventoryRecords = value.inventoryRecords;
      turnips = value.turnips;
      recycleItem = value.recycleItem;
      progressRecords = value.progressRecords;
      housingRecords = value.housingRecords;
      designRecords = value.designRecords;
      mapRecords = value.mapRecords;
      mapFlags = value.mapFlags;
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
            clipboard: gridClipboard ? { kind: gridClipboard.kind, count: gridClipboard.count, width: gridClipboard.width, height: gridClipboard.height } : null,
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
            if (['setAdvanced', 'advancedTextBatch', 'advancedImport', 'residentBatch'].includes(message.command)) {
              const controls = window as unknown as { nhseAdvancedCancelNext?: boolean; nhseAdvancedErrorNext?: string };
              if (controls.nhseAdvancedErrorNext) {
                const error = controls.nhseAdvancedErrorNext; controls.nhseAdvancedErrorNext = undefined;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } })));
                return;
              }
              if (controls.nhseAdvancedCancelNext) controls.nhseAdvancedCancelNext = false;
              else {
                remember();
                if (p.target) {
                  const key = advancedKey(p.target as AdvancedTarget);
                  advancedRecords[key] = { ...(advancedRecords[key] ?? {}), ...(message.command === 'setAdvanced' ? p.values as Record<string, string> : { Count: '7' }) };
                }
              }
            }
            if (message.command.startsWith('console')) {
              const controls = window as unknown as { nhseConsoleErrorNext?: boolean };
              if (controls.nhseConsoleErrorNext) {
                controls.nhseConsoleErrorNext = false; consoleState.connected = false;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error: 'consoleConnectionError' } })));
                return;
              }
              if (message.command === 'consoleConnect') Object.assign(consoleState, { transport: p.transport, connected: true });
              if (message.command === 'consoleDisconnect') consoleState.connected = false;
              if (message.command === 'consoleRead') consoleState.data = '01020304';
              if (message.command === 'consoleWrite') consoleState.data = String(p.data);
              if (message.command === 'consolePocketRead') { remember(); item.quantity = 21; }
            }
            if (message.command === 'setMuseumLevel') { remember(); museumRecords.level = Number(p.level); }
            if (message.command === 'setMuseumEntry') {
              remember();
              const entry = museumRecords.entries[Number(p.index)]!;
              entry.item = { ...(p.item as typeof entry.item) }; entry.date = String(p.date); entry.player = Number(p.donor);
              entry.name = entry.item.itemId === 65534 ? '' : '木材';
            }
            if (['museumBatch', 'museumImport'].includes(message.command)) {
              const controls = window as unknown as { nhseMuseumCancelNext?: boolean; nhseMuseumErrorNext?: string };
              if (controls.nhseMuseumErrorNext) {
                const error = controls.nhseMuseumErrorNext; controls.nhseMuseumErrorNext = undefined;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } })));
                return;
              }
              if (controls.nhseMuseumCancelNext) controls.nhseMuseumCancelNext = false;
              else {
                remember();
                if (message.command === 'museumImport') museumRecords.level = 9;
                else if (p.action === 'sort') {
                  museumRecords.entries.sort((a, b) => Number(!a.date) - Number(!b.date) || a.date.localeCompare(b.date));
                  museumRecords.entries.forEach((entry, index) => { entry.index = index; });
                } else for (const entry of museumRecords.entries.slice(1, 5)) Object.assign(entry, { name: '木材', date: '2024-03-01', player: 0, item: { itemId: 1000, systemParam: 0, additionalParam: 0, freeParam: 0 } });
              }
            }
            if (['copyGrid', 'pasteGrid', 'editGrid'].includes(message.command)) {
              const target = p.target as GridTarget, selection = p.selection as GridPoint[];
              const fail = (error: string) => { queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } }))); };
              if (message.command === 'copyGrid') {
                const minX = Math.min(...selection.map(p => p.x)), minY = Math.min(...selection.map(p => p.y));
                const entries = selection.map(point => ({ x: point.x - minX, y: point.y - minY, values: gridRead(target, point) })).filter(e => target.kind === 'terrain' || e.values.itemId !== 65534);
                if (!entries.length) { fail('gridEmptyCopy'); return; }
                gridClipboard = { kind: target.kind === 'terrain' ? 'terrain' : 'items', count: entries.length, width: Math.max(...selection.map(p => p.x)) - minX + 1, height: Math.max(...selection.map(p => p.y)) - minY + 1, entries };
              } else if (message.command === 'pasteGrid') {
                if (!gridClipboard) { fail('gridNoClipboard'); return; }
                if (gridClipboard.kind !== (target.kind === 'terrain' ? 'terrain' : 'items')) { fail('gridTypeMismatch'); return; }
                const controls = window as unknown as { nhseGridErrorNext?: string };
                if (controls.nhseGridErrorNext) { fail(controls.nhseGridErrorNext); controls.nhseGridErrorNext = undefined; return; }
                remember(); const anchor = p.anchor as GridPoint;
                const entries = gridClipboard.entries.length === 1 && selection.length > 1 ? selection.map(point => ({ ...point, values: gridClipboard!.entries[0]!.values })) : gridClipboard.entries.map(e => ({ x: e.x + anchor.x, y: e.y + anchor.y, values: e.values }));
                for (const entry of entries) gridWrite(target, entry, entry.values);
              } else {
                remember();
                for (const point of selection) {
                  const values = gridRead(target, point);
                  if (target.kind === 'terrain') {
                    if (p.operation === 'delete') for (const key of ['UnitModel', 'Variation', 'LandMakingAngle', 'UnitModelRoad', 'VariationRoad', 'LandMakingAngleRoad', 'Elevation']) values['Map.' + key] = 0;
                    else values['Map.LandMakingAngle'] = ((values['Map.LandMakingAngle'] ?? 0) + 1) % 4;
                  } else if (p.operation === 'delete') Object.assign(values, { itemId: 65534, systemParam: 0, additionalParam: 0, freeParam: 0 });
                  else values.systemParam = ((values.systemParam ?? 0) & ~3) | (((values.systemParam ?? 0) + 1) & 3);
                  gridWrite(target, point, values);
                }
              }
            }
            let mapAdvancedCount: { requested: number; applied: number; cancelled: boolean } | null = null;
            if (['spawnMap', 'editMapParameters', 'replaceMapMatches'].includes(message.command)) {
              const controls = window as unknown as { nhseMapCancelNext?: boolean; nhseMapErrorNext?: string };
              if (controls.nhseMapErrorNext) {
                const error = controls.nhseMapErrorNext; controls.nhseMapErrorNext = undefined;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } })));
                return;
              }
              if (controls.nhseMapCancelNext) { controls.nhseMapCancelNext = false; mapAdvancedCount = { requested: 0, applied: 0, cancelled: true }; }
              else {
                remember();
                const options = p.options as { count?: number } | undefined;
                mapAdvancedCount = { requested: options?.count ?? 2, applied: options?.count ?? 2, cancelled: false };
              }
            }
            if (message.command === 'setMapActive') {
              remember(); mapFlags[mapKey(p.target as Record<string, unknown>)] = Boolean(p.active);
            }
            if (['paintMap', 'paintMapStroke', 'batchMap'].includes(message.command)) {
              remember();
              const key = mapKey(p.target as Record<string, unknown>);
              mapRecords[key] = { ...mapRecords[key], ...(p.values as Record<string, number>) };
              if (p.operation === 'zeroElevation') mapRecords[key]!['Map.Elevation'] = 0;
            }
            if (message.command === 'mapImport') {
              const controls = window as unknown as { nhseMapCancelNext?: boolean; nhseMapErrorNext?: string };
              if (controls.nhseMapErrorNext) {
                const error = controls.nhseMapErrorNext; controls.nhseMapErrorNext = undefined;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } })));
                return;
              }
              if (controls.nhseMapCancelNext) controls.nhseMapCancelNext = false;
              else { remember(); }
            }
            if (message.command === 'editMap') {
              remember();
              mapRecords[mapKey(p.target as Record<string, unknown>)] = { ...(p.values as Record<string, number>) };
            }
            if (message.command === 'language') locale = String(p.language);
            if (message.command === 'open' || message.command === 'openRecent') { opened = true; gridClipboard = null; }
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
            if (message.command === 'housingImport') {
              const controls = window as unknown as { nhseHousingCancelNext?: boolean; nhseHousingErrorNext?: string };
              if (controls.nhseHousingErrorNext) {
                const error = controls.nhseHousingErrorNext; controls.nhseHousingErrorNext = undefined;
                queueMicrotask(() => listeners.forEach(fn => fn({ data: { id: message.id, ok: false, error } })));
                return;
              }
              if (controls.nhseHousingCancelNext) controls.nhseHousingCancelNext = false;
              else {
                remember();
                if (!p.roomOnly) housingRecords.houses[String(p.kind)]![Number(p.index)]!.HouseLevel = 9;
                else for (let layer = 0; layer < 8; layer++) housingRecords.tiles[tileKey({ ...p, layer }, 0, 0)] = { itemId: 1000, systemParam: 128, additionalParam: 9, freeParam: -1 };
              }
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
            if (message.command === 'map') {
              data = mapData(p.target as Record<string, unknown>);
            } else if (message.command === 'designs') {
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
            } else if (message.command === 'advancedData') {
              data = advanced(p.target as AdvancedTarget);
            } else if (message.command === 'museum') {
              const controls = window as unknown as { nhseMuseumRawDateNext?: boolean };
              if (controls.nhseMuseumRawDateNext) { museumRecords.entries[7]!.date = '65535-255-255'; controls.nhseMuseumRawDateNext = false; }
              data = museumRecords;
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
              for (const entry of items) {
                const value = inventoryRecords[`${p.container}:${p.player}:${entry.index}`];
                if (value) Object.assign(entry, { itemId: value.itemId, empty: value.itemId === 65534, name: value.itemId === 65534 ? '' : '木材', quantity: value.itemId === 65534 ? 0 : 1 });
              }
              data = { page: 0, pages: 1, total: 40, used: items.filter(i => !i.empty).length, matched: 40, items };
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
            if (mapAdvancedCount) data = { state: snapshot(), ...mapAdvancedCount };
            if (message.command.startsWith('console')) data = { ...consoleState, state: snapshot() };
            const response = structuredClone({ id: message.id, ok: true, data });
            const delayControls = window as unknown as { nhseDelayMapNext?: number };
            if (message.command === 'map' && delayControls.nhseDelayMapNext) {
              const delay = delayControls.nhseDelayMapNext; delayControls.nhseDelayMapNext = undefined;
              setTimeout(() => listeners.forEach(fn => fn({ data: response })), delay);
            } else queueMicrotask(() => listeners.forEach((fn) => fn({ data: response })));
          },
        },
      },
    });
  }, picker);
}
