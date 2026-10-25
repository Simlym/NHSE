<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue';
import type { MapData, MapEditRule } from './bridge';
import { t } from './i18n';
import { housingLabel } from './housingLabels';
const props = defineProps<{
  data: MapData; values: Record<string, number | boolean | string>; busy: boolean;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>; validate: () => boolean;
}>();
const emit = defineEmits<{ applied: [] }>();
const batchText = ref('');
const whole = ref(false), source = ref('current'), arrangement = ref('square'), count = ref(8), recipeMin = ref(0), recipeMax = ref(65535);
watch(source, value => { count.value = value === 'current' ? 8 : 1; });
const form = ref<HTMLFormElement | null>(null), dialog = ref<HTMLDialogElement | null>(null);
const filters = ref<MapEditRule[]>([]), changes = ref<MapEditRule[]>([{ key: 'FreeParam', operator: 'set', value: '0' }]);
const parameters = computed(() => props.data.parameters ?? []);
const editable = computed(() => parameters.value.filter(p => p.writable));
const paramKeys = ['RawValue', 'Rotation', 'IsBuried', 'IsDropped', 'Count', 'UseCount', 'BodyType', 'PatternSource', 'PatternChoice', 'DaysWatered', 'IsWatered', 'IsWateredGold', 'Genes', 'WrappingType', 'WrappingPaper', 'WrappingShowItem', 'Wrapping80', 'IsWrapped', 'DisplayItemId', 'IsNone', 'IsExtension', 'IsRoot', 'IsFieldItem', 'Size'];
function parameterName(key: string) { const index = paramKeys.indexOf(key); return index < 0 ? housingLabel(key) : t('mapParamNames').split('|')[index] ?? key; }
function field(key: string) { return parameters.value.find(p => p.key === key); }
function changeKey(rule: MapEditRule) { rule.value = field(rule.key)?.type === 'boolean' ? 'false' : '0'; }
function current() { return { itemId: Number(props.values['Map.ItemId']), systemParam: Number(props.values['Map.SystemParam']), additionalParam: Number(props.values['Map.AdditionalParam']), freeParam: Number(props.values['Map.FreeParam']) }; }
const confirmation = shallowRef<{ command: string; payload: Record<string, unknown>; label: string; detail: string } | null>(null);
async function ask(command: string, payload: Record<string, unknown>, label: string, detail: string) {
  if (props.busy || !props.validate()) return;
  const section = command === 'spawnMap' ? '.map-spawn' : command === 'editMapParameters' ? '.map-parameters' : null;
  if (section && !Array.from(form.value?.querySelectorAll<HTMLInputElement | HTMLSelectElement>(`${section} input, ${section} select`) ?? []).every(input => input.reportValidity())) return;
  confirmation.value = { command, payload, label, detail };
  await nextTick(); dialog.value?.showModal();
}
function cancel() { dialog.value?.close(); confirmation.value = null; }
async function confirm() { const operation = confirmation.value; cancel(); if (operation && await props.execute(operation.command, operation.payload)) emit('applied'); }
function scope() { return `${t('mapScopeNames').split('|')[whole.value ? 1 : 0]} · ${t('mapLayer')} ${props.data.target.layer}`; }
async function spawn() {
  await ask('spawnMap', { target: { ...props.data.target }, options: { source: source.value, arrangement: arrangement.value, count: count.value, recipeMin: recipeMin.value, recipeMax: recipeMax.value }, current: current() }, t('mapSpawn'), `${t('mapSpawnSources').split('|')[['current', 'recipes', 'alphabetical', 'nhi'].indexOf(source.value)]} × ${count.value} · ${t('mapArrangeNames').split('|')[['square', 'horizontal', 'vertical'].indexOf(arrangement.value)]} · X ${props.data.target.x} Y ${props.data.target.y} · ${t('mapLayer')} ${props.data.target.layer}`);
}
async function replace(remove: boolean) { await ask('replaceMapMatches', { target: { ...props.data.target }, whole: whole.value, current: current(), remove }, remove ? t('mapRemoveMatches') : t('mapReplaceMatches'), scope()); }
async function batch() { await ask('editMapParameters', { target: { ...props.data.target }, whole: whole.value, filters: filters.value.map(r => ({ ...r })), changes: changes.value.map(r => ({ ...r })) }, t('mapParameters'), scope()); }
</script>
<template>
  <section v-if="data.target.mode === 'items' && data.fields.length" class="map-advanced">
    <form ref="form" @submit.prevent>
      <details class="map-spawn"><summary>{{ t('mapSpawn') }}</summary>
        <p>{{ t('mapSpawnHint') }}</p>
        <div class="advanced-row">
          <label>{{ t('mapSpawnSource') }}<select v-model="source" :disabled="busy"><option v-for="(s, i) in ['current', 'recipes', 'alphabetical', 'nhi']" :key="s" :value="s">{{ t('mapSpawnSources').split('|')[i] }}</option></select></label>
          <label>{{ t('mapArrangement') }}<select v-model="arrangement" :disabled="busy"><option v-for="(a, i) in ['square', 'horizontal', 'vertical']" :key="a" :value="a">{{ t('mapArrangeNames').split('|')[i] }}</option></select></label>
          <label>{{ t('mapRepeatCount') }}<input v-model.number="count" type="number" min="1" max="1000" step="1" required :disabled="busy" /></label>
          <template v-if="['recipes', 'alphabetical'].includes(source)">
            <label>{{ t('mapRecipeStart') }}<input v-model.number="recipeMin" type="number" min="0" max="65535" step="1" required :disabled="busy" /></label>
            <label>{{ t('mapRecipeEnd') }}<input v-model.number="recipeMax" type="number" :min="recipeMin" max="65535" step="1" required :disabled="busy" /></label>
          </template>
          <button type="button" :disabled="busy" @click="spawn">{{ t('mapReviewSpawn') }}</button>
        </div>
      </details>
      <details class="map-parameters"><summary>{{ t('mapParameters') }}</summary>
        <p>{{ t('mapParameterHint') }}</p>
        <div class="advanced-row"><label>{{ t('mapScope') }}<select v-model="whole" :disabled="busy"><option :value="false">{{ t('mapScopeNames').split('|')[0] }}</option><option :value="true">{{ t('mapScopeNames').split('|')[1] }}</option></select></label>
          <button type="button" :disabled="busy" @click="replace(false)">{{ t('mapReplaceMatches') }}</button><button type="button" :disabled="busy" @click="replace(true)">{{ t('mapRemoveMatches') }}</button>
        </div>
        <h3>{{ t('mapFilters') }}</h3>
        <div v-for="(rule, i) in filters" :key="i" class="advanced-row rule-row">
          <select v-model="rule.key" :aria-label="t('mapProperty')" :disabled="busy" @change="changeKey(rule)"><option v-for="p in parameters" :key="p.key" :value="p.key">{{ parameterName(p.key) }}</option></select>
          <select v-model="rule.operator" :aria-label="t('mapCompare')" :disabled="busy"><option v-for="(op, j) in ['eq', 'ne', 'gt', 'ge', 'lt', 'le']" :key="op" :value="op">{{ ['=', '≠', '>', '≥', '<', '≤'][j] }}</option></select>
          <select v-if="field(rule.key)?.type === 'boolean'" v-model="rule.value" :aria-label="t('mapValue')" :disabled="busy"><option value="false">{{ t('mapBooleanNames').split('|')[0] }}</option><option value="true">{{ t('mapBooleanNames').split('|')[1] }}</option></select>
          <input v-else v-model="rule.value" :aria-label="t('mapValue')" :title="`${field(rule.key)?.min} … ${field(rule.key)?.max}`" type="text" inputmode="numeric" pattern="-?[0-9]+" required :disabled="busy" />
          <button type="button" :disabled="busy" :aria-label="t('mapRemoveRule')" @click="filters.splice(i, 1)">×</button>
        </div>
        <button type="button" :disabled="busy || filters.length >= 32" @click="filters.push({ key: 'ItemId', operator: 'eq', value: String(values['Map.ItemId']) })">{{ t('mapAddFilter') }}</button>
        <h3>{{ t('mapChanges') }}</h3>
        <div v-for="(rule, i) in changes" :key="i" class="advanced-row change-row">
          <select v-model="rule.key" :aria-label="t('mapProperty')" :disabled="busy" @change="changeKey(rule)"><option v-for="p in editable" :key="p.key" :value="p.key">{{ parameterName(p.key) }}</option></select>
          <select v-if="field(rule.key)?.type === 'boolean'" v-model="rule.value" :aria-label="t('mapValue')" :disabled="busy"><option value="false">{{ t('mapBooleanNames').split('|')[0] }}</option><option value="true">{{ t('mapBooleanNames').split('|')[1] }}</option></select>
          <input v-else v-model="rule.value" :aria-label="t('mapValue')" :title="`${field(rule.key)?.min} … ${field(rule.key)?.max}`" type="text" inputmode="numeric" pattern="-?[0-9]+" required :disabled="busy" />
          <button type="button" :disabled="busy" :aria-label="t('mapRemoveRule')" @click="changes.splice(i, 1)">×</button>
        </div>
        <div class="advanced-row"><button type="button" :disabled="busy || changes.length >= 32" @click="changes.push({ key: 'FreeParam', operator: 'set', value: '0' })">{{ t('mapAddChange') }}</button><button type="button" :disabled="busy || !changes.length" @click="batch">{{ t('mapRunBatch') }}</button></div>
      </details>
      <details class="map-text-batch"><summary>{{ t('advancedTextBatch') }}</summary><p>{{ t('advancedTextHint') }}</p>
        <label>{{ t('mapScope') }}<select v-model="whole" :disabled="busy"><option :value="false">{{ t('mapScopeNames').split('|')[0] }}</option><option :value="true">{{ t('mapScopeNames').split('|')[1] }}</option></select></label>
        <textarea v-model="batchText" rows="7" maxlength="32000" :aria-label="t('advancedTextBatch')" :disabled="busy" spellcheck="false" />
        <button type="button" :disabled="busy || !batchText.trim()" @click="ask('mapTextBatch', { target: { ...data.target }, whole, text: batchText }, t('advancedTextBatch'), scope())">{{ t('mapRunBatch') }}</button>
      </details>
    </form>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="t('mapConfirmTitle')" @cancel.prevent="cancel"><h2>{{ confirmation.label }}</h2><p>{{ confirmation.detail }}</p><p>{{ t('mapParameterHint') }}</p><div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('apply') }}</button></div></dialog>
  </section>
</template>
<style scoped>
.map-advanced, form { display: grid; gap: 1rem; min-width: 0; }
.advanced-row { display: flex; flex-wrap: wrap; gap: .6rem; align-items: center; margin-block: .8rem; }
label { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; min-width: 0; }
select, input, button, textarea { box-sizing: border-box; max-width: 100%; padding: .5rem; color: inherit; background: var(--surface); border: 1px solid var(--line-strong); border-radius: .5rem; }
input { width: 12rem; }
textarea { width: 100%; font: inherit; }
summary { cursor: pointer; font-weight: 600; }
p { line-height: 1.5; }
</style>
