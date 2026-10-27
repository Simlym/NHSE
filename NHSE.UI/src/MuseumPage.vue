<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { t } from './i18n';
import { housingLabel } from './housingLabels';
import type { MuseumData, MuseumEntry, ItemView } from './bridge';
const props = defineProps<{ data: MuseumData | null; busy: boolean;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>; pickItem: () => void }>();
const selected = ref(0), query = ref(''), page = ref(0), level = ref(0);
const form = ref<HTMLFormElement | null>(null), levelForm = ref<HTMLFormElement | null>(null);
const dialog = ref<HTMLDialogElement | null>(null), confirmation = ref<string | null>(null);
const entry = computed(() => props.data?.entries.find(e => e.index === selected.value));
const values = (e?: MuseumEntry) => ({ date: e?.date ?? '', donor: e?.player ?? 0,
  item: { ...(e?.item ?? { itemId: 65534, systemParam: 0, additionalParam: 0, freeParam: 0 }) } });
const fields = ref(values());
const pending = computed(() => !!entry.value && JSON.stringify(fields.value) !== JSON.stringify(values(entry.value)));
watch(() => JSON.stringify([selected.value, values(entry.value)]), () => { fields.value = values(entry.value); }, { immediate: true });
watch(() => props.data?.level, value => { level.value = value ?? 0; }, { immediate: true });
const filtered = computed(() => (props.data?.entries ?? []).filter(e => `${e.index} ${e.name} ${e.item.itemId} ${e.date} ${e.player}`.toLocaleLowerCase().includes(query.value.trim().toLocaleLowerCase())));
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / 60)));
const visible = computed(() => filtered.value.slice(page.value * 60, (page.value + 1) * 60));
watch(query, () => { page.value = 0; });
watch(pages, () => { page.value = Math.min(page.value, pages.value - 1); });
const parts = ['itemId', 'systemParam', 'additionalParam', 'freeParam'] as const;
const names = ['ItemId', 'SystemParam', 'AdditionalParam', 'FreeParam'];
async function applyPending() {
  if (level.value !== props.data?.level && !levelForm.value?.reportValidity()) return false;
  if (pending.value && !form.value?.reportValidity()) return false;
  if (level.value !== props.data?.level && !(await props.execute('setMuseumLevel', { level: level.value }))) return false;
  return !pending.value || await props.execute('setMuseumEntry', { index: selected.value, ...fields.value, item: { ...fields.value.item } });
}
async function select(index: number) { if (await applyPending()) selected.value = index; }
async function ask(action: string) {
  if (props.busy || !(await applyPending())) return;
  confirmation.value = action; await nextTick(); dialog.value?.showModal();
}
function cancel() { dialog.value?.close(); confirmation.value = null; }
async function confirm() {
  const action = confirmation.value; cancel();
  if (action) await props.execute(action === 'import' ? 'museumImport' : 'museumBatch', action === 'import' ? {} : { action });
}
async function exportFile() { if (await applyPending()) await props.execute('museumExport'); }
function chooseItem(item: ItemView) { fields.value.item = { itemId: item.itemId, systemParam: 0, additionalParam: 0, freeParam: 0 }; }
async function clearEntry() {
  if (!(await applyPending())) return;
  await props.execute('setMuseumEntry', { index: selected.value, date: '', donor: 0, item: { itemId: 65534, systemParam: 0, additionalParam: 0, freeParam: 0 } });
}
const actionLabel = (action: string) => t(action === 'import' ? 'museumImport' : action === 'giveAll' ? 'museumGiveAll' : 'museumSort');
defineExpose({ applyPending, chooseItem, hasPending: () => pending.value || level.value !== props.data?.level, resetDraft: () => { fields.value = values(entry.value); level.value = props.data?.level ?? 0; } });
</script>
<template>
  <section v-if="data" class="museum-page">
    <p class="page-description">{{ t('museumHint') }}</p>
    <form ref="levelForm" class="museum-level" @submit.prevent="applyPending">
      <label>{{ t('museumLevel') }}<input v-model.number="level" type="number" required min="-2147483648" max="2147483647" step="1" :disabled="busy" /></label>
      <button class="button" :disabled="busy || level === data.level">{{ t('apply') }}</button>
    </form>
    <div class="form-actions">
      <button class="button" :disabled="busy" @click="ask('giveAll')">{{ t('museumGiveAll') }}</button>
      <button class="button" :disabled="busy" @click="ask('sort')">{{ t('museumSort') }}</button>
      <button class="button" :disabled="busy" @click="ask('import')">{{ t('museumImport') }}</button>
      <button class="button" :disabled="busy" @click="exportFile">{{ t('museumExport') }}</button>
    </div>
    <div class="museum-layout">
      <section class="museum-browser">
        <label class="search-field"><span class="sr-only">{{ t('museumSearch') }}</span><input v-model="query" type="search" :placeholder="t('museumSearch')" :disabled="busy" /></label>
        <p role="status">{{ filtered.length }} / {{ data.entries.length }} · {{ t('page') }} {{ page + 1 }} / {{ pages }}</p>
        <div class="museum-list" role="group" :aria-label="t('museumEntries')">
          <button v-for="row in visible" :key="row.index" class="museum-entry" :class="{ active: row.index === selected }" :aria-pressed="row.index === selected" :disabled="busy" @click="select(row.index)">
            <small>#{{ row.index }} · {{ row.date || '—' }}</small><strong>{{ row.name || t('empty') }}</strong>
          </button>
        </div>
        <p v-if="!visible.length">{{ t('noResults') }}</p>
        <div class="form-actions"><button class="button" :disabled="busy || page === 0" @click="page--">{{ t('previous') }}</button><button class="button" :disabled="busy || page >= pages - 1" @click="page++">{{ t('next') }}</button></div>
      </section>
      <form v-if="entry" ref="form" class="museum-detail" @submit.prevent="applyPending">
        <h2>#{{ entry.index }} · {{ entry.name || t('empty') }}</h2>
        <label>{{ t('museumDate') }}<input v-model="fields.date" type="date" min="0001-01-02" max="9999-12-31" :disabled="busy" /></label>
        <p class="field-help">{{ t('museumDateHint') }} {{ entry.date || '—' }}</p>
        <label>{{ t('museumDonor') }}<input v-model.number="fields.donor" type="number" min="0" max="255" step="1" required :disabled="busy" /></label>
        <button class="button" type="button" :disabled="busy" @click="pickItem">{{ t('mapPickItem') }}</button>
        <label v-for="(part, i) in parts" :key="part">{{ housingLabel(names[i]!) }}
          <input v-model.number="fields.item[part]" type="number" required :min="part === 'freeParam' ? -2147483648 : 0" :max="part === 'freeParam' ? 2147483647 : part === 'itemId' ? 65535 : 255" step="1" :disabled="busy" />
        </label>
        <div class="form-actions"><button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button><button class="button" type="button" :disabled="busy" @click="clearEntry">{{ t('museumClearEntry') }}</button></div>
      </form>
    </div>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="actionLabel(confirmation)" @cancel.prevent="cancel">
      <h2>{{ actionLabel(confirmation) }}</h2><p>{{ t('museumBatchHint') }}</p>
      <div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('apply') }}</button></div>
    </dialog>
  </section>
</template>
<style scoped>
.museum-layout { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr); gap: 1.5rem; margin-top: 1rem; }
.museum-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .5rem; }
.museum-entry { display: grid; gap: .4rem; min-width: 0; text-align: start; padding: .75rem; border: 1px solid var(--line); border-radius: .5rem; background: var(--surface); color: inherit; overflow-wrap: anywhere; }
.museum-entry.active { outline: 2px solid var(--green); }
.museum-detail { display: grid; gap: .75rem; align-content: start; min-width: 0; }
.museum-level { display: flex; gap: .75rem; flex-wrap: wrap; align-items: end; margin-bottom: 1rem; }
label { display: grid; gap: .35rem; min-width: 0; }
input { width: 100%; min-width: 0; box-sizing: border-box; padding: .6rem; border: 1px solid var(--line); border-radius: .5rem; color: inherit; background: var(--surface); font: inherit; }
@media (max-width: 800px) { .museum-layout { grid-template-columns: minmax(0, 1fr); } }
</style>
