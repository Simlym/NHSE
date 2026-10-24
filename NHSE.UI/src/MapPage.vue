<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { t } from './i18n';
import type { MapData, MapTarget, ItemView, MapBrush, GridClipboardInfo, GridTarget } from './bridge';
import HousingFields from './HousingFields.vue';
import MapTools from './MapTools.vue';
import MapAdvanced from './MapAdvanced.vue';
import GridToolbar from './GridToolbar.vue';
import { useGridSelection, pointKey } from './gridSelection';
const props = defineProps<{
  data: MapData | null; busy: boolean; clipboard: GridClipboardInfo | null;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>;
  navigate: (target: Partial<MapTarget>) => Promise<void>; pickItem: () => void;
}>();
const modes: MapTarget['mode'][] = ['terrain', 'items', 'buildings', 'acres', 'markers'];
const form = ref<HTMLFormElement | null>(null);
const autoExtensions = ref(true), noOverwrite = ref(true), snapFieldItems = ref(true);
const stored = () => Object.fromEntries((props.data?.fields ?? []).map(f => [f.key, f.value]));
const fields = ref(stored());
const brush = ref<MapBrush>({ enabled: false, radius: 1, randomVariation: false });
const strokePoints = ref<{ x: number; y: number }[]>([]);
const strokeMeta = shallowRef<{ target: MapTarget; radius: number; randomVariation: boolean; values: Record<string, number | boolean | string> } | null>(null);
let pointerGrid: HTMLElement | null = null, pointerId = -1;
let lastPoint: { x: number; y: number } | null = null;
const strokeKeys = computed(() => new Set(strokePoints.value.map(p => `${p.x},${p.y}`)));
const gridSelection = useGridSelection({
  cells: () => props.data?.cells.filter(c => c.valid).map(c => ({ x: c.x, y: c.y })) ?? [],
  scope: () => JSON.stringify([props.data?.target.mode, props.data?.target.acre, props.data?.target.layer]),
  active: () => ({ x: props.data?.target.x ?? 0, y: props.data?.target.y ?? 0 }),
  busy: () => props.busy,
  activate: point => props.navigate(point), action: gridAction,
});
async function gridAction(name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') {
  if (!props.data || props.busy || !gridSelection.selected.value.length || strokeMeta.value) return;
  const target: GridTarget = { kind: props.data.target.mode === 'terrain' ? 'terrain' : 'mapItems', map: { ...props.data.target } };
  const selection = gridSelection.selected.value.map(p => ({ ...p }));
  const anchor = selection.length > 1 ? { x: Math.min(...selection.map(p => p.x)), y: Math.min(...selection.map(p => p.y)) } : { x: props.data.target.x, y: props.data.target.y };
  const success = await props.execute(name === 'delete' || name === 'rotate' ? 'editGrid' : name, { target, selection, anchor, noOverwrite: noOverwrite.value, operation: name });
  if (success && name !== 'copyGrid' && !brush.value.enabled) fields.value = stored();
}
function gridPointerDown(event: PointerEvent) {
  if (brush.value.enabled && !event.ctrlKey && !event.metaKey && !event.shiftKey) startStroke(event);
  else gridSelection.down(event);
}
function gridPointerMove(event: PointerEvent) { if (gridSelection.dragging.value) gridSelection.move(event); else moveStroke(event); }
function gridPointerUp(event: PointerEvent) { if (gridSelection.dragging.value) void gridSelection.up(event); else void endStroke(event); }
function cancelGridPointer() { gridSelection.cancel(); cancelStroke(); }
function gridKeydown(event: KeyboardEvent) {
  if (strokeMeta.value) {
    strokeKeyboard(event);
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); cancelStroke(); }
    return;
  }
  gridSelection.keydown(event);
}
function gridPoint(event: PointerEvent) {
  if (!pointerGrid) return null;
  const box = pointerGrid.getBoundingClientRect();
  const x = Math.floor((event.clientX - box.left) / box.width * 16), y = Math.floor((event.clientY - box.top) / box.height * 16);
  return x >= 0 && x < 16 && y >= 0 && y < 16 && props.data?.cells.some(c => c.x === x && c.y === y && c.valid) ? { x, y } : null;
}
function collect(event: PointerEvent) {
  const point = gridPoint(event);
  if (!point) { lastPoint = null; return; }
  let x = lastPoint?.x ?? point.x, y = lastPoint?.y ?? point.y;
  const dx = Math.abs(point.x - x), dy = -Math.abs(point.y - y), sx = x < point.x ? 1 : -1, sy = y < point.y ? 1 : -1;
  let error = dx + dy;
  while (true) {
    if (!strokePoints.value.some(p => p.x === x && p.y === y)) strokePoints.value.push({ x, y });
    if (x === point.x && y === point.y) break;
    const twice = 2 * error;
    if (twice >= dy) { error += dy; x += sx; }
    if (twice <= dx) { error += dx; y += sy; }
  }
  lastPoint = point;
}
function startStroke(event: PointerEvent) {
  if (!brush.value.enabled || props.data?.target.mode !== 'terrain' || props.busy || event.button !== 0 || strokeMeta.value || !Number.isInteger(brush.value.radius) || brush.value.radius < 1 || brush.value.radius > 8 || !form.value?.reportValidity()) return;
  event.preventDefault();
  pointerGrid = event.currentTarget as HTMLElement; pointerId = event.pointerId;
  if (!gridPoint(event)) { pointerGrid = null; return; }
  strokeMeta.value = { target: { ...props.data.target }, radius: brush.value.radius, randomVariation: brush.value.randomVariation, values: { ...fields.value } };
  strokePoints.value = []; lastPoint = null;
  pointerGrid.focus({ preventScroll: true }); pointerGrid.setPointerCapture(pointerId); collect(event);
}
function strokeKeyboard(event: KeyboardEvent) {
  if (strokeMeta.value && (event.ctrlKey || event.metaKey) && ['z', 'y'].includes(event.key.toLowerCase())) {
    event.preventDefault(); event.stopPropagation(); cancelStroke();
  }
}
function moveStroke(event: PointerEvent) { if (strokeMeta.value && event.pointerId === pointerId) collect(event); }
function cancelStroke() {
  const grid = pointerGrid, id = pointerId;
  strokeMeta.value = null; strokePoints.value = []; lastPoint = null; pointerGrid = null; pointerId = -1;
  if (grid?.hasPointerCapture(id)) grid.releasePointerCapture(id);
}
async function endStroke(event: PointerEvent) {
  if (!strokeMeta.value || event.pointerId !== pointerId) return;
  collect(event);
  const payload = { ...strokeMeta.value, points: strokePoints.value.map(p => ({ ...p })) };
  cancelStroke();
  if (payload.points.length) await props.execute('paintMapStroke', payload);
}
function preset(values: Record<string, number>) { fields.value = { ...fields.value, ...values }; }
watch(() => brush.value.enabled, enabled => { if (!enabled) { cancelStroke(); fields.value = stored(); } });
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(stored()));
watch(() => JSON.stringify([props.data?.target, stored()]), () => {
  if (props.data?.target.mode !== 'terrain') brush.value.enabled = false;
  if (!brush.value.enabled) fields.value = stored();
});
async function edit(action = 'apply') {
  if (!props.data || !form.value?.reportValidity()) return false;
  return await props.execute('editMap', { target: { ...props.data.target }, values: { ...fields.value }, action,
    autoExtensions: autoExtensions.value, noOverwrite: noOverwrite.value, snapFieldItems: snapFieldItems.value });
}
async function applyPending() { if (strokeMeta.value || gridSelection.dragging.value) return false; return (brush.value.enabled && props.data?.target.mode === 'terrain') || !pending.value || await edit(); }
function chooseItem(item: ItemView) {
  fields.value = { 'Map.ItemId': item.itemId, 'Map.SystemParam': 0, 'Map.AdditionalParam': 0, 'Map.FreeParam': 0 };
}
function overviewClick(event: MouseEvent) {
  const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
  const x = Math.max(0, Math.min(8, Math.floor((event.clientX - box.left) / box.width * 9)));
  const y = Math.max(0, Math.min(7, Math.floor((event.clientY - box.top) / box.height * 8)));
  void props.navigate({ acre: y * 9 + x });
}
defineExpose({ applyPending, chooseItem });
</script>
<template>
  <section v-if="data" class="map-page">
    <div class="map-tabs" role="group" :aria-label="t('mapEditor')">
      <button v-for="(mode, i) in modes" :key="mode" :disabled="busy" :aria-pressed="data.target.mode === mode"
        @click="navigate({ mode, x: 0, y: 0 })">{{ t('mapModes').split('|')[i] }}</button>
      <button :disabled="busy" @click="execute('nativeMap')">{{ t('mapAdvanced') }}</button>
    </div>
    <div class="map-controls">
      <label>{{ t('mapAcre') }} <select :value="data.target.acre" :disabled="busy" @change="navigate({ acre: Number(($event.target as HTMLSelectElement).value) })">
        <option v-for="a in data.acres" :key="a.id" :value="a.id">{{ a.name }}</option>
      </select></label>
      <label v-if="data.target.mode === 'items'">{{ t('mapLayer') }} <select :value="data.target.layer" :disabled="busy" @change="navigate({ layer: Number(($event.target as HTMLSelectElement).value) })"><option :value="0">0</option><option :value="1">1</option></select></label>
      <label v-if="data.target.mode === 'buildings'">{{ t('mapModes').split('|')[2] }} <select :value="data.target.building" :disabled="busy" @change="navigate({ building: Number(($event.target as HTMLSelectElement).value) })"><option v-for="b in data.buildings" :key="b.id" :value="b.id">{{ b.name }}</option></select></label>
      <label v-if="data.target.mode === 'markers'">{{ t('mapSlot') }} <input type="number" min="0" :max="data.markerCount - 1" :value="data.target.marker" :disabled="busy" @change="($event.target as HTMLInputElement).reportValidity() && navigate({ marker: Number(($event.target as HTMLInputElement).value) })" /></label>
    </div>
    <p v-if="data.target.mode === 'markers'">{{ t('mapSlotHint') }} (0–{{ data.markerCount - 1 }})</p>
    <div class="map-images">
      <button class="map-overview" :disabled="busy" :aria-label="t('mapAcre')" @click="overviewClick">
        <img v-if="data.overview" :src="data.overview" alt="" />
        <span class="acre-outline" :style="{ left: `${data.target.acre % 9 / 9 * 100}%`, top: `${Math.floor(data.target.acre / 9) / 8 * 100}%` }" />
      </button>
      <div class="map-viewport">
        <img v-if="data.viewport" :src="data.viewport" alt="" />
        <img v-if="data.itemImage && data.target.mode === 'items'" :src="data.itemImage" alt="" />
        <div v-if="['terrain', 'items'].includes(data.target.mode)" class="map-grid" tabindex="0" role="group" :aria-label="t('gridArea')" :class="{ 'brush-enabled': brush.enabled && data.target.mode === 'terrain' }"
          @pointerdown="gridPointerDown" @pointermove="gridPointerMove" @pointerup="gridPointerUp" @pointercancel="cancelGridPointer" @lostpointercapture="cancelGridPointer" @keydown="gridKeydown" :data-columns="data.gridSize" :data-rows="data.gridSize" :style="{ gridTemplateColumns: `repeat(${data.gridSize}, 1fr)` }">
          <button tabindex="-1" :aria-pressed="gridSelection.keys.value.has(pointKey(cell))" v-for="cell in data.cells" :key="`${cell.x},${cell.y}`" :disabled="busy || !cell.valid"
            :aria-label="`${cell.x}, ${cell.y}${cell.name ? ' · ' + cell.name : ''}`" :title="`${cell.relativeX}, ${cell.relativeY} ${cell.name}`"
            :class="{ selected: cell.x === data.target.x && cell.y === data.target.y, occupied: !!cell.name, extension: cell.extension, 'stroke-preview': strokeKeys.has(`${cell.x},${cell.y}`), 'multi-selected': gridSelection.keys.value.has(pointKey(cell)) }"
            @click="!brush.enabled && gridSelection.click($event, cell)" />
        </div>
      </div>
    </div>
    <GridToolbar v-if="['terrain', 'items'].includes(data.target.mode)" :count="gridSelection.selected.value.length" :clipboard="clipboard" :kind="data.target.mode === 'terrain' ? 'terrain' : 'items'" :busy="busy || !!strokeMeta || gridSelection.dragging.value" :action="gridAction" :clear="gridSelection.clear" />
    <p v-if="['terrain', 'items'].includes(data.target.mode)">X {{ data.target.x }} · Y {{ data.target.y }}</p>
    <form v-if="data.fields.length" ref="form" @submit.prevent="edit()">
      <div v-if="data.target.mode === 'items'" class="map-controls">
        <button type="button" :disabled="busy" @click="pickItem">{{ t('mapPickItem') }}</button>
        <select :aria-label="t('mapFieldObject')" :value="data.fieldObjects.some(o => o.id === fields['Map.ItemId']) ? fields['Map.ItemId'] : ''" :disabled="busy" @change="chooseItem({ itemId: Number(($event.target as HTMLSelectElement).value) } as ItemView)">
          <option value="" disabled>{{ t('mapFieldObject') }}</option><option v-for="o in data.fieldObjects" :key="o.id" :value="o.id">{{ o.name }}</option>
        </select>
        <label><input v-model="autoExtensions" type="checkbox" :disabled="busy" />{{ t('mapExtensions') }}</label>
        <label><input v-model="noOverwrite" type="checkbox" :disabled="busy" />{{ t('mapNoOverwrite') }}</label>
        <label><input v-model="snapFieldItems" type="checkbox" :disabled="busy" />{{ t('mapSnap') }}</label>
      </div>
      <HousingFields v-model="fields" :fields="data.fields" :busy="busy" />
      <div class="map-tabs map-actions">
        <button type="submit" :disabled="busy || !pending">{{ t('apply') }}</button>
        <button v-if="['terrain', 'items'].includes(data.target.mode)" type="button" :disabled="busy" @click="edit('delete')">{{ t('mapClear') }}</button>
        <button v-if="data.target.mode === 'terrain'" type="button" :disabled="busy" @click="edit('rotate')">{{ t('mapRotate') }}</button>
      </div>
    </form>
    <p v-else>{{ t('mapOutside') }}</p>
    <MapAdvanced @applied="fields = stored()" :data="data" :values="fields" :busy="busy" :execute="execute" :validate="() => !form || form.reportValidity()" />
    <MapTools v-model:brush="brush" @preset="preset" :data="data" :busy="busy" :values="fields" :execute="execute" :apply-pending="applyPending" :navigate="navigate" :validate="() => !form || form.reportValidity()" />
  </section>
</template>
<style scoped>
.map-page { display: grid; gap: 1rem; min-width: 0; }
.map-tabs, .map-controls { display: flex; flex-wrap: wrap; gap: .6rem; align-items: center; }
.map-controls label { display: flex; gap: .5rem; align-items: center; }
button, select, input[type=number] { padding: .5rem; border: 1px solid var(--line-strong); border-radius: .5rem; color: inherit; background: var(--surface); max-width: 100%; }
button[aria-pressed=true] { outline: 2px solid var(--accent); }
.map-images { display: grid; grid-template-columns: minmax(0, 1.125fr) minmax(0, 1fr); gap: 1rem; }
.map-overview { position: relative; padding: 0; aspect-ratio: 9/8; overflow: hidden; border: 0; border-radius: 0; }
.acre-outline { position: absolute; width: calc(100% / 9); height: 12.5%; border: 2px solid #ffcf40; box-sizing: border-box; pointer-events: none; }
.map-viewport { position: relative; aspect-ratio: 1; }
.map-images img { display: block; width: 100%; height: 100%; image-rendering: pixelated; }
.map-viewport img, .map-grid { position: absolute; inset: 0; }
.map-grid { display: grid; touch-action: none; user-select: none; }
.map-grid button.multi-selected { box-shadow: inset 0 0 0 1px #2f8fe8; background: #2f8fe830; }
.map-grid.brush-enabled { touch-action: none; cursor: crosshair; }
.map-grid button.stroke-preview { background: #36acfa80; }
.map-grid button { padding: 0; min-width: 0; border: 1px solid #ffffff18; background: transparent; border-radius: 0; }
.map-grid button:disabled { background: #0005; }
.map-grid button.selected { outline: 2px solid #ffcf40; z-index: 1; }
.map-grid button.occupied { background: #ffda1730; }
.map-grid button.extension { background: #ffda1718; }
.map-actions { margin-top: 1rem; }
@media(max-width: 650px) { .map-images { grid-template-columns: minmax(0, 1fr); } }
</style>
