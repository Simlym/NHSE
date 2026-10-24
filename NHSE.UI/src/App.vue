<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  ArrowRight,
  Check,
  CheckCheck,
  ChevronLeft,
  ChevronRight,
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
  LockKeyhole,
} from '@lucide/vue';
import { languages, language, setLanguage, t, hasText } from './i18n';
import {
  listenState,
  native,
  request,
  type Inventory,
  type ItemView,
  type Player,
  type State,
} from './bridge';

function stepText(number: number, suffix: 'Title' | 'Body') {
  return t(('step' + number + suffix) as Parameters<typeof t>[0]);
}

type Page = 'overview' | 'player' | 'inventory' | 'tools';
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
const catalog = ref<ItemView[]>([]);
const catalogOpen = ref(false);
const helpOpen = ref(false);
const busy = ref(false);
const error = ref('');
const errorDetail = ref('');
const notice = ref('');
const dragging = ref(false);
const focused = ref(false);
const theme = ref(localStorage.getItem('nhse.theme') ?? 'system');
const fields = ref({ wallet: 0, bank: 0, miles: 0, earnedMiles: 0 });
const currentPlayer = computed(() => state.value.session?.players.find((p) => p.index === playerIndex.value));
const currentItem = computed(() => draftItem.value ?? selected.value);
const balanceFields = ['wallet', 'bank', 'miles', 'earnedMiles'] as const;
const nav = [
  { id: 'overview', icon: LayoutDashboard },
  { id: 'player', icon: UserRound },
  { id: 'inventory', icon: Package },
  { id: 'tools', icon: Map },
] as const;
let inventoryToken = 0,
  catalogToken = 0;
let queryTimer: ReturnType<typeof setTimeout> | undefined;
let catalogTimer: ReturnType<typeof setTimeout> | undefined;
let noticeTimer: ReturnType<typeof setTimeout> | undefined;
let lastFocus: HTMLElement | null = null;
const dialog = ref<HTMLDialogElement | null>(null);

function syncFields(player?: Player) {
  if (player)
    fields.value = {
      wallet: player.wallet,
      bank: player.bank,
      miles: player.miles,
      earnedMiles: player.earnedMiles,
    };
}
function accept(next: State) {
  state.value = next;
  setLanguage(next.language);
  localStorage.setItem('nhse.language', language.value);
  if (!next.session?.players.some((p) => p.index === playerIndex.value))
    playerIndex.value = next.session?.players[0]?.index ?? 0;
  syncFields(currentPlayer.value);
}
function toast(key: Parameters<typeof t>[0]) {
  notice.value = t(key);
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => (notice.value = ''), 5000);
}
async function run(action: () => Promise<void>) {
  if (busy.value) return;
  busy.value = true;
  error.value = '';
  errorDetail.value = '';
  try {
    await action();
  } catch (reason) {
    const e = reason instanceof Error ? reason : new Error('operationFailed');
    error.value = t(hasText(e.message) ? e.message : 'operationFailed');
    errorDetail.value = typeof e.cause === 'string' ? e.cause : '';
  } finally {
    busy.value = false;
  }
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
  await run(async () => {
    accept(await request<State>(name, payload));
    if (page.value === 'inventory') await loadInventory();
    if (success) toast(success);
  });
}
async function open(kind: string) {
  await command('open', { kind });
  if (state.value.session) page.value = 'overview';
}
async function go(destination: Page) {
  if (busy.value) return;
  page.value = destination;
  if (destination === 'inventory') await run(() => loadInventory(0));
}
function chooseSlot(item: ItemView) {
  if (!item.available) return;
  selected.value = item;
  draftItem.value = null;
  quantity.value = item.quantity || 1;
}
async function searchCatalog() {
  const token = ++catalogToken;
  const result = await request<ItemView[]>('catalog', { query: catalogQuery.value });
  if (token === catalogToken) catalog.value = result;
}
async function showCatalog() {
  catalogQuery.value = '';
  catalog.value = [];
  await run(searchCatalog);
  if (!error.value) catalogOpen.value = true;
}
function chooseCatalog(item: ItemView) {
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
    void command('save', {}, 'saveSuccess');
  }
  const target = event.target as HTMLElement;
  if (target.matches('input,textarea,select,[contenteditable=true]')) return;
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
  catalogTimer = setTimeout(() => void run(searchCatalog), 250);
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
  <div
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
              ><button class="button" :disabled="busy" @click="open('folder')">{{ t('openFolder') }}</button>
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
            <label class="player-select"
              ><UserRound :size="17" /><span class="sr-only">{{ t('player') }}</span
              ><select v-model="playerIndex" :disabled="busy">
                <option v-for="player in state.session.players" :key="player.index" :value="player.index">
                  {{ player.name || `#${player.index + 1}` }}
                </option>
              </select></label
            ><button
              class="icon-button"
              :aria-label="focused ? t('exitFocus') : t('focus')"
              :title="focused ? t('exitFocus') : t('focus')"
              @click="focused = !focused"
            >
              <PanelLeftOpen v-if="focused" :size="20" /><PanelLeftClose v-else :size="20" />
            </button>
          </div>
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
            ><button class="task-card" @click="go('tools')">
              <Map :size="27" /><strong>{{ t('tools') }}</strong
              ><span>{{ t('map') }} · {{ t('villagers') }}</span
              ><ArrowRight :size="19" />
            </button>
          </div>
        </section>
        <section v-else-if="page === 'player'" class="player-page">
          <p class="page-description">{{ t('playerHint') }}</p>
          <form
            class="balance-form"
            @submit.prevent="command('setPlayer', { player: playerIndex, ...fields }, 'applied')"
          >
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
                    {{ t('storage') }}
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
              <div
                class="item-grid"
                :class="{ 'pocket-grid': container === 'pocket' }"
                :aria-label="t(container === 'pocket' ? 'pocket' : 'storage')"
              >
                <button
                  v-for="item in inventory?.items ?? []"
                  :key="item.index"
                  class="item-slot"
                  :class="{
                    selected: selected?.index === item.index,
                    'is-empty': item.empty,
                    locked: !item.available,
                  }"
                  :disabled="busy || !item.available"
                  :aria-label="`${t('slot')} ${item.index + 1}: ${!item.available ? t('locked') : item.empty ? t('empty') : item.name}`"
                  :aria-pressed="selected?.index === item.index"
                  @click="chooseSlot(item)"
                >
                  <span class="slot-index">{{ item.index + 1 }}</span
                  ><LockKeyhole v-if="!item.available" :size="18" /><img
                    v-else-if="item.image"
                    :src="item.image"
                    alt=""
                  /><Plus v-else :size="17" /><span
                    v-if="!item.empty && item.quantity > 1"
                    class="item-count"
                    >{{ item.quantity }}</span
                  >
                </button>
              </div>
              <div v-if="inventory?.items.length === 0" class="empty-result">{{ t('noResults') }}</div>
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
                <button class="button choose-item" :disabled="busy" @click="showCatalog">
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
              @click="command('legacy', { section: tool.id })"
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
            ><button class="button primary" :disabled="busy" @click="command('save', {}, 'saveSuccess')">
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
      <template v-if="catalogOpen"
        ><p class="page-description">{{ t('catalogHint') }}</p>
        <label class="search-field"
          ><Search :size="18" /><span class="sr-only">{{ t('searchItems') }}</span
          ><input v-model="catalogQuery" autofocus type="search" :placeholder="t('searchItems')"
        /></label>
        <div class="catalog-results">
          <button
            v-for="item in catalog"
            :key="item.itemId"
            class="catalog-item"
            :disabled="busy"
            @click="chooseCatalog(item)"
          >
            <img v-if="item.image" :src="item.image" alt="" /><span>{{ item.name }}</span
            ><Plus :size="17" />
          </button>
          <p v-if="catalog.length === 0">{{ t('noResults') }}</p>
        </div></template
      >
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
