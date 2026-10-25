<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue';
import type { MapData, MapTarget, MapBrush } from './bridge';
import { t } from './i18n';
const props = defineProps<{
  data: MapData; busy: boolean; values: Record<string, number | boolean | string>;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>;
  applyPending: () => Promise<boolean>;
  navigate: (target: Partial<MapTarget>) => Promise<void>;
  validate: () => boolean;
}>();
const brush = defineModel<MapBrush>('brush', { required: true });
const emit = defineEmits<{ preset: [values: Record<string, number>] }>();
const family = ref('land'), corner = ref('round');
function preset(index: number) {
  const water = family.value === 'water';
  const isCorner = [0, 2, 6, 8].includes(index);
  const name = index === 4 ? (water ? 'River8A' : 'Base') : (water ? 'River' : 'Cliff') + (isCorner ? corner.value === 'inside' ? '7A' : corner.value === 'angle' ? '3C' : '3B' : '5B');
  const model = props.data.fields.find(f => f.key === 'Map.UnitModel')?.options?.find(o => o.name === name);
  if (!model) return;
  const rotation = (isCorner && corner.value === 'inside' ? [3, 0, 2, 0, 0, 0, 0, 0, 1] : [0, 3, 3, 0, 0, 2, 1, 1, 2])[index]!;
  emit('preset', { 'Map.UnitModel': model.id, 'Map.LandMakingAngle': rotation, 'Map.Variation': 0, 'Map.UnitModelRoad': 0, 'Map.VariationRoad': 0, 'Map.LandMakingAngleRoad': 0 });
}
const whole = ref(false), interiorOnly = ref(true);
const resource = ref('terrain'), fileWhole = ref(true);
const resources = ['items', 'terrain', 'buildings', 'acres', 'flags', 'designs'];
const itemOperations = ['removeWeeds', 'removeTrees', 'fillHoles', 'removePlants', 'removeFences', 'removeObjects', 'removeAll', 'removePlaced', 'removeShells', 'removeBranches', 'removeFlowers', 'removeBushes', 'waterFlowers', 'waterAllFlowers'];
const terrainOperations = ['setTerrain', 'setRoad', 'zeroElevation'];
const operations = computed(() => props.data.target.mode === 'items' ? itemOperations : props.data.target.mode === 'terrain' ? terrainOperations : props.data.target.mode === 'markers' ? ['clearDesigns'] : []);
const operation = ref('setTerrain');
watch(() => props.data.target.mode, mode => {
  resource.value = mode === 'markers' ? 'designs' : mode;
  operation.value = operations.value[0] ?? '';
}, { immediate: true });
watch(resource, value => { if (!['items', 'terrain'].includes(value)) fileWhole.value = true; });
const dialog = ref<HTMLDialogElement | null>(null);
const confirmation = shallowRef<{ command: string; payload: Record<string, unknown>; label: string; scope: string } | null>(null);
function operationName(value: string) {
  const index = itemOperations.indexOf(value);
  if (index >= 0) return t('mapItemOperations').split('|')[index]!;
  const terrain = terrainOperations.indexOf(value);
  return terrain >= 0 ? t('mapTerrainOperations').split('|')[terrain]! : t('mapClearDesigns');
}
async function ask(command: string, payload: Record<string, unknown>, label: string, all: boolean) {
  if (props.busy) return;
  confirmation.value = { command, payload, label, scope: `${t('mapScopeNames').split('|')[all ? 1 : 0]} · ${props.data.acres.find(a => a.id === props.data.target.acre)?.name} · ${t('mapLayer')} ${props.data.target.layer}${props.data.target.mode === 'terrain' && interiorOnly.value && command === 'batchMap' ? ' · ' + t('mapInteriorOnly') : ''}` };
  await nextTick();
  dialog.value?.showModal();
}
function cancel() { dialog.value?.close(); confirmation.value = null; }
async function confirm() {
  const pending = confirmation.value;
  cancel();
  if (pending) await props.execute(pending.command, pending.payload);
}
async function batch() {
  if (!props.validate()) return;
  const all = props.data.target.mode === 'markers' || whole.value;
  await ask('batchMap', { target: { ...props.data.target }, operation: operation.value, whole: all,
    interiorOnly: interiorOnly.value, values: props.data.target.mode === 'terrain' ? { ...props.values } : {} }, operationName(operation.value), all);
}
async function paint() {
  if (!props.validate() || !Number.isInteger(brush.value.radius) || brush.value.radius < 1 || brush.value.radius > 8) return;
  await props.execute('paintMap', { target: { ...props.data.target }, radius: brush.value.radius,
    randomVariation: brush.value.randomVariation, values: { ...props.values } });
}
async function file(importing: boolean) {
  if (!(await props.applyPending())) return;
  const payload = { target: { ...props.data.target }, resource: resource.value, whole: fileWhole.value };
  if (importing) await ask('mapImport', payload, `${t('mapImport')} · ${t('mapResources').split('|')[resources.indexOf(resource.value)]}`, fileWhole.value);
  else await props.execute('mapExport', payload);
}
async function png() { if (await props.applyPending()) await props.execute('mapPng', { target: { ...props.data.target } }); }
async function active(value: boolean) {
  if (await props.applyPending()) await props.execute('setMapActive', { target: { ...props.data.target }, active: value });
}
</script>
<template>
  <section class="map-tools">
    <label v-if="data.active !== null && data.active !== undefined" class="map-active">
      <input type="checkbox" :checked="data.active" :disabled="busy" @change="active(($event.target as HTMLInputElement).checked)" />{{ t('mapActive') }}
    </label>
    <details v-if="data.target.mode === 'terrain' && data.fields.length" class="map-brush" :open="brush.enabled">
      <summary>{{ t('mapBrush') }}</summary>
      <p>{{ t('mapBrushHint') }}</p>
      <label><input v-model="brush.enabled" type="checkbox" :disabled="busy" />{{ t('mapDragBrush') }}</label>
      <p v-if="brush.enabled">{{ t('mapDragHint') }}</p>
      <div class="tool-row brush-presets">
        <label>{{ t('mapPresetFamily') }}<select v-model="family" :disabled="busy"><option value="land">{{ t('mapPresetFamilies').split('|')[0] }}</option><option value="water">{{ t('mapPresetFamilies').split('|')[1] }}</option></select></label>
        <label>{{ t('mapPresetCorner') }}<select v-model="corner" :disabled="busy"><option v-for="(kind, i) in ['round', 'angle', 'inside']" :key="kind" :value="kind">{{ t('mapPresetCorners').split('|')[i] }}</option></select></label>
        <div class="preset-grid"><button v-for="(arrow, i) in ['↖', '↑', '↗', '←', '●', '→', '↙', '↓', '↘']" :key="i" type="button" :aria-label="`${t('mapPresetTile')} ${i + 1}`" :disabled="busy" @click="preset(i)">{{ arrow }}</button></div>
      </div>
      <div class="tool-row">
        <label>{{ t('mapBrushSize') }}<input v-model.number="brush.radius" type="number" min="1" max="8" step="1" :disabled="busy" /></label>
        <label><input v-model="brush.randomVariation" type="checkbox" :disabled="busy" />{{ t('mapRandomVariation') }}</label>
        <button :disabled="busy" @click="paint">{{ t('mapPaint') }}</button>
      </div>
    </details>
    <details v-if="operations.length" class="map-batch">
      <summary>{{ t('mapBatch') }}</summary>
      <div class="tool-row">
        <label>{{ t('mapOperation') }}<select v-model="operation" :disabled="busy"><option v-for="op in operations" :key="op" :value="op">{{ operationName(op) }}</option></select></label>
        <label v-if="data.target.mode !== 'markers'">{{ t('mapScope') }}<select v-model="whole" :disabled="busy"><option :value="false">{{ t('mapScopeNames').split('|')[0] }}</option><option :value="true">{{ t('mapScopeNames').split('|')[1] }}</option></select></label>
        <label v-if="data.target.mode === 'terrain'"><input v-model="interiorOnly" type="checkbox" :disabled="busy" />{{ t('mapInteriorOnly') }}</label>
        <button :disabled="busy || (data.target.mode === 'terrain' && !data.fields.length)" @click="batch">{{ t('mapRunBatch') }}</button>
      </div>
      <p v-if="data.target.mode === 'items'">{{ t('mapFootprintHint') }}</p>
    </details>
    <details class="map-files">
      <summary>{{ t('mapFiles') }}</summary>
      <p>{{ t('mapFileHint') }}</p>
      <div class="tool-row">
        <label>{{ t('mapResource') }}<select v-model="resource" :disabled="busy"><option v-for="(res, i) in resources" :key="res" :value="res">{{ t('mapResources').split('|')[i] }}</option></select></label>
        <label v-if="['items', 'flags'].includes(resource)">{{ t('mapLayer') }}<select :value="data.target.layer" :disabled="busy" @change="navigate({ layer: Number(($event.target as HTMLSelectElement).value) })"><option :value="0">0</option><option :value="1">1</option></select></label>
        <label v-if="['items', 'terrain'].includes(resource)">{{ t('mapScope') }}<select v-model="fileWhole" :disabled="busy"><option :value="false">{{ t('mapScopeNames').split('|')[0] }}</option><option :value="true">{{ t('mapScopeNames').split('|')[1] }}</option></select></label>
        <button :disabled="busy" @click="file(true)">{{ t('mapImport') }}</button>
        <button :disabled="busy" @click="file(false)">{{ t('mapExport') }}</button>
        <button :disabled="busy" @click="png">{{ t('mapPng') }}</button>
      </div>
    </details>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="t('mapConfirmTitle')" @cancel.prevent="cancel">
      <h2>{{ t('mapConfirmTitle') }}</h2><p>{{ confirmation.label }}</p><p>{{ confirmation.scope }}</p><p>{{ t('mapBatchHint') }}</p>
      <div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('apply') }}</button></div>
    </dialog>
  </section>
</template>
<style scoped>
.map-tools { display: grid; gap: 1rem; min-width: 0; }
.tool-row { display: flex; flex-wrap: wrap; gap: .75rem; align-items: center; margin-block: 1rem; }
label { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; min-width: 0; }
button, select, input[type=number] { max-width: 100%; box-sizing: border-box; padding: .5rem; border: 1px solid var(--line-strong); border-radius: .5rem; color: inherit; background: var(--surface); }
input[type=number] { width: 5rem; }
.preset-grid { display: grid; grid-template-columns: repeat(3, 2.5rem); gap: .25rem; }
summary { cursor: pointer; font-weight: 600; }
p { color: var(--muted); line-height: 1.5; }
</style>
