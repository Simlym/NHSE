<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import type { IslandRecordsData, IslandRecordSection } from './bridge';
import { t } from './i18n';
import { islandRecordLabel } from './islandRecordLabels';
import IslandRecordFields from './IslandRecordFields.vue';
const props = defineProps<{ data: IslandRecordsData | null; busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>;
  navigate: (section: IslandRecordSection, index: number) => Promise<void>; back: () => void }>();
const values = ref<Record<string, string>>({});
const query = ref(''), validation = ref(''), form = ref<HTMLFormElement | null>(null);
const stored = () => Object.fromEntries((props.data?.fields ?? []).map(f => [f.key, f.value]));
watch(() => JSON.stringify([props.data?.section, props.data?.index, stored()]), () => { values.value = stored(); validation.value = ''; }, { immediate: true });
const pending = computed(() => JSON.stringify(values.value) !== JSON.stringify(stored()));
watch(() => props.data?.section, () => { query.value = ''; });
const entries = computed(() => props.data?.entries.filter(e => `${e.index} ${e.body} ${e.date}`.toLocaleLowerCase().includes(query.value.trim().toLocaleLowerCase())) ?? []);
const group = (name: string) => props.data?.fields.filter(f => f.group === name) ?? [];
async function applyPending() {
  if (!props.data || !pending.value) return true;
  for (const field of props.data.fields) {
    const value = values.value[field.key]!;
    if (value === field.value) continue;
    let valid = true;
    if (field.type === 'number') {
      try { valid = /^-?[0-9]+$/.test(value) && BigInt(value) >= BigInt(field.min) && BigInt(value) <= BigInt(field.max); }
      catch { valid = false; }
    } else if (field.type === 'float') valid = /^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$/.test(value.trim()) && Number.isFinite(Number(value)) && Math.abs(Number(value)) <= 3.4028234663852886e38;
    else if (field.type === 'hex') valid = value.length === field.length && /^[0-9a-f]*$/i.test(value);
    else if (field.type === 'text') valid = value.length <= field.length && !value.includes('\0');
    else if (field.type === 'date' && value !== '') {
      const date = new Date(`${value}T00:00:00Z`);
      valid = /^\d{4}-\d{2}-\d{2}$/.test(value) && !value.startsWith('0000') && Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
    }
    if (!valid) {
      validation.value = `${islandRecordLabel(field.key)}: ${t(field.type === 'date' ? 'recordDateInvalid' : field.type === 'hex' ? 'recordHexInvalid' : field.type === 'float' ? 'recordFloatInvalid' : 'recordInvalid')}`;
      const label = Array.from(form.value?.querySelectorAll<HTMLElement>('.record-field') ?? []).find(e => e.dataset.key === field.key);
      for (let parent = label?.parentElement; parent; parent = parent.parentElement) if (parent instanceof HTMLDetailsElement) parent.open = true;
      await nextTick(); label?.querySelector<HTMLElement>('input,select,textarea')?.focus(); return false;
    }
  }
  validation.value = '';
  return await props.execute('setIslandRecords', { section: props.data.section, index: props.data.index, values: { ...values.value } });
}
async function navigate(section: IslandRecordSection, index = 0) { if (await applyPending()) await props.navigate(section, index); }
defineExpose({ applyPending, hasPending: () => pending.value, resetDraft: () => { values.value = stored(); validation.value = ''; } });
</script>
<template>
  <section v-if="data" class="island-records-page">
    <button class="button" :disabled="busy" @click="back">← {{ t('island') }}</button>
    <p class="page-description">{{ t('recordScopeHint') }}</p>
    <div class="segmented record-tabs"><button v-for="section in (['bulletin', 'visitors'] as const)" :key="section" :disabled="busy" :aria-pressed="data.section === section" :class="{ active: data.section === section }" @click="navigate(section)">{{ t(section) }}</button></div>
    <div class="record-layout" :class="{ visitors: data.section === 'visitors' }">
      <aside v-if="data.section === 'bulletin'" class="record-browser">
        <label class="search-field"><span class="sr-only">{{ t('recordSearch') }}</span><input v-model="query" type="search" :placeholder="t('recordSearch')" :disabled="busy" /></label>
        <p role="status">{{ entries.length }} / {{ data.entries.length }}</p>
        <div class="record-list"><button v-for="entry in entries" :key="entry.index" class="record-entry" :aria-pressed="entry.index === data.index" :disabled="busy" @click="navigate('bulletin', entry.index)"><small>#{{ entry.index }} · {{ entry.date || '—' }}</small><span>{{ entry.body || t('empty') }}</span></button></div>
      </aside>
      <form ref="form" class="record-editor" novalidate @submit.prevent="applyPending">
        <template v-if="data.section === 'bulletin'">
          <h2>{{ t('bulletin') }} · #{{ data.index }}</h2>
          <IslandRecordFields :fields="group('content')" :values="values" :busy="busy" />
          <fieldset><legend>{{ t('recordAuthor') }}</legend><IslandRecordFields :fields="group('author')" :values="values" :busy="busy" /></fieldset>
          <details class="record-preview"><summary>{{ t('recordPreview') }}</summary><article><time>{{ values.Date || '—' }}</time><p>{{ values.Body }}</p><footer>{{ values.Footer }}</footer></article><p class="field-help">{{ t('recordPreviewHint') }}</p></details>
          <details><summary>{{ t('recordHandwriting') }}</summary><p class="field-help">{{ t('recordHexHint') }}</p><IslandRecordFields :fields="group('handwriting')" :values="values" :busy="busy" /></details>
          <details><summary>{{ t('recordBoard') }}</summary><p class="field-help">{{ t('recordBoardHint') }}</p><IslandRecordFields :fields="group('board')" :values="values" :busy="busy" /></details>
        </template>
        <template v-else>
          <h2>{{ t('visitors') }}</h2><p class="field-help">{{ t('recordVisitorHint') }}</p>
          <div class="record-week"><fieldset v-for="day in 7" :key="day" class="record-day"><legend>{{ t('slot') }} {{ day - 1 }}</legend><IslandRecordFields :fields="group('day' + (day - 1))" :values="values" :busy="busy" /></fieldset></div>
          <fieldset><legend>{{ t('recordSpecialVisitors') }}</legend><IslandRecordFields :fields="group('special')" :values="values" :busy="busy" /></fieldset>
        </template>
        <details><summary>{{ t('recordAdvanced') }}</summary><IslandRecordFields :fields="group('advanced')" :values="values" :busy="busy" /></details>
        <p v-if="validation" role="alert">{{ validation }}</p><button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
      </form>
    </div>
  </section>
</template>
<style scoped>
.record-tabs { margin-block: 1rem; display: flex; flex-wrap: wrap; }
.record-layout { display: grid; grid-template-columns: minmax(180px, .7fr) minmax(0, 1.3fr); gap: 1.25rem; align-items: start; }
.record-layout.visitors { grid-template-columns: minmax(0, 1fr); }
.record-editor, .record-browser { min-width: 0; padding: 1rem; border: 1px solid var(--line); border-radius: 1rem; background: var(--surface); }
.record-editor { display: grid; gap: 1rem; }
.record-list { display: grid; gap: .5rem; max-height: 650px; overflow: auto; padding: 3px; }
.record-entry { display: grid; gap: .4rem; min-width: 0; text-align: start; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; padding: .7rem; }
.record-entry[aria-pressed=true] { outline: 2px solid var(--green); }
.record-entry span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.record-week { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 1rem; }
fieldset { min-width: 0; border: 1px solid var(--line); border-radius: .6rem; padding: .8rem; }
summary { cursor: pointer; margin-bottom: .75rem; }
.record-preview article { padding: 1rem; background: var(--green-soft); border-radius: .6rem; overflow-wrap: anywhere; }
.record-preview article p { white-space: pre-wrap; }
.record-preview footer { text-align: end; white-space: pre-wrap; }
@media(max-width:800px) { .record-layout { grid-template-columns: minmax(0, 1fr); } .record-list { max-height: 240px; } }
@media(max-width:500px) { .record-week { grid-template-columns: minmax(0, 1fr); } }
</style>
