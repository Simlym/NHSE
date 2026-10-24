export interface Player {
  index: number;
  name: string;
  island: string;
  avatar: string | null;
  wallet: number;
  bank: number;
  miles: number;
  earnedMiles: number;
  pocketCapacity: number;
  storageCapacity: number;
  maxPocketCapacity: number;
  maxStorageCapacity: number;
  hotelTickets: number | null;
  poki: number | null;
}
export interface Session {
  title: string;
  island: string;
  dirty: boolean;
  canUndo: boolean;
  canRedo: boolean;
  players: Player[];
  residents: Resident[];
  islandSettings: IslandSettings;
  turnips: TurnipSettings;
}
export interface Resident extends VillagerEntry {
  index: number;
  catchphrase: string;
  defaultCatchphrase: string;
  movingOut: boolean;
  empty: boolean;
  canReplace: boolean;
}
export interface TurnipSettings {
  buyPrice: number;
  sellPrices: number[];
  pattern: number;
  feverStart: number;
}
export interface IslandSettings {
  airportColor: number;
  hemisphere: number;
  tourHemisphere: number;
  weatherSeed: number;
  tourWeatherSeed: number;
}
export interface State {
  language: string;
  recent: string;
  session: Session | null;
  fullScreen: boolean;
  villagerPicker?: VillagerPickerContext | null;
}
export interface VillagerEntry {
  id: string;
  name: string;
  species: number;
  personality: number;
  image: string | null;
}
export interface VillagerPickerContext {
  index: number;
  residents: { index: number; id: string; name: string; image: string | null; empty: boolean }[];
}
export interface VillagerCatalog {
  page: number;
  pages: number;
  total: number;
  species: { id: number; total: number }[];
  personalities: { id: number; total: number }[];
  items: VillagerEntry[];
}
export interface ItemView {
  index: number;
  itemId: number;
  name: string;
  empty: boolean;
  available: boolean;
  quantity: number;
  maxQuantity: number;
  image: string | null;
}
export interface Inventory {
  page: number;
  pages: number;
  total: number;
  matched: number;
  used: number;
  items: ItemView[];
}
export interface Catalog {
  page: number;
  pages: number;
  total: number;
  totalAll: number;
  categories: { id: string; count: number }[];
  subcategories: { id: string; count: number }[];
  items: ItemView[];
}
interface Response {
  id: string;
  ok: boolean;
  data?: unknown;
  error?: string;
  detail?: string;
  eventName?: string;
}
interface WebView {
  postMessage(message: unknown): void;
  postMessageWithAdditionalObjects(message: unknown, objects: File[]): void;
  addEventListener(
    name: 'message',
    listener: (event: MessageEvent<Response & { data: State }>) => void,
  ): void;
}
declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

export const native = !!window.chrome?.webview;
let sequence = 0;
const pending = new Map<string, { resolve(value: unknown): void; reject(reason: Error): void }>();
export let onState: (state: State) => void = () => {};
export function listenState(listener: (state: State) => void) {
  onState = listener;
}
window.chrome?.webview?.addEventListener('message', (event) => {
  const response = event.data;
  if (response.eventName === 'state') {
    onState(response.data);
    return;
  }
  const task = pending.get(response.id);
  if (!task) return;
  pending.delete(response.id);
  if (response.ok) task.resolve(response.data);
  else task.reject(new Error(response.error ?? 'operationFailed', { cause: response.detail }));
});

export function request<T>(
  command: string,
  payload: Record<string, unknown> = {},
  files?: File[],
): Promise<T> {
  if (!native) {
    if (command === 'bootstrap' || command === 'language')
      return Promise.resolve({
        language: payload.language ?? localStorage.getItem('nhse.language') ?? 'en',
        recent: '',
        session: null,
        fullScreen: false,
      } as T);
    return Promise.reject(new Error('offlinePreview'));
  }
  const id = String(++sequence);
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: (value) => resolve(value as T), reject });
    try {
      const message = { id, command, payload };
      if (files) window.chrome!.webview!.postMessageWithAdditionalObjects(message, files);
      else window.chrome!.webview!.postMessage(message);
    } catch (error) {
      pending.delete(id);
      reject(error);
    }
  });
}

export type ProgressSection = 'recipes' | 'reactions' | 'achievements';
export interface ProgressEntry {
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
}
export interface ProgressData {
  section: ProgressSection;
  entries: ProgressEntry[];
  options: { id: number; name: string }[];
}

export type HousingKind = 'playerHouses' | 'residentHouses' | 'campsite' | 'fruitFlowers';
export interface HousingField {
  key: string;
  value: number | boolean | string;
  type: 'number' | 'boolean' | 'date';
  min: number;
  max: number;
  options: { id: number; name: string }[] | null;
}
export interface RoomItem {
  itemId: number;
  systemParam: number;
  additionalParam: number;
  freeParam: number;
}
export interface HousingTile {
  x: number; y: number; rootX: number; rootY: number;
  empty: boolean; extension: boolean; name: string; item: RoomItem; image: string | null;
}
export interface HousingData {
  kind: HousingKind; index: number;
  entries: { index: number; name: string }[];
  fields: HousingField[]; flags: HousingField[];
  room: { room: number; layer: number; tiles: HousingTile[] } | null;
  locked: boolean;
}
export interface HousingTarget { kind: HousingKind; index: number; room: number; layer: number; resident?: number }

export type DesignKind = 'patterns' | 'proDesigns' | 'flag' | 'tailor';
export interface DesignData {
  kind: DesignKind;
  index: number;
  entries: { index: number; name: string; preview: string }[];
  selected: { name: string; player: string; town: string; compatibility: number; sheets: string[]; palette: string[] };
}

export interface MapTarget {
  mode: 'terrain' | 'items' | 'buildings' | 'acres' | 'markers';
  acre: number; layer: number; x: number; y: number; building: number; marker: number;
}
export interface MapData {
  target: MapTarget; gridSize: number; itemWidth: number; itemHeight: number; markerCount: number;
  acres: { id: number; name: string }[]; buildings: { id: number; name: string }[];
  fieldObjects: { id: number; name: string }[]; fields: HousingField[];
  cells: { x: number; y: number; relativeX: number; relativeY: number; valid: boolean;
    extension: boolean; rootX: number; rootY: number; name: string }[];
  overview: string | null; viewport: string | null; itemImage: string | null; active: boolean | null;
}
