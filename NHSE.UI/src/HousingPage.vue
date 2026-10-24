<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { t } from './i18n';
import type { HousingData, HousingTarget, HousingTile, ItemView, RoomItem } from './bridge';
import HousingFields from './HousingFields.vue';
import { housingKinds, housingName, housingLabel } from './housingLabels';
const props = defineProps<{
  data: HousingData | null;
  player: number;
  busy: boolean;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>;
  navigate: (target: Partial<HousingTarget>) => Promise<void>;
  pickItem: () => void;
}>();
const form = ref<HTMLFormElement | null>(null);
const tileForm = ref<HTMLFormElement | null>(null);
const selected = ref({ x: 0, y: 0 });
const autoExtensions = ref(true);
const noOverwrite = ref(true);
function stored() {
  return Object.fromEntries(
    [...(props.data?.fields ?? []), ...(props.data?.flags ?? [])].map((f) => [f.key, f.value]),
  );
}
const fields = ref(stored());
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(stored()));
const selectedTile = computed(() =>
  props.data?.room?.tiles.find((t) => t.x === selected.value.x && t.y === selected.value.y),
);
const emptyItem = (): RoomItem => ({ itemId: 65534, systemParam: 0, additionalParam: 0, freeParam: 0 });
const item = ref<RoomItem>(emptyItem());
const pendingTile = computed(
  () => selectedTile.value && JSON.stringify(item.value) !== JSON.stringify(selectedTile.value.item),
);
watch(
  () => JSON.stringify([props.player, props.data?.kind, props.data?.index, stored()]),
  () => {
    fields.value = stored();
  },
  { immediate: true },
);
watch(
  () =>
    JSON.stringify([
      props.player,
      props.data?.kind,
      props.data?.index,
      props.data?.room?.room,
      props.data?.room?.layer,
      selectedTile.value?.item,
    ]),
  () => {
    item.value = { ...(selectedTile.value?.item ?? emptyItem()) };
  },
);
const commonKeys = new Set([
  'HouseLevel',
  'HouseStatus',
  'WallUniqueID',
  'RoofUniqueID',
  'DoorUniqueID',
  'Loan',
  'Occupied',
  'VisitorCode',
  'VisitDate',
  'ProfileFruit',
  'SpecialtyFruit',
  'SisterFruit',
  'SpecialtyFlower',
  'SisterFlower',
]);
const primaryFields = computed(() => props.data?.fields.filter((f) => commonKeys.has(f.key)) ?? []);
const advancedFields = computed(() => props.data?.fields.filter((f) => !commonKeys.has(f.key)) ?? []);
const tileKeys = ['itemId', 'systemParam', 'additionalParam', 'freeParam'] as const;
const tileNames = ['ItemId', 'SystemParam', 'AdditionalParam', 'FreeParam'];
async function applyFields() {
  if (!props.data || !pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  return await props.execute('setHousing', {
    kind: props.data.kind,
    index: props.data.index,
    values: { ...fields.value },
  });
}
async function applyTile(remove = false) {
  if (!props.data?.room || !selectedTile.value) return true;
  if (!remove && !tileForm.value?.reportValidity()) return false;
  return await props.execute('setRoomItem', {
    index: props.data.index,
    room: props.data.room.room,
    layer: props.data.room.layer,
    x: selected.value.x,
    y: selected.value.y,
    ...item.value,
    autoExtensions: autoExtensions.value,
    noOverwrite: noOverwrite.value,
    delete: remove,
  });
}
async function applyPending() {
  if (pending.value && !form.value?.reportValidity()) return false;
  if (pendingTile.value && !tileForm.value?.reportValidity()) return false;
  if (!(await applyFields())) return false;
  return !pendingTile.value || (await applyTile());
}
async function selectTile(tile: HousingTile) {
  if (!(await applyPending())) return;
  const current = props.data?.room?.tiles.find((t) => t.x === tile.x && t.y === tile.y) ?? tile;
  selected.value =
    autoExtensions.value && current.extension
      ? { x: current.rootX, y: current.rootY }
      : { x: current.x, y: current.y };
  item.value = { ...(selectedTile.value?.item ?? emptyItem()) };
}
function chooseItem(choice: ItemView) {
  item.value = { itemId: choice.itemId, systemParam: 0, additionalParam: 0, freeParam: 0 };
}
function changeSelect(event: Event, field: 'index' | 'room' | 'layer') {
  const input = event.target as HTMLSelectElement;
  const value = Number(input.value);
  input.value = String(field === 'index' ? (props.data?.index ?? 0) : (props.data?.room?.[field] ?? 0));
  void props.navigate({ [field]: value });
}
defineExpose({ applyPending, chooseItem });
</script>

<template>
  <section class="housing-page">
    <p class="page-description">{{ t('housingHint') }}</p>
    <div class="segmented housing-tabs" role="group" :aria-label="t('housing')">
      <button
        v-for="kind in housingKinds"
        :key="kind"
        :class="{ active: data?.kind === kind }"
        :aria-pressed="data?.kind === kind"
        :disabled="busy"
        @click="navigate({ kind, index: 0, room: 0, layer: 0 })"
      >
        {{ housingName(kind) }}
      </button>
    </div>
    <template v-if="data">
      <label v-if="data.entries.length" class="housing-selector"
        >{{ t('housingSelect')
        }}<select :value="data.index" :disabled="busy" @change="changeSelect($event, 'index')">
          <option v-for="entry in data.entries" :key="entry.index" :value="entry.index">
            {{ entry.index + 1 }} · {{ entry.name }}
          </option>
        </select></label
      >
      <p v-if="data.locked" class="field-help">{{ t('housingCampLocked') }}</p>
      <form ref="form" class="housing-settings" @submit.prevent="applyFields">
        <h2>{{ housingName(data.kind) }}</h2>
        <HousingFields v-model="fields" :fields="primaryFields" :busy="busy || data.locked" />
        <p v-if="data.kind === 'campsite'" class="field-help">{{ t('housingCampHint') }}</p>
        <p v-if="data.kind === 'fruitFlowers'" class="field-help">{{ t('housingFruitHint') }}</p>
        <details v-if="advancedFields.length" class="housing-advanced">
          <summary>{{ t('advanced') }}</summary>
          <HousingFields v-model="fields" :fields="advancedFields" :busy="busy" />
        </details>
        <details v-if="data.flags.length" class="housing-flags">
          <summary>{{ t('housingEventFlag') }}</summary>
          <HousingFields v-model="fields" :fields="data.flags" :busy="busy" />
        </details>
        <div class="form-actions">
          <button class="button primary" :disabled="busy || data.locked || !pending">{{ t('apply') }}</button>
        </div>
      </form>
      <section v-if="data.room" class="housing-room">
        <h2>{{ t('housingRoomEditor') }}</h2>
        <div class="housing-room-controls">
          <label
            >{{ t('housingRoom')
            }}<select :value="data.room.room" :disabled="busy" @change="changeSelect($event, 'room')">
              <option v-for="i in 6" :key="i" :value="i - 1">
                {{ t('housingRooms').split('|')[i - 1] }}
              </option>
            </select></label
          >
          <label
            >{{ t('housingLayer')
            }}<select :value="data.room.layer" :disabled="busy" @change="changeSelect($event, 'layer')">
              <option v-for="i in 8" :key="i" :value="i - 1">
                {{ t('housingLayers').split('|')[i - 1] }}
              </option>
            </select></label
          >
        </div>
        <p class="field-help">{{ t('housingRoomHint') }}</p>
        <div class="housing-room-layout">
          <div class="housing-grid-scroll">
            <div class="housing-grid" :aria-label="t('housingRoomEditor')" role="group">
              <button
                v-for="tile in data.room.tiles"
                :key="`${tile.x},${tile.y}`"
                class="housing-tile"
                :class="{
                  active: selected.x === tile.x && selected.y === tile.y,
                  empty: tile.empty,
                  extension: tile.extension,
                }"
                :aria-label="`${tile.x},${tile.y}: ${tile.empty ? t('empty') : tile.extension ? t('housingExtension') : tile.name}`"
                :aria-pressed="selected.x === tile.x && selected.y === tile.y"
                :title="`${tile.x},${tile.y} · ${tile.name}`"
                :disabled="busy"
                @click="selectTile(tile)"
              >
                <img v-if="tile.image" :src="tile.image" alt="" /><span v-else>{{
                  tile.empty ? '·' : tile.extension ? '↖' : tile.item.itemId
                }}</span>
              </button>
            </div>
          </div>
          <form ref="tileForm" class="housing-tile-editor" @submit.prevent="applyTile()">
            <h3>{{ selectedTile?.name || t('empty') }}</h3>
            <p>({{ selected.x }}, {{ selected.y }})</p>
            <button type="button" class="button" :disabled="busy" @click="pickItem">
              {{ t('replace') }}
            </button>
            <label v-for="(key, i) in tileKeys" :key="key"
              >{{ housingLabel(tileNames[i]!)
              }}<input
                v-model.number="item[key]"
                type="number"
                :min="key === 'freeParam' ? -2147483648 : 0"
                :max="key === 'itemId' ? 65535 : key === 'freeParam' ? 2147483647 : 255"
                step="1"
                required
                :disabled="busy"
            /></label>
            <label class="housing-checkbox"
              ><input v-model="autoExtensions" type="checkbox" :disabled="busy" />{{
                t('housingAutoExtensions')
              }}</label
            >
            <label class="housing-checkbox"
              ><input v-model="noOverwrite" type="checkbox" :disabled="busy" />{{
                t('housingNoOverwrite')
              }}</label
            >
            <div class="housing-actions">
              <button class="button primary" :disabled="busy || !pendingTile">{{ t('apply') }}</button
              ><button
                type="button"
                class="button"
                :disabled="busy || selectedTile?.empty"
                @click="applyTile(true)"
              >
                {{ t('delete') }}
              </button>
            </div>
          </form>
        </div>
      </section>
    </template>
  </section>
</template>

<style scoped>
.housing-tabs,
.housing-room-controls,
.housing-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-block: 1rem;
}
.housing-settings {
  margin-block: 1.5rem;
  padding: 1.25rem;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: var(--surface);
}
.housing-settings h2 {
  margin-bottom: 1.2rem;
}
.housing-advanced,
.housing-flags {
  margin-top: 1.5rem;
}
.housing-advanced summary,
.housing-flags summary {
  cursor: pointer;
  margin-bottom: 1rem;
}
.housing-room-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(220px, 0.5fr);
  gap: 1.5rem;
  align-items: start;
}
.housing-grid-scroll {
  overflow: auto;
  max-height: 660px;
  border: 1px solid var(--line);
}
.housing-grid {
  display: grid;
  grid-template-columns: repeat(20, minmax(28px, 1fr));
  min-width: 560px;
}
.housing-tile {
  aspect-ratio: 1;
  padding: 1px;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--line);
  background: var(--slot);
  font-size: 9px;
}
.housing-tile.active {
  outline: 2px solid var(--green);
  outline-offset: -2px;
  background: var(--green-soft);
}
.housing-tile.extension {
  background: var(--hover);
}
.housing-tile img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  image-rendering: pixelated;
}
.housing-tile-editor {
  display: grid;
  gap: 0.75rem;
  min-width: 0;
}
.housing-tile-editor h3 {
  overflow-wrap: anywhere;
}
label {
  display: grid;
  gap: 0.5rem;
  min-width: 0;
}
.housing-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.housing-checkbox input {
  width: auto;
  flex: 0 0 auto;
}
input,
select {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  padding: 0.65rem;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
  background: var(--surface);
  color: inherit;
  font: inherit;
}
@media (max-width: 1000px) {
  .housing-room-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
