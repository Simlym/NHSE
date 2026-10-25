<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  ArrowRight,
  Check,
  CheckCheck,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  FolderOpen,
  Globe2,
  HelpCircle,
  LayoutDashboard,
  Leaf,
  Map,
  Maximize2,
  Minimize2,
  Package,
  Plus,
  Redo2,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  Undo2,
  Upload,
  UserRound,
  Users,
  X,
  Save,
  PanelLeftClose,
  PanelLeftOpen,
  Palette,
  SunMoon,
} from '@lucide/vue';
import { languages, language, setLanguage, t, hasText } from './i18n';
import VillagerPicker from './VillagerPicker.vue';
import ResidentsPage from './ResidentsPage.vue';
import IslandPage from './IslandPage.vue';
import ProgressPage from './ProgressPage.vue';
import HousingPage from './HousingPage.vue';
import DesignPage from './DesignPage.vue';
import { editableTarget } from './gridSelection';
import MapPage from './MapPage.vue';
import InventoryGrid from './InventoryGrid.vue';
import {
  listenState,
  native,
  request,
  type MapData,
  type MapTarget,
  type DesignData,
  type DesignKind,
  type HousingData,
  type HousingTarget,
  type HousingKind,
  type ProgressData,
  type ProgressSection,
  type Inventory,
  type Catalog,
  type ItemView,
  type Player,
  type State,
} from './bridge';

function stepText(number: number, suffix: 'Title' | 'Body') {
  return t(('step' + number + suffix) as Parameters<typeof t>[0]);
}

type Page =
  | 'overview'
  | 'player'
  | 'inventory'
  | 'villagers'
  | 'island'
  | 'tools'
  | 'progress'
  | 'housing'
  | 'designs'
  | 'mapEditor';
const state = ref<State>({ language: 'en', recent: '', session: null, fullScreen: false });
const page = ref<Page>('overview');
const playerIndex = ref(0);
const container = ref('pocket');
const inventory = ref<Inventory | null>(null);
const selected = ref<ItemView | null>(null);
const draftItem = ref<ItemView | null>(null);
const quantity = ref(1);
const query = ref('');
const catalogQuery = ref('');
const catalog = ref<Catalog | null>(null);
const catalogCategory = ref('all');
const catalogSubcategory = ref('all');
const clothingLabels = {
  all: 'clothesAll',
  tops: 'clothesTops',
  bottoms: 'clothesBottoms',
  dresses: 'clothesDresses',
  headwear: 'clothesHeadwear',
  accessories: 'clothesAccessories',
  socks: 'clothesSocks',
  shoes: 'clothesShoes',
  bags: 'clothesBags',
  umbrellas: 'clothesUmbrellas',
  other: 'clothesOther',
} as const;
const catalogJump = ref(1);
const catalogLabels = {
  all: 'categoryAll',
  furniture: 'categoryFurniture',
  clothing: 'categoryClothing',
  tools: 'categoryTools',
  nature: 'categoryNature',
  materials: 'categoryMaterials',
  creatures: 'categoryCreatures',
  interior: 'categoryInterior',
  food: 'categoryFood',
  recipes: 'categoryRecipes',
  other: 'categoryOther',
} as const;
const catalogOpen = ref(false);
const helpOpen = ref(false);
const busy = ref(false);
const error = ref('');
const errorDetail = ref('');
const notice = ref('');
const dragging = ref(false);
const focused = ref(false);
const theme = ref(localStorage.getItem('nhse.theme') ?? 'system');
const fields = ref({
  name: '',
  island: '',
  wallet: 0,
  bank: 0,
  miles: 0,
  earnedMiles: 0,
  pocketCapacity: 0,
  storageCapacity: 0,
  hotelTickets: null as number | null,
  poki: null as number | null,
});
const playerForm = ref<HTMLFormElement | null>(null);
const residentsPage = ref<InstanceType<typeof ResidentsPage> | null>(null);
const designData = ref<DesignData | null>(null);
const designTarget = ref<{ kind: DesignKind; index: number }>({ kind: 'patterns', index: 0 });
const housingPage = ref<InstanceType<typeof HousingPage> | null>(null);
const housingData = ref<HousingData | null>(null);
const housingTarget = ref<HousingTarget>({ kind: 'playerHouses', index: 0, room: 0, layer: 0 });
const mapData = ref<MapData | null>(null);
const mapPage = ref<InstanceType<typeof MapPage> | null>(null);
const mapTarget = ref<MapTarget>({ mode: 'terrain', acre: 10, layer: 0, x: 0, y: 0, building: 0, marker: 0 });
let catalogTarget: 'inventory' | 'housing' | 'map' = 'inventory';
const progressPage = ref<InstanceType<typeof ProgressPage> | null>(null);
const progressData = ref<ProgressData | null>(null);
const progressSection = ref<ProgressSection>('recipes');
const islandPage = ref<InstanceType<typeof IslandPage> | null>(null);
const currentPlayer = computed(() => state.value.session?.players.find((p) => p.index === playerIndex.value));
const currentItem = computed(() => draftItem.value ?? selected.value);
const profilePending = computed(
  () =>
    currentPlayer.value &&
    Object.entries(fields.value).some(
      ([key, value]) => value !== (currentPlayer.value![key as keyof Player] ?? null),
    ),
);
const balanceFields = ['wallet', 'bank', 'miles', 'earnedMiles'] as const;
const nav = [
  { id: 'overview', icon: LayoutDashboard },
  { id: 'player', icon: UserRound },
  { id: 'inventory', icon: Package },
  { id: 'progress', icon: Check },
  { id: 'housing', icon: Map },
  { id: 'designs', icon: Palette },
  { id: 'mapEditor', icon: Map },
  { id: 'villagers', icon: Users },
  { id: 'island', icon: Leaf },
  { id: 'tools', icon: Map },
] as const;
let inventoryToken = 0,
  catalogToken = 0;
let queryTimer: ReturnType<typeof setTimeout> | undefined;
let catalogTimer: ReturnType<typeof setTimeout> | undefined;
let noticeTimer: ReturnType<typeof setTimeout> | undefined;
let pendingCatalogPage: number | null = null;
let lastFocus: HTMLElement | null = null;
const dialog = ref<HTMLDialogElement | null>(null);

function syncFields(player?: Player) {
  if (player)
    fields.value = {
      name: player.name,
      island: player.island,
      wallet: player.wallet,
      bank: player.bank,
      miles: player.miles,
      earnedMiles: player.earnedMiles,
      pocketCapacity: player.pocketCapacity,
      storageCapacity: player.storageCapacity,
      hotelTickets: player.hotelTickets ?? null,
      poki: player.poki ?? null,
    };
}
function accept(next: State) {
  const previousPlayer = currentPlayer.value;
  state.value = next;
  setLanguage(next.language);
  localStorage.setItem('nhse.language', language.value);
  if (!next.session?.players.some((p) => p.index === playerIndex.value))
    playerIndex.value = next.session?.players[0]?.index ?? 0;
  const nextPlayer = currentPlayer.value;
  if (
    !previousPlayer ||
    previousPlayer.index !== nextPlayer?.index ||
    Object.keys(fields.value).some(
      (key) => previousPlayer[key as keyof Player] !== nextPlayer?.[key as keyof Player],
    )
  )
    syncFields(nextPlayer);
}
function toast(key: Parameters<typeof t>[0]) {
  notice.value = t(key);
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => (notice.value = ''), 5000);
}
async function run(action: () => Promise<void>) {
  if (busy.value) return false;
  busy.value = true;
  error.value = '';
  errorDetail.value = '';
  try {
    await action();
    return true;
  } catch (reason) {
    const e = reason instanceof Error ? reason : new Error('operationFailed');
    error.value = t(hasText(e.message) ? e.message : 'operationFailed');
    errorDetail.value = typeof e.cause === 'string' ? e.cause : '';
    return false;
  } finally {
    busy.value = false;
  }
}
async function loadMap() {
  if (state.value.session) mapData.value = await request<MapData>('map', { target: { ...mapTarget.value } });
}
async function navigateMap(part: Partial<MapTarget>) {
  if (busy.value || !(await applyWorldDrafts())) return;
  await run(async () => {
    const next = { ...mapTarget.value, ...part };
    const data = await request<MapData>('map', { target: next });
    mapTarget.value = data.target;
    mapData.value = data;
  });
}
async function loadDesigns() {
  if (state.value.session) designData.value = await request<DesignData>('designs', { ...designTarget.value });
}
async function navigateDesigns(kind: DesignKind, index: number) {
  if (busy.value) return;
  await run(async () => {
    const data = await request<DesignData>('designs', { kind, index });
    designData.value = data;
    designTarget.value = { kind: data.kind, index: data.index };
  });
}
async function loadHousing() {
  if (!state.value.session) return;
  const data = await request<HousingData>('housing', { player: playerIndex.value, ...housingTarget.value });
  housingData.value = data;
  housingTarget.value = {
    kind: data.kind,
    index: data.index,
    room: data.room?.room ?? 0,
    layer: data.room?.layer ?? 0,
  };
}
async function navigateHousing(part: Partial<HousingTarget>) {
  if (busy.value || !(await applyWorldDrafts())) return;
  const next = { ...housingTarget.value, ...part };
  await run(async () => {
    const data = await request<HousingData>('housing', { player: playerIndex.value, ...next });
    housingData.value = data;
    housingTarget.value = {
      kind: data.kind,
      index: data.index,
      room: data.room?.room ?? 0,
      layer: data.room?.layer ?? 0,
    };
  });
}
async function loadProgress() {
  if (!state.value.session) return;
  progressData.value = await request<ProgressData>('progress', {
    player: playerIndex.value,
    section: progressSection.value,
  });
}
async function changeProgressSection(section: ProgressSection) {
  if (busy.value || !(await applyWorldDrafts())) return;
  await run(async () => {
    const next = await request<ProgressData>('progress', { player: playerIndex.value, section });
    progressSection.value = section;
    progressData.value = next;
  });
}
async function loadInventory(pageNumber = inventory.value?.page ?? 0) {
  if (!state.value.session) return;
  const token = ++inventoryToken;
  const result = await request<Inventory>('inventory', {
    player: playerIndex.value,
    container: container.value,
    page: pageNumber,
    query: query.value,
  });
  if (token !== inventoryToken) return;
  inventory.value = result;
  if (selected.value) {
    selected.value = result.items.find((item) => item.index === selected.value?.index) ?? null;
    draftItem.value = null;
    quantity.value = selected.value?.quantity || 1;
  }
}
async function command(
  name: string,
  payload: Record<string, unknown> = {},
  success?: Parameters<typeof t>[0],
) {
  if (busy.value) return false;
  if (['open', 'openRecent', 'closeSave', 'legacy'].includes(name)) {
    if (page.value === 'player' && profilePending.value && !(await applyProfile())) return false;
    if (!(await applyWorldDrafts())) return false;
  }
  return await run(async () => {
    accept(await request<State>(name, payload));
    // Reopening a save may change the library capacity (50 vs 100 slots).
    if (['open', 'openRecent', 'closeSave'].includes(name))
    {
      designTarget.value = { ...designTarget.value, index: 0 };
      mapTarget.value = { ...mapTarget.value, marker: 0 };
    }
    if (page.value === 'inventory') await loadInventory();
    if (page.value === 'progress') await loadProgress();
    if (page.value === 'housing' && name !== 'copyGrid') await loadHousing();
    if (page.value === 'mapEditor' && name !== 'copyGrid') await loadMap();
    if (page.value === 'designs') await loadDesigns();
    if (success) toast(success);
  });
}
async function open(kind: string) {
  await command('open', { kind });
  if (state.value.session) page.value = 'overview';
}
async function go(destination: Page) {
  if (busy.value) return;
  if (page.value === 'player' && profilePending.value && !(await applyProfile())) return;
  if (!(await applyWorldDrafts())) return;
  page.value = destination;
  if (destination === 'inventory') await run(() => loadInventory(0));
  if (destination === 'progress') await run(loadProgress);
  if (destination === 'housing') await run(loadHousing);
  if (destination === 'mapEditor') await run(loadMap);
  if (destination === 'designs') await run(loadDesigns);
}
async function applyProfile() {
  if (!playerForm.value?.reportValidity()) return false;
  return await command('setPlayer', { player: playerIndex.value, ...fields.value }, 'applied');
}
async function selectPlayer(index: number) {
  if (busy.value || index === playerIndex.value) return;
  if (page.value === 'player' && profilePending.value && !(await applyProfile())) return;
  if (!(await applyWorldDrafts())) return;
  playerIndex.value = index;
}
async function save() {
  if (page.value === 'player' && profilePending.value && !(await applyProfile())) return;
  if (!(await applyWorldDrafts())) return;
  await command('save', {}, 'saveSuccess');
}
async function applyWorldDrafts() {
  if (page.value === 'mapEditor' && mapPage.value && !(await mapPage.value.applyPending())) return false;
  if (page.value === 'housing' && housingPage.value && !(await housingPage.value.applyPending()))
    return false;
  if (page.value === 'progress' && progressPage.value && !(await progressPage.value.applyPending()))
    return false;
  if (page.value === 'villagers' && residentsPage.value && !(await residentsPage.value.applyPending()))
    return false;
  if (page.value === 'island' && islandPage.value && !(await islandPage.value.applyPending())) return false;
  return true;
}
async function worldCommand(name: string, payload: Record<string, unknown> = {}) {
  if (name === 'copyGrid') return await command(name, payload, 'gridCopied');
  if (['spawnMap', 'editMapParameters', 'replaceMapMatches'].includes(name)) {
    let applied = false;
    const completed = await run(async () => {
      const result = await request<{ state: State; requested: number; applied: number; cancelled: boolean }>(name, payload);
      accept(result.state);
      await loadMap();
      if (!result.cancelled) {
        applied = true;
        notice.value = t('mapEditResult').replace('{0}', String(result.applied)).replace('{1}', String(result.requested));
        clearTimeout(noticeTimer);
        noticeTimer = setTimeout(() => (notice.value = ''), 8000);
      }
    });
    return completed && applied;
  }
  if (name === 'tool' && payload.tool === 'terrain') { await go('mapEditor'); return page.value === 'mapEditor'; }
  if (name === 'nativeMap' && !(await applyWorldDrafts())) return false;
  if (
    (name === 'tool' && ['patterns', 'proDesigns', 'flag', 'tailor'].includes(String(payload.tool))) ||
    (name === 'legacy' && payload.section === 'designs')
  ) {
    if (busy.value || !(await applyWorldDrafts())) return false;
    designTarget.value = { kind: (payload.tool as DesignKind) ?? 'patterns', index: 0 };
    page.value = 'designs';
    return await run(loadDesigns);
  }
  if (
    name === 'tool' &&
    ['playerHouses', 'residentHouses', 'campsite', 'fruitFlowers'].includes(String(payload.tool))
  ) {
    if (busy.value || !(await applyWorldDrafts())) return false;
    housingTarget.value = {
      kind: payload.tool as HousingKind,
      index: payload.tool === 'playerHouses' ? playerIndex.value : 0,
      room: 0,
      layer: 0,
      ...(payload.resident === undefined ? {} : { resident: Number(payload.resident) }),
    };
    page.value = 'housing';
    return await run(loadHousing);
  }
  if (name === 'tool' && payload.tool === 'recycle') {
    await go('inventory');
    if (page.value !== 'inventory') return false;
    container.value = 'recycle';
    return true;
  }
  return await command(
    name,
    { player: playerIndex.value, ...payload },
    ['legacy', 'tool', 'nativeMap', 'mapImport', 'mapExport', 'mapPng', 'designImport', 'designExport', 'designPng', 'housingImport', 'housingExport'].includes(name) ? undefined : 'applied',
  );
}
function chooseSlot(item: ItemView) {
  if (!item.available) return;
  selected.value = item;
  draftItem.value = null;
  quantity.value = item.quantity || 1;
}
async function searchCatalog(pageNumber = 0) {
  if (busy.value) {
    pendingCatalogPage = pageNumber;
    return;
  }
  const token = ++catalogToken;
  const query = catalogQuery.value;
  const category = catalogCategory.value;
  const subcategory = catalogSubcategory.value;
  await run(async () => {
    const result = await request<Catalog>('catalog', { query, category, subcategory, page: pageNumber });
    if (
      token === catalogToken &&
      query === catalogQuery.value &&
      category === catalogCategory.value &&
      subcategory === catalogSubcategory.value
    ) {
      catalog.value = result;
      catalogJump.value = result.page + 1;
      await nextTick();
      dialog.value?.querySelector('.catalog-results')?.scrollTo(0, 0);
    }
  });
}
async function showCatalog(target: 'inventory' | 'housing' | 'map' = 'inventory') {
  catalogTarget = target;
  catalogOpen.value = true;
  await searchCatalog(catalog.value?.page ?? 0);
}
function categoryLabel(id: string) {
  return t(catalogLabels[id as keyof typeof catalogLabels] ?? 'categoryOther');
}
function clothingLabel(id: string) {
  return t(clothingLabels[id as keyof typeof clothingLabels] ?? 'clothesOther');
}
function chooseClothingGroup(id: string) {
  catalogSubcategory.value = id;
  void searchCatalog(0);
}
function chooseCatalog(item: ItemView) {
  if (catalogTarget === 'map') {
    mapPage.value?.chooseItem(item);
    catalogOpen.value = false;
    return;
  }
  if (catalogTarget === 'housing') {
    housingPage.value?.chooseItem(item);
    catalogOpen.value = false;
    return;
  }
  draftItem.value = item;
  quantity.value = 1;
  catalogOpen.value = false;
}
async function applyItem() {
  if (!selected.value || !currentItem.value) return;
  await command(
    'setItem',
    {
      player: playerIndex.value,
      container: container.value,
      index: selected.value.index,
      itemId: currentItem.value.itemId,
      quantity: Number(quantity.value),
    },
    'applied',
  );
}
async function itemAction(action: string) {
  if (!selected.value) return;
  await command(
    action,
    { player: playerIndex.value, container: container.value, index: selected.value.index },
    'applied',
  );
}
async function drop(event: DragEvent) {
  dragging.value = false;
  if (busy.value) return;
  const files = Array.from(event.dataTransfer?.files ?? []);
  if (files.length !== 1) {
    error.value = t('dropHint');
    return;
  }
  await run(async () => {
    accept(await request<State>('drop', {}, files));
    page.value = 'overview';
  });
}
function keyboard(event: KeyboardEvent) {
  if (event.defaultPrevented) return;
  if (event.key === 'Escape') {
    catalogOpen.value = false;
    helpOpen.value = false;
    return;
  }
  if (event.key === 'F11') {
    event.preventDefault();
    void command('fullScreen');
    return;
  }
  if (!(event.ctrlKey || event.metaKey) || !state.value.session || event.altKey) return;
  if (event.key.toLowerCase() === 's') {
    event.preventDefault();
    void save();
  }
  if (editableTarget(event.target)) return;
  if (event.key.toLowerCase() === 'z') {
    event.preventDefault();
    void command(event.shiftKey ? 'redo' : 'undo');
  }
  if (event.key.toLowerCase() === 'y') {
    event.preventDefault();
    void command('redo');
  }
}
watch(playerIndex, () => {
  syncFields(currentPlayer.value);
  selected.value = null;
  draftItem.value = null;
  if (page.value === 'inventory') void run(() => loadInventory(0));
  if (page.value === 'progress') void run(loadProgress);
  if (page.value === 'housing') void run(loadHousing);
  if (page.value === 'designs') void run(loadDesigns);
});
watch(container, () => {
  selected.value = null;
  draftItem.value = null;
  void run(() => loadInventory(0));
});
watch(query, () => {
  clearTimeout(queryTimer);
  queryTimer = setTimeout(() => void run(() => loadInventory(0)), 250);
});
watch(catalogQuery, () => {
  clearTimeout(catalogTimer);
  catalogTimer = setTimeout(() => void searchCatalog(0), 250);
});
watch(catalogCategory, () => {
  catalogSubcategory.value = 'all';
  void searchCatalog(0);
});
watch(busy, (value) => {
  if (!value && pendingCatalogPage !== null && catalogOpen.value) {
    const next = pendingCatalogPage;
    pendingCatalogPage = null;
    void searchCatalog(next);
  }
});
watch(catalogOpen, (value) => {
  if (!value) {
    pendingCatalogPage = null;
    clearTimeout(catalogTimer);
  }
});
watch(
  theme,
  (value) => {
    document.documentElement.dataset.theme = value;
    localStorage.setItem('nhse.theme', value);
  },
  { immediate: true },
);
watch(
  () => catalogOpen.value || helpOpen.value,
  async (open) => {
    await nextTick();
    if (open && dialog.value) {
      lastFocus = document.activeElement as HTMLElement;
      dialog.value.showModal();
    } else {
      dialog.value?.close();
      lastFocus?.focus();
    }
  },
);
listenState(accept);
onMounted(async () => {
  window.addEventListener('keydown', keyboard);
  await run(async () => accept(await request<State>('bootstrap')));
});
onUnmounted(() => {
  window.removeEventListener('keydown', keyboard);
  clearTimeout(queryTimer);
  clearTimeout(catalogTimer);
  clearTimeout(noticeTimer);
});
</script>

<template>
  <VillagerPicker v-if="state.villagerPicker" :context="state.villagerPicker" />
  <div
    v-else
    class="app"
    :class="{ 'is-focused': focused }"
    @dragover.prevent="dragging = true"
    @dragleave.self="dragging = false"
    @drop.prevent="drop"
    :aria-busy="busy"
  >
    <aside v-if="!focused" class="sidebar">
      <div class="brand">
        <span class="brand-mark"><Leaf :size="25" /></span><span>NHSE<small>ISLAND STUDIO</small></span>
      </div>
      <div class="sidebar-label">{{ t('workspace') }}</div>
      <nav :aria-label="t('workspace')">
        <template v-if="state.session"
          ><button
            v-for="item in nav"
            :key="item.id"
            :class="{ active: page === item.id }"
            :aria-current="page === item.id ? 'page' : undefined"
            :disabled="busy"
            @click="go(item.id)"
          >
            <component :is="item.icon" :size="19" />{{ t(item.id)
            }}<span v-if="page === item.id" class="nav-dot"></span></button
        ></template>
        <button v-else class="active" aria-current="page">
          <LayoutDashboard :size="19" />{{ t('home') }}<span class="nav-dot"></span>
        </button>
      </nav>
      <div class="sidebar-bottom">
        <div class="local-badge">
          <ShieldCheck :size="19" /><span>{{ t('localNote') }}</span>
        </div>
        <button class="help-button" @click="helpOpen = true">
          <HelpCircle :size="18" />{{ t('howTitle') }}
        </button>
      </div>
    </aside>
    <div class="app-body">
      <header class="topbar">
        <div class="breadcrumb">
          <Leaf v-if="focused" :size="20" /><span>NHSE</span><span class="separator">/</span
          ><strong>{{ state.session ? state.session.island : t('home') }}</strong>
        </div>
        <div class="top-actions">
          <label class="icon-select"
            ><Globe2 :size="17" /><span class="sr-only">{{ t('language') }}</span
            ><select
              :value="language"
              :disabled="busy"
              @change="command('language', { language: ($event.target as HTMLSelectElement).value })"
            >
              <option v-for="[id, name] in languages" :key="id" :value="id">{{ name }}</option>
            </select></label
          >
          <label class="icon-select theme-select"
            ><SunMoon :size="17" /><span class="sr-only">{{ t('theme') }}</span
            ><select v-model="theme">
              <option value="system">{{ t('system') }}</option>
              <option value="light">{{ t('light') }}</option>
              <option value="dark">{{ t('dark') }}</option>
            </select></label
          >
          <button
            class="icon-button"
            :aria-label="state.fullScreen ? t('exitFullScreen') : t('fullScreen')"
            :title="`${t('fullScreen')} · F11`"
            :disabled="busy || !native"
            @click="command('fullScreen')"
          >
            <Minimize2 v-if="state.fullScreen" :size="19" /><Maximize2 v-else :size="19" />
          </button>
        </div>
      </header>
      <div v-if="!native" class="preview-note">{{ t('offlinePreview') }}</div>
      <div v-if="error" class="error-banner" role="alert">
        <div>
          {{ error }}
          <details v-if="errorDetail">
            <summary>{{ t('advanced') }}</summary>
            {{ errorDetail }}
          </details>
        </div>
        <button class="icon-button" :aria-label="t('closeDialog')" @click="error = ''">
          <X :size="18" />
        </button>
      </div>
      <main v-if="!state.session" class="home-content">
        <section class="welcome">
          <div class="welcome-copy">
            <div class="eyebrow"><span></span>{{ t('homeEyebrow') }}</div>
            <h1>{{ t('homeTitle') }}</h1>
            <p>{{ t('homeDescription') }}</p>
            <div class="welcome-actions">
              <button class="button primary" :disabled="busy" @click="open('file')">
                <FolderOpen :size="19" />{{ t('openSave') }}<ArrowRight :size="17" /></button
              ><button class="button" :disabled="busy" @click="open('folder')">
                {{ t('openFolder') }}
              </button>
            </div>
            <div class="welcome-note"><ShieldCheck :size="15" />{{ t('localNote') }}</div>
          </div>
          <div class="island-art" aria-hidden="true">
            <div class="sun"></div>
            <div class="cloud cloud-one"></div>
            <div class="cloud cloud-two"></div>
            <div class="island-water"></div>
            <div class="island-sand"></div>
            <div class="island-ground"></div>
            <div class="house">
              <div class="roof"></div>
              <div class="house-window"></div>
              <div class="door"></div>
            </div>
            <div class="tree tree-one"><span></span><i></i></div>
            <div class="tree tree-two"><span></span><i></i></div>
            <div class="art-leaf"><Leaf :size="22" /></div>
            <div class="art-badge"><Check :size="16" /><span>NHSE</span></div>
          </div>
        </section>
        <section class="open-area">
          <button class="dropzone" :class="{ dragging }" :disabled="busy" @click="open('file')">
            <span class="drop-icon"><Upload :size="24" /></span
            ><span
              ><strong>{{ t('dropTitle') }}</strong
              ><small>{{ t('dropHint') }}</small></span
            ><Plus :size="20" />
          </button>
          <div class="recent-card">
            <div class="section-caption">{{ t('recent') }}</div>
            <template v-if="state.recent"
              ><span class="recent-path" :title="state.recent">{{ state.recent }}</span
              ><button class="text-button" :disabled="busy" @click="command('openRecent')">
                {{ t('openRecent') }}<ArrowRight :size="16" /></button
            ></template>
            <p v-else>{{ t('noRecent') }}</p>
          </div>
        </section>
        <section class="getting-started">
          <div class="section-heading">
            <div>
              <div class="eyebrow muted">{{ t('quickStart') }}</div>
              <h2>{{ t('howTitle') }}</h2>
            </div>
            <span class="small-leaf"><Leaf :size="20" /></span>
          </div>
          <div class="steps">
            <article v-for="number in [1, 2, 3]" :key="number">
              <span class="step-number">0{{ number }}</span>
              <h3>{{ stepText(number, 'Title') }}</h3>
              <p>{{ stepText(number, 'Body') }}</p>
            </article>
          </div>
        </section>
      </main>
      <main v-else class="workspace-content">
        <div class="workspace-heading">
          <div>
            <div class="eyebrow muted">{{ state.session.island }}</div>
            <h1>{{ t(page) }}</h1>
          </div>
          <div class="workspace-controls">
            <button
              class="icon-button"
              :aria-label="focused ? t('exitFocus') : t('focus')"
              :title="focused ? t('exitFocus') : t('focus')"
              @click="focused = !focused"
            >
              <PanelLeftOpen v-if="focused" :size="20" /><PanelLeftClose v-else :size="20" />
            </button>
          </div>
        </div>
        <div
          v-if="
            page === 'overview' ||
            page === 'player' ||
            page === 'inventory' ||
            page === 'progress' ||
            page === 'housing' ||
            page === 'designs'
          "
          class="player-switcher"
          role="group"
          :aria-label="t('switchPlayer')"
        >
          <button
            v-for="player in state.session.players"
            :key="player.index"
            class="player-chip"
            :class="{ active: player.index === playerIndex }"
            :aria-pressed="player.index === playerIndex"
            :aria-label="`${t('player')} ${player.name}`"
            :title="player.name"
            :disabled="busy"
            @click="selectPlayer(player.index)"
          >
            <span class="player-avatar"
              ><img v-if="player.avatar" :src="player.avatar" alt="" /><UserRound v-else :size="23"
            /></span>
            <span class="player-chip-name">{{ player.name || `#${player.index + 1}` }}</span>
            <Check v-if="player.index === playerIndex" :size="15" class="player-active-mark" />
          </button>
        </div>
        <nav v-if="focused" class="focus-nav" :aria-label="t('workspace')">
          <button
            v-for="item in nav"
            :key="item.id"
            :class="{ active: page === item.id }"
            @click="go(item.id)"
          >
            {{ t(item.id) }}
          </button>
        </nav>
        <section v-if="page === 'overview'" class="overview-page">
          <div class="overview-welcome">
            <span class="overview-leaf"><Leaf :size="32" /></span>
            <h2>{{ t('overviewTitle') }}</h2>
            <p>{{ t('overviewHint') }}</p>
          </div>
          <div class="task-grid">
            <button class="task-card" @click="go('player')">
              <UserRound :size="27" /><strong>{{ t('editPlayer') }}</strong
              ><span>{{ currentPlayer?.name }}</span
              ><ArrowRight :size="19" /></button
            ><button class="task-card" @click="go('inventory')">
              <Package :size="27" /><strong>{{ t('editInventory') }}</strong
              ><span>{{ t('pocket') }} · {{ currentPlayer?.pocketCapacity }}</span
              ><ArrowRight :size="19" /></button
            ><button class="task-card" @click="go('villagers')">
              <Users :size="27" /><strong>{{ t('villagers') }}</strong
              ><span>{{ t('residentProfile') }}</span
              ><ArrowRight :size="19" /></button
            ><button class="task-card" @click="go('island')">
              <Map :size="27" /><strong>{{ t('island') }}</strong
              ><span>{{ t('islandBasics') }}</span
              ><ArrowRight :size="19" />
            </button>
          </div>
        </section>
        <section v-else-if="page === 'player'" class="player-page">
          <p class="page-description">{{ t('playerHint') }}</p>
          <form ref="playerForm" class="balance-form" @submit.prevent="applyProfile">
            <div class="form-section-title">
              <UserRound :size="19" />
              <h2>{{ t('identity') }}</h2>
            </div>
            <label
              ><span>{{ t('playerName') }}</span
              ><input
                v-model="fields.name"
                :aria-label="t('playerName')"
                aria-describedby="player-name-help"
                required
                maxlength="10"
                :disabled="busy"
                :spellcheck="false"
                autocomplete="off"
              /><small id="player-name-help" class="field-help">{{ t('nameHint') }}</small></label
            >
            <label
              ><span>{{ t('islandName') }}</span
              ><input
                v-model="fields.island"
                :aria-label="t('islandName')"
                aria-describedby="island-name-help"
                required
                maxlength="10"
                :disabled="busy"
                :spellcheck="false"
                autocomplete="off"
              /><small id="island-name-help" class="field-help">{{ t('islandNameHint') }}</small></label
            >
            <div class="form-section-title">
              <Package :size="19" />
              <h2>{{ t('balancesAndCapacity') }}</h2>
            </div>
            <label v-for="field in balanceFields" :key="field"
              ><span>{{ t(field) }}</span>
              <div class="unit-input">
                <input
                  v-model.number="fields[field]"
                  type="number"
                  required
                  min="0"
                  max="999999999"
                  step="1"
                  :disabled="busy"
                /><span>{{ t(field === 'wallet' || field === 'bank' ? 'currency' : 'milesUnit') }}</span>
              </div></label
            >
            <label
              ><span>{{ t('pocketCapacity') }}</span
              ><input
                v-model.number="fields.pocketCapacity"
                type="number"
                required
                min="0"
                :max="currentPlayer?.maxPocketCapacity ?? 40"
                step="1"
                :disabled="busy"
            /></label>
            <label
              ><span>{{ t('storageCapacity') }}</span
              ><input
                v-model.number="fields.storageCapacity"
                type="number"
                required
                min="0"
                :max="currentPlayer?.maxStorageCapacity ?? 5000"
                step="1"
                :disabled="busy"
            /></label>
            <label v-if="currentPlayer?.hotelTickets != null"
              ><span>{{ t('hotelTickets') }}</span
              ><input
                v-model.number="fields.hotelTickets"
                type="number"
                required
                min="0"
                max="2147483647"
                step="1"
                :disabled="busy"
            /></label>
            <label v-if="currentPlayer?.poki != null"
              ><span>{{ t('poki') }}</span
              ><input
                v-model.number="fields.poki"
                type="number"
                required
                min="0"
                max="2147483647"
                step="1"
                :disabled="busy"
            /></label>
            <div class="form-actions">
              <button class="button primary" :disabled="busy">{{ t('apply') }}<Check :size="17" /></button
              ><button
                type="button"
                class="text-button"
                :disabled="busy"
                @click="command('legacy', { section: 'player' })"
              >
                {{ t('legacy') }}<ArrowRight :size="16" />
              </button>
            </div>
          </form>
        </section>
        <section v-else-if="page === 'inventory'" class="inventory-page">
          <p class="page-description">{{ t('inventoryHint') }}</p>
          <div class="inventory-layout">
            <div class="inventory-main">
              <div class="inventory-toolbar">
                <div class="segmented">
                  <button
                    :class="{ active: container === 'pocket' }"
                    :disabled="busy"
                    @click="container = 'pocket'"
                  >
                    {{ t('pocket') }}</button
                  ><button
                    :class="{ active: container === 'storage' }"
                    :disabled="busy"
                    @click="container = 'storage'"
                  >
                    {{ t('storage') }}</button
                  ><button
                    :class="{ active: container === 'recycle' }"
                    :disabled="busy"
                    @click="container = 'recycle'"
                  >
                    {{ t('recycle') }}
                  </button>
                </div>
                <span class="inventory-count"
                  >{{ t('used') }} {{ inventory?.used ?? 0 }} / {{ inventory?.total ?? 0 }}</span
                >
              </div>
              <label class="search-field"
                ><Search :size="18" /><span class="sr-only">{{ t('searchInventory') }}</span
                ><input v-model="query" type="search" :placeholder="t('searchInventory')" :disabled="busy"
              /></label>
              <InventoryGrid :data="inventory" :player="playerIndex" :container="container"
                :scope="JSON.stringify([page, query, inventory?.page, state.session?.title])" :busy="busy"
                :selected="selected" :clipboard="state.session?.clipboard ?? null" :choose="chooseSlot" :execute="worldCommand" />
              <div v-if="inventory?.items.length === 0" class="empty-result">
                {{ t('noResults') }}
              </div>
              <div v-if="inventory && inventory.pages > 1" class="pagination">
                <button
                  class="icon-button"
                  :disabled="busy || inventory.page === 0"
                  :aria-label="t('previous')"
                  @click="run(() => loadInventory(inventory!.page - 1))"
                >
                  <ChevronLeft :size="19" /></button
                ><span>{{ t('page') }} {{ inventory.page + 1 }} / {{ inventory.pages }}</span
                ><button
                  class="icon-button"
                  :disabled="busy || inventory.page >= inventory.pages - 1"
                  :aria-label="t('next')"
                  @click="run(() => loadInventory(inventory!.page + 1))"
                >
                  <ChevronRight :size="19" />
                </button>
              </div>
            </div>
            <aside class="inspector">
              <h2>{{ t('itemDetails') }}</h2>
              <template v-if="selected"
                ><div class="item-preview">
                  <img v-if="currentItem?.image" :src="currentItem.image" alt="" /><Plus v-else :size="36" />
                </div>
                <h3>{{ currentItem?.empty ? t('empty') : currentItem?.name }}</h3>
                <p class="slot-position">{{ t('slot') }} {{ selected.index + 1 }}</p>
                <button class="button choose-item" :disabled="busy" @click="showCatalog()">
                  <Search :size="17" />{{ t('replace') }}
                </button>
                <form v-if="currentItem && !currentItem.empty" @submit.prevent="applyItem">
                  <label class="quantity-field"
                    >{{ t('quantity')
                    }}<input
                      v-model.number="quantity"
                      type="number"
                      required
                      min="1"
                      :max="currentItem.maxQuantity"
                      step="1"
                      :disabled="busy || currentItem.maxQuantity === 1"
                  /></label>
                  <p class="field-help">{{ t('quantityHint') }} ({{ currentItem.maxQuantity }})</p>
                  <button class="button primary inspector-apply" :disabled="busy">
                    {{ t('apply') }}<Check :size="17" />
                  </button>
                </form>
                <div class="inspector-more">
                  <button
                    class="text-button"
                    :disabled="busy || !!draftItem"
                    @click="itemAction('detailItem')"
                  >
                    <SlidersHorizontal :size="16" />{{ t('advancedItem') }}</button
                  ><button
                    v-if="!selected.empty"
                    class="text-button destructive"
                    :disabled="busy"
                    @click="itemAction('deleteItem')"
                  >
                    <X :size="16" />{{ t('delete') }}
                  </button>
                </div></template
              >
              <div v-else class="inspector-empty">
                <Package :size="34" />
                <h3>{{ t('selectSlot') }}</h3>
                <p>{{ t('selectSlotHint') }}</p>
              </div>
            </aside>
          </div>
        </section>
        <MapPage :clipboard="state.session?.clipboard ?? null" v-else-if="page === 'mapEditor'" ref="mapPage" :data="mapData" :busy="busy"
          :execute="worldCommand" :navigate="navigateMap" :pick-item="() => showCatalog('map')" />
        <DesignPage
          v-else-if="page === 'designs'"
          :data="designData"
          :busy="busy"
          :execute="worldCommand"
          :navigate="navigateDesigns"
        />
        <HousingPage
          v-else-if="page === 'housing'"
          ref="housingPage"
          :clipboard="state.session?.clipboard ?? null"
          :data="housingData"
          :player="playerIndex"
          :busy="busy"
          :execute="worldCommand"
          :navigate="navigateHousing"
          :pick-item="() => showCatalog('housing')"
        />
        <ProgressPage
          v-else-if="page === 'progress'"
          ref="progressPage"
          :data="progressData"
          :section="progressSection"
          :player="playerIndex"
          :busy="busy"
          :execute="worldCommand"
          :change-section="changeProgressSection"
        />
        <ResidentsPage
          v-else-if="page === 'villagers'"
          ref="residentsPage"
          :residents="state.session.residents"
          :busy="busy"
          :execute="worldCommand"
        />
        <IslandPage
          v-else-if="page === 'island'"
          ref="islandPage"
          :settings="state.session.islandSettings"
          :turnips="state.session.turnips"
          :busy="busy"
          :execute="worldCommand"
        />
        <section v-else class="tools-page">
          <h2>{{ t('toolsTitle') }}</h2>
          <p class="page-description">{{ t('toolsHint') }}</p>
          <div class="task-grid">
            <button
              v-for="tool in [
                { id: 'villagers', icon: Users },
                { id: 'map', icon: Map },
                { id: 'designs', icon: Palette },
              ] as const"
              :key="tool.id"
              class="task-card"
              :disabled="busy"
              @click="
                tool.id === 'villagers'
                  ? go('villagers')
                  : tool.id === 'map'
                    ? go('island')
                    : worldCommand('legacy', { section: tool.id })
              "
            >
              <component :is="tool.icon" :size="28" /><strong>{{ t(tool.id) }}</strong
              ><ArrowRight :size="19" />
            </button>
          </div>
          <div class="legacy-note">
            <SlidersHorizontal :size="22" />
            <div>
              <h3>{{ t('advanced') }}</h3>
              <p>{{ t('legacyHint') }}</p>
            </div>
            <button class="button" :disabled="busy" @click="command('legacy', { section: 'advanced' })">
              {{ t('legacy') }}<ArrowRight :size="16" />
            </button>
          </div>
        </section>
      </main>
      <footer class="statusbar">
        <span v-if="busy" class="status-text"><span class="spinner"></span>{{ t('loading') }}</span
        ><span v-else class="status-text"
          ><span class="status-dot" :class="{ changed: state.session?.dirty }"></span
          >{{ state.session ? t(state.session.dirty ? 'dirty' : 'saved') : t('ready') }}</span
        ><template v-if="state.session"
          ><div class="save-actions">
            <button
              class="icon-button"
              :aria-label="t('undo')"
              :title="`${t('undo')} · Ctrl+Z`"
              :disabled="busy || !state.session.canUndo"
              @click="command('undo')"
            >
              <Undo2 :size="19" /></button
            ><button
              class="icon-button"
              :aria-label="t('redo')"
              :title="`${t('redo')} · Ctrl+Y`"
              :disabled="busy || !state.session.canRedo"
              @click="command('redo')"
            >
              <Redo2 :size="19" /></button
            ><button class="button" :disabled="busy" @click="command('closeSave')">
              {{ t('closeSave') }}</button
            ><button class="button primary" :disabled="busy" @click="save">
              <Save :size="17" />{{ t('save') }}
            </button>
          </div></template
        ><span v-else class="footer-brand">NHSE · ISLAND STUDIO</span>
      </footer>
    </div>
    <div v-if="notice" class="toast" role="status"><CheckCheck :size="19" />{{ notice }}</div>
    <div v-if="dragging" class="drop-overlay" @dragleave.prevent="dragging = false" @drop.prevent.stop="drop">
      <Upload :size="36" /><strong>{{ t('dropTitle') }}</strong
      ><span>{{ t('dropHint') }}</span>
    </div>
    <dialog
      v-if="catalogOpen || helpOpen"
      ref="dialog"
      class="modal"
      :class="{ 'catalog-modal': catalogOpen }"
      :aria-label="catalogOpen ? t('catalogTitle') : t('howTitle')"
      @cancel.prevent="
        catalogOpen = false;
        helpOpen = false;
      "
    >
      <div class="modal-heading">
        <div>
          <div class="eyebrow muted">NHSE</div>
          <h2>{{ catalogOpen ? t('catalogTitle') : t('howTitle') }}</h2>
        </div>
        <button
          class="icon-button"
          :aria-label="t('closeDialog')"
          @click="
            catalogOpen = false;
            helpOpen = false;
          "
        >
          <X :size="21" />
        </button>
      </div>
      <template v-if="catalogOpen">
        <p class="page-description">{{ t('catalogHint') }}</p>
        <label class="search-field"
          ><Search :size="18" /><span class="sr-only">{{ t('searchItems') }}</span
          ><input v-model="catalogQuery" autofocus type="search" :placeholder="t('searchItems')"
        /></label>
        <div class="catalog-browser">
          <nav class="catalog-categories" :aria-label="t('itemCategories')">
            <button
              v-for="category in catalog?.categories ?? []"
              :key="category.id"
              :class="{ active: catalogCategory === category.id }"
              :aria-pressed="catalogCategory === category.id"
              :disabled="busy || category.count === 0"
              @click="catalogCategory = category.id"
            >
              <span>{{ categoryLabel(category.id) }}</span
              ><small>{{ category.count.toLocaleString() }}</small>
            </button>
          </nav>
          <section class="catalog-main" :aria-label="t('catalogTitle')" :aria-busy="busy">
            <div
              v-if="catalogCategory === 'clothing'"
              class="catalog-subcategories"
              role="group"
              :aria-label="t('clothingSubcategories')"
            >
              <button
                v-for="group in catalog?.subcategories ?? []"
                :key="group.id"
                :class="{ active: catalogSubcategory === group.id }"
                :aria-pressed="catalogSubcategory === group.id"
                :disabled="busy || group.count === 0"
                @click="chooseClothingGroup(group.id)"
              >
                {{ clothingLabel(group.id) }}
                <small>{{ group.count.toLocaleString() }}</small>
              </button>
            </div>
            <div class="catalog-summary" role="status">
              <span
                >{{ categoryLabel(catalogCategory) }} · {{ (catalog?.total ?? 0).toLocaleString() }}
                {{ t('catalogItems') }}</span
              ><span v-if="busy">{{ t('loading') }}</span
              ><span v-else-if="catalog">{{ t('page') }} {{ catalog.page + 1 }} / {{ catalog.pages }}</span>
            </div>
            <div class="catalog-results">
              <button
                v-for="item in catalog?.items ?? []"
                :key="item.itemId"
                class="catalog-item"
                :aria-label="item.name"
                :title="item.name"
                :disabled="busy"
                @click="chooseCatalog(item)"
              >
                <span class="catalog-image"
                  ><img v-if="item.image" :src="item.image" alt="" loading="lazy" /><Leaf
                    v-else
                    :size="30" /></span
                ><span class="catalog-item-name">{{ item.name }}</span
                ><small>#{{ item.itemId }}</small>
              </button>
              <p v-if="!busy && catalog?.items.length === 0" class="empty-result">
                {{ t('noResults') }}
              </p>
            </div>
            <form v-if="catalog" class="catalog-pagination" @submit.prevent="searchCatalog(catalogJump - 1)">
              <div class="catalog-page-buttons">
                <button
                  type="button"
                  class="icon-button"
                  :disabled="busy || catalog.page === 0"
                  :aria-label="t('firstPage')"
                  @click="searchCatalog(0)"
                >
                  <ChevronsLeft :size="18" /></button
                ><button
                  type="button"
                  class="icon-button"
                  :disabled="busy || catalog.page === 0"
                  :aria-label="t('previous')"
                  @click="searchCatalog(catalog.page - 1)"
                >
                  <ChevronLeft :size="18" /></button
                ><button
                  type="button"
                  class="icon-button"
                  :disabled="busy || catalog.page >= catalog.pages - 1"
                  :aria-label="t('next')"
                  @click="searchCatalog(catalog.page + 1)"
                >
                  <ChevronRight :size="18" /></button
                ><button
                  type="button"
                  class="icon-button"
                  :disabled="busy || catalog.page >= catalog.pages - 1"
                  :aria-label="t('lastPage')"
                  @click="searchCatalog(catalog.pages - 1)"
                >
                  <ChevronsRight :size="18" />
                </button>
              </div>
              <label
                >{{ t('jumpToPage')
                }}<input
                  v-model.number="catalogJump"
                  type="number"
                  required
                  min="1"
                  :max="catalog.pages"
                  :disabled="busy" /></label
              ><button class="button" :disabled="busy">{{ t('go') }}</button>
            </form>
          </section>
        </div>
      </template>
      <div v-else class="help-steps">
        <article v-for="number in [1, 2, 3]" :key="number">
          <h3>{{ stepText(number, 'Title') }}</h3>
          <p>{{ stepText(number, 'Body') }}</p>
        </article>
        <p>{{ t('keyHints') }}</p>
      </div>
    </dialog>
  </div>
</template>
