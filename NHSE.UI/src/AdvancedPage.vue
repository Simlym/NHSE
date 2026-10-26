<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from 'vue';
import { t } from './i18n';
import { housingLabel } from './housingLabels';
import AdvancedFieldInput from './AdvancedFieldInput.vue';
import type { AdvancedData, AdvancedTarget, Player, Resident, ItemView } from './bridge';
const props = defineProps<{ data: AdvancedData | null; players: Player[]; residents: Resident[]; busy: boolean;
  itemOnly?: boolean; back: () => void;
  navigate: (target: Partial<AdvancedTarget>) => Promise<void>; execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>; pickItem: () => void }>();
const kinds = ['flagsPlayer', 'flagsLand', 'flagsResident', 'resident', 'memory', 'wear', 'furniture', 'room', 'mailbox', 'item'];
const kindNames = computed(() => t('advancedKinds').split('|'));
const residentKinds = ['flagsResident', 'resident', 'memory', 'wear', 'furniture', 'room'];
const itemKinds = ['item', 'wear', 'furniture'];
const playerScoped = computed(() => props.data && ['flagsPlayer', 'mailbox', 'item'].includes(props.data.target.kind) && !(props.data.target.kind === 'item' && props.data.target.container === 'recycle'));
const query = ref(''), fieldQuery = ref(''), page = ref(0), text = ref('');
const form = ref<HTMLFormElement | null>(null), dialog = ref<HTMLDialogElement | null>(null);
const fields = ref<Record<string, string>>({});
const stored = () => Object.fromEntries((props.data?.fields ?? []).map(f => [f.key, f.value]));
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(stored()));
watch(() => JSON.stringify([props.data?.target, stored()]), () => { fields.value = stored(); }, { immediate: true });
watch(() => JSON.stringify(props.data?.target), (value, previous) => {
  const next = value && JSON.parse(value), old = previous && JSON.parse(previous);
  if (!old || next?.kind !== old.kind || next?.player !== old.player || next?.resident !== old.resident || next?.container !== old.container) { query.value = ''; page.value = 0; }
  fieldQuery.value = '';
});
const filtered = computed(() => props.data?.entries.filter(e => `${e.index} ${e.name}`.toLocaleLowerCase().includes(query.value.trim().toLocaleLowerCase())) ?? []);
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / 60)));
const visible = computed(() => filtered.value.slice(page.value * 60, (page.value + 1) * 60));
watch(query, () => { page.value = 0; });
watch(pages, () => { page.value = Math.min(page.value, pages.value - 1); });
const parameterKeys = ['RawValue', 'Rotation', 'IsBuried', 'IsDropped', 'Count', 'UseCount', 'BodyType', 'PatternSource', 'PatternChoice', 'DaysWatered', 'IsWatered', 'IsWateredGold', 'Genes', 'WrappingType', 'WrappingPaper', 'WrappingShowItem', 'Wrapping80', 'IsWrapped', 'DisplayItemId', 'IsNone', 'IsExtension', 'IsRoot', 'IsFieldItem', 'Size'];
const mailKeys = ['ReadStatus', 'Favourite', 'SenderIslandID', 'SenderIslandName', 'SenderSpecies', 'SenderVariant', 'NPCIslandID', 'NPCIslandName', 'SenderID', 'SenderName', 'SenderType', 'ReceiverIslandID', 'ReceiverIslandName', 'ReceiverPlayerID', 'ReceiverPlayerName', 'Year', 'Month', 'Day', 'MailType01', 'MailType02', 'To', 'Message', 'From', 'Stationery', 'SortIndex', 'Language'];
const residentKeys = ['Species', 'Variant', 'BirthType', 'InducementType', 'MoveType', 'DIYEndHour', 'DIYEndMinute', 'DIYEndSecond', 'DIYRecipeIndex', 'TownID', 'TownName', 'PlayerID', 'PlayerName', 'Friendship', 'NickName', 'Value', 'Personality', 'CatchPhrase', 'MovingOut'];
function label(key: string): string {
  if (key.startsWith('Greeting.')) return `${t('advancedGreeting')} #${key.split('.')[1]}`;
  if (key.startsWith('Flag.')) return `${t('housingEventFlag')} #${key.split('.')[1]}`;
  if (key.startsWith('WateredVisitor.')) return `${t('advancedWateredVisitor')} #${key.split('.')[1]}`;
  const p = parameterKeys.indexOf(key); if (p >= 0) return t('mapParamNames').split('|')[p] ?? key;
  const m = mailKeys.indexOf(key); if (props.data?.target.kind === 'mailbox' && m >= 0) return t('advancedMailFields').split('|')[m] ?? key;
  const r = residentKeys.indexOf(key); if (r >= 0) return t('advancedResidentFields').split('|')[r] ?? key;
  return housingLabel(key);
}
const visibleFields = computed(() => props.data?.fields.filter(f => `${f.key} ${label(f.key)}`.toLocaleLowerCase().includes(fieldQuery.value.trim().toLocaleLowerCase())) ?? []);
// Group presentation only; every field still uses the host's original key, value and limits.
const groupNames = computed(() => t('advancedGroupNames').split('|'));
const sections = computed(() => {
  const kind = props.data?.target.kind;
  const groups: { id: number; keys?: string[]; prefix?: string; open?: boolean }[] = kind === 'mailbox' ? [
    { id: 0, keys: ['To', 'Message', 'From', 'Stationery', 'Language'] },
    { id: 1, keys: ['ItemId', 'SystemParam', 'AdditionalParam', 'FreeParam'] },
    { id: 4, keys: ['Year', 'Month', 'Day', 'ReadStatus', 'Favourite'] },
    { id: 2, keys: ['SenderType', 'SenderIslandID', 'SenderIslandName', 'SenderSpecies', 'SenderVariant', 'NPCIslandID', 'NPCIslandName', 'SenderID', 'SenderName'], open: false },
    { id: 3, keys: ['ReceiverIslandID', 'ReceiverIslandName', 'ReceiverPlayerID', 'ReceiverPlayerName'], open: false },
  ] : kind === 'resident' ? [
    { id: 6, keys: ['Species', 'Variant', 'Personality', 'CatchPhrase', 'BirthType', 'InducementType', 'MoveType', 'MovingOut'] },
    { id: 7, keys: ['DIYRecipeIndex', 'DIYEndHour', 'DIYEndMinute', 'DIYEndSecond'] },
  ] : kind === 'memory' ? [
    { id: 8, keys: ['TownID', 'TownName', 'PlayerID', 'PlayerName', 'Friendship', 'NickName'] },
    { id: 9, prefix: 'Greeting.' }, { id: 10, prefix: 'Flag.' },
  ] : itemKinds.includes(kind ?? '') ? [
    { id: 11, keys: ['ItemId', 'Count', 'UseCount', 'Rotation', 'BodyType', 'PatternSource', 'PatternChoice', 'IsBuried', 'IsDropped', 'IsWrapped', 'WrappingType', 'WrappingPaper', 'WrappingShowItem', 'Wrapping80'] },
    { id: 12, keys: ['DaysWatered', 'IsWatered', 'IsWateredGold', 'Genes'], prefix: 'WateredVisitor.' },
  ] : [];
  const used = new Set<string>();
  const mapped = groups.map(group => {
    const fields = visibleFields.value.filter(f => group.keys?.includes(f.key) || (group.prefix && f.key.startsWith(group.prefix)));
    fields.forEach(f => used.add(f.key));
    return { ...group, fields };
  });
  const rest = visibleFields.value.filter(f => !used.has(f.key));
  if (rest.length) mapped.push({ id: kind?.startsWith('flags') ? 10 : kind === 'room' ? 13 : 5, fields: rest, open: kind !== 'mailbox' });
  return mapped.filter(group => group.fields.length);
});
const validation = ref('');
async function revealField(key: string) {
  // Searching or collapsing a section must never hide an invalid draft on navigation.
  fieldQuery.value = '';
  await nextTick();
  const input = form.value?.querySelector<HTMLElement>(`[data-field-key="${CSS.escape(key)}"] input, [data-field-key="${CSS.escape(key)}"] textarea, [data-field-key="${CSS.escape(key)}"] select`);
  const section = input?.closest('details'); if (section) section.open = true;
  await nextTick(); input?.focus();
}
async function reportValidity() {
  // Native form validation skips controls removed by field search; validate the full draft first.
  for (const field of props.data?.fields ?? []) {
    const value = fields.value[field.key]!;
    if (field.type === 'number') {
      try { const n = BigInt(value); if (!/^-?[0-9]+$/.test(value) || n < BigInt(field.min) || n > BigInt(field.max)) throw new Error(); }
      catch { validation.value = `${label(field.key)}: ${field.min} … ${field.max}`; await revealField(field.key); return false; }
    } else if (field.type === 'text' && value.length > field.length) {
      validation.value = `${label(field.key)}: ${value.length} / ${field.length}`; await revealField(field.key); return false;
    }
  }
  return true;
}
async function applyPending() {
  if (!props.data || !pending.value) return true;
  if (!(await reportValidity())) return false;
  validation.value = '';
  return await props.execute('setAdvanced', { target: { ...props.data.target }, values: { ...fields.value } });
}
async function navigate(part: Partial<AdvancedTarget>) { if (await applyPending()) await props.navigate(part); }
async function change(event: Event, key: keyof AdvancedTarget) {
  const input = event.target as HTMLSelectElement | HTMLInputElement;
  const value = input.value;
  try {
    if (!input.reportValidity()) return;
    await navigate({ [key]: ['player', 'resident', 'index'].includes(key) ? Number(value) : value,
      ...(['kind', 'player', 'resident', 'container'].includes(key) ? { index: 0 } : {}) });
  } finally {
    // Native controls change before validation/navigation; reflect the accepted target on every outcome.
    await nextTick();
    if (props.data) input.value = String(props.data.target[key]);
  }
}
const confirmation = shallowRef<{ name: string; payload: Record<string, unknown>; title: string } | null>(null);
async function ask(name: string, payload: Record<string, unknown>, title: string) {
  if (props.busy || !(await applyPending())) return;
  confirmation.value = { name, payload, title }; await nextTick(); dialog.value?.showModal();
}
function cancel() { dialog.value?.close(); confirmation.value = null; }
async function confirm() { const operation = confirmation.value; cancel(); if (operation) await props.execute(operation.name, operation.payload); }
async function file(importFile: boolean, all: boolean) {
  if (!props.data || !(await applyPending())) return;
  const payload = { target: { ...props.data.target }, all };
  if (importFile) await ask('advancedImport', payload, t('advancedImport'));
  else await props.execute('advancedExport', payload);
}
function chooseItem(item: ItemView) {
  for (const [key, value] of Object.entries({ ItemId: item.itemId, SystemParam: 0, AdditionalParam: 0, FreeParam: 0 })) if (key in fields.value) fields.value[key] = String(value);
}
defineExpose({ applyPending, chooseItem });
</script>
<template>
  <section v-if="data" class="advanced-page">
    <button class="button advanced-back" :disabled="busy" @click="back">← {{ t('back') }}</button>
    <p class="page-description">{{ t('advancedHint') }}</p>
    <div v-if="!itemOnly" class="advanced-target">
      <label>{{ t('advancedSection') }}<select :value="data.target.kind" :disabled="busy" @change="change($event, 'kind')"><option v-for="(kind, i) in kinds" :key="kind" :value="kind">{{ kindNames[i] }}</option></select></label>
      <label v-if="playerScoped">{{ t('player') }}<select :value="data.target.player" :disabled="busy" @change="change($event, 'player')"><option v-for="player in players" :key="player.index" :value="player.index">{{ player.name }}</option></select></label>
      <label v-if="residentKinds.includes(data.target.kind)">{{ t('villagers') }}<select :value="data.target.resident" :disabled="busy" @change="change($event, 'resident')"><option v-for="resident in residents" :key="resident.index" :value="resident.index">{{ resident.index }} · {{ resident.name }}</option></select></label>
      <label v-if="data.target.kind === 'item'">{{ t('inventory') }}<select :value="data.target.container" :disabled="busy" @change="change($event, 'container')"><option v-for="container in ['pocket', 'storage', 'recycle']" :key="container" :value="container">{{ t(container as 'pocket' | 'storage' | 'recycle') }}</option></select></label>
    </div>
    <details class="advanced-file-tools" :open="!itemOnly">
    <summary>{{ t('advancedFileTools') }}</summary>
    <div class="form-actions">
      <button class="button" :disabled="busy" @click="file(true, false)">{{ t('advancedImport') }}</button><button class="button" :disabled="busy" @click="file(false, false)">{{ t('advancedExport') }}</button><button class="button" :disabled="busy" @click="file(false, true)">{{ t('advancedExportAll') }}</button>
      <button v-if="itemKinds.includes(data.target.kind)" class="button" :disabled="busy" @click="file(true, true)">{{ t('advancedImportAll') }}</button>
      <button v-if="data.target.kind === 'resident'" class="button" :disabled="busy" @click="execute('tool', { tool: 'residentDesigns', index: data.target.resident })">{{ t('advancedResidentDesigns') }}</button>
    </div>
    <p class="field-help">{{ t('advancedFileHint') }} · .{{ data.extension }} · {{ data.size }}</p>
    </details>
    <div v-if="residentKinds.includes(data.target.kind)" class="form-actions">
      <button v-for="(operation, i) in ['friendship', 'moveOut', 'resetPhrase']" :key="operation" class="button" :disabled="busy" @click="ask('residentBatch', { operation }, t('advancedResidentActions').split('|')[i]!)">{{ t('advancedResidentActions').split('|')[i] }}</button>
    </div>
    <div class="advanced-layout" :class="{ 'item-only': itemOnly }">
      <section v-if="!itemOnly" class="advanced-browser">
        <label class="search-field"><span class="sr-only">{{ t('advancedSearch') }}</span><input v-model="query" type="search" :placeholder="t('advancedSearch')" :disabled="busy" /></label>
        <p role="status">{{ filtered.length }} · {{ t('page') }} {{ page + 1 }} / {{ pages }}</p>
        <label>{{ t('slot') }}<input type="number" min="0" :max="data.entries.length - 1" :value="data.target.index" :disabled="busy" @change="change($event, 'index')" /></label>
        <div class="advanced-list"><button v-for="entry in visible" :key="entry.index" class="advanced-entry" :aria-pressed="entry.index === data.target.index" :disabled="busy || !entry.available" @click="navigate({ index: entry.index })">#{{ entry.index }} · {{ entry.name || t('empty') }}</button></div>
        <div class="form-actions"><button class="button" :disabled="busy || !page" @click="page--">{{ t('previous') }}</button><button class="button" :disabled="busy || page >= pages - 1" @click="page++">{{ t('next') }}</button></div>
      </section>
      <form ref="form" class="advanced-fields" novalidate @submit.prevent="applyPending">
        <h2>{{ kindNames[kinds.indexOf(data.target.kind)] }} · #{{ data.target.index }}</h2>
        <label class="search-field"><span class="sr-only">{{ t('advancedSearchFields') }}</span><input v-model="fieldQuery" type="search" :placeholder="t('advancedSearchFields')" :disabled="busy" /></label>
        <p v-if="pending" class="draft-status" role="status">{{ t('advancedDraftPending') }}</p>
        <p v-if="!sections.length" class="field-help" role="status">{{ t('advancedNoFields') }}</p>
        <details v-for="section in sections" :key="`${data.target.kind}:${section.id}`" class="advanced-section" :data-group="section.id" :open="!!fieldQuery.trim() || section.open !== false">
          <summary>{{ groupNames[section.id] }} <span>{{ section.fields.length }}</span></summary>
          <div class="advanced-section-fields">
            <p v-if="data.target.kind === 'mailbox' && section.id === 2" class="field-help section-help">{{ t('advancedSenderOverlapHint') }}</p>
            <button v-if="(data.target.kind === 'mailbox' && section.id === 1) || (itemKinds.includes(data.target.kind) && section.id === 11)" class="button section-action" type="button" :disabled="busy" @click="pickItem">{{ t('mapPickItem') }}</button>
            <AdvancedFieldInput v-for="field in section.fields" :key="field.key" :field="field" :label="label(field.key)" :model-value="fields[field.key]!" :busy="busy" @update:model-value="fields[field.key] = $event" />
          </div>
        </details>
        <details v-if="data.target.kind === 'mailbox' && !fieldQuery.trim()" class="mail-preview">
          <summary>{{ t('advancedMailPreview') }}</summary>
          <article><p>{{ fields.To }}</p><p class="letter-body">{{ fields.Message }}</p><p>{{ fields.From }}</p></article>
        </details>
        <p v-if="validation" role="alert">{{ validation }}</p><button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
      </form>
    </div>
    <details v-if="itemKinds.includes(data.target.kind)" class="advanced-text-batch" :open="!itemOnly">
      <summary>{{ t('advancedTextBatch') }}</summary>
      <h2>{{ t('advancedTextBatch') }}</h2><p>{{ t('advancedTextHint') }}</p>
      <textarea v-model="text" rows="7" maxlength="32000" :aria-label="t('advancedTextBatch')" :disabled="busy" spellcheck="false" />
      <button class="button" :disabled="busy || !text.trim()" @click="ask('advancedTextBatch', { target: { ...data!.target }, text }, t('advancedTextBatch'))">{{ t('mapRunBatch') }}</button>
    </details>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="confirmation.title" @cancel.prevent="cancel"><h2>{{ confirmation.title }}</h2><p>{{ t('advancedConfirm') }}</p><p>{{ kindNames[kinds.indexOf(data.target.kind)] }}<template v-if="playerScoped"> · {{ players.find(p => p.index === data!.target.player)?.name }}</template><template v-if="residentKinds.includes(data.target.kind)"> · {{ t('villagers') }} #{{ data.target.resident }}</template> · #{{ data.target.index }}</p><div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('apply') }}</button></div></dialog>
  </section>
</template>
<style scoped>
.advanced-target, .form-actions { display: flex; flex-wrap: wrap; gap: .75rem; margin-block: 1rem; }
.advanced-layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 1.5rem; }
.advanced-layout.item-only { grid-template-columns: minmax(0, 1fr); max-width: 960px; }
.advanced-back { margin-bottom: 1rem; }
.advanced-file-tools { margin-bottom: 1rem; }
summary { cursor: pointer; color: var(--green); }
.advanced-browser, .advanced-fields { padding: 20px; background: var(--surface); border: 1px solid var(--line); border-radius: 12px; }
.advanced-list { display: grid; gap: .4rem; margin-block: .75rem; max-height: 480px; overflow-y: auto; padding: 3px; }
.item-only .advanced-fields { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.advanced-fields > h2, .advanced-fields > .search-field, .advanced-fields > button, .advanced-fields > [role=alert], .advanced-fields > .draft-status, .advanced-fields > .advanced-section, .advanced-fields > .mail-preview { grid-column: 1 / -1; }
.advanced-entry { text-align: start; padding: .6rem; overflow-wrap: anywhere; }
.advanced-entry[aria-pressed=true] { outline: 2px solid var(--green); }
.advanced-fields { display: grid; gap: .8rem; align-content: start; min-width: 0; }
label { display: grid; gap: .3rem; min-width: 0; }
small { font-size: .75rem; overflow-wrap: anywhere; opacity: .75; }
input, select, textarea, .advanced-entry { box-sizing: border-box; max-width: 100%; min-width: 0; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
input, select, textarea { padding: .65rem; width: 100%; font: inherit; }
.advanced-text-batch { margin-top: 2rem; }
.advanced-section { min-width: 0; border: 1px solid var(--line); border-radius: 8px; }
.advanced-section > summary { padding: .8rem; font-weight: 600; }
.advanced-section > summary span { font-weight: 400; font-size: .8rem; color: var(--muted); margin-inline-start: .5rem; }
.advanced-section-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .85rem; padding: 0 .8rem .8rem; }
.section-help, .section-action { grid-column: 1 / -1; }
.section-action { justify-self: start; }
.section-help { margin: 0; }
.draft-status { margin: 0; color: var(--green); font-size: .85rem; }
.mail-preview article { margin-top: .5rem; padding: 1rem; border: 1px dashed var(--line); border-radius: .5rem; overflow-wrap: anywhere; }
.mail-preview article p { white-space: pre-wrap; }
.letter-body { min-height: 4rem; }
@media(max-width: 1100px) { .advanced-section-fields { grid-template-columns: minmax(0, 1fr); } }
@media(max-width: 800px) { .advanced-layout, .item-only .advanced-fields { grid-template-columns: minmax(0, 1fr); } }
</style>
