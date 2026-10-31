<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { t } from './i18n';
import type { ProgressData, ProgressEntry, ProgressSection } from './bridge';
const props = defineProps<{
  data: ProgressData | null;
  section: ProgressSection;
  player: number;
  busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>;
  changeSection: (section: ProgressSection) => Promise<void>;
}>();
const sections = ['recipes', 'reactions', 'achievements', 'collection', 'remakes'] as const;
const collecting = computed(() => props.section === 'collection' || props.section === 'remakes');
const category = ref('everything'), remakeOnly = ref(false);
const collectionCategories = ['everything', 'furniture', 'bugs', 'fish', 'art', 'dive'];
const labels = {
  recipes: 'progressRecipes',
  reactions: 'progressReactions',
  achievements: 'progressAchievements',
  collection: 'progressCollection',
  remakes: 'progressRemakes',
} as const;
const selected = ref(0);
const expanded = ref(false);
const query = ref('');
const page = ref(0);
const pageSize = 24;
const status = ref('all');
const statusOptions = computed(() => [
  { id: 'all', label: t('categoryAll') },
  { id: 'known', label: t(collecting.value ? 'collectionKnown' : 'progressKnown') },
  { id: 'unknown', label: t(collecting.value ? 'collectionUnknown' : 'progressUnknown') },
  ...(props.section === 'recipes' ? [{ id: 'favorite', label: t('progressFavorite') }] : []),
]);
const form = ref<HTMLFormElement | null>(null);
const dialog = ref<HTMLDialogElement | null>(null);
const confirmation = ref<{ action: string; index: number; category?: string; remakeOnly?: boolean; known?: boolean } | null>(null);
const entry = computed(() => props.data?.entries.find((e) => e.index === selected.value));
function values(e?: ProgressEntry) {
  return {
    known: e?.known ?? false,
    made: e?.made ?? false,
    fresh: e?.fresh ?? false,
    favorite: e?.favorite ?? false,
    value: e?.value ?? 0,
    count: e?.count ?? 0,
    flag: e?.flag ?? 0,
    dates: [...(e?.dates ?? [])],
    read: [...(e?.read ?? [])],
  };
}
const fields = ref(values());
const pending = computed(
  () => entry.value && JSON.stringify(fields.value) !== JSON.stringify(values(entry.value)),
);
// Names may change with the language; only saved editable values reset drafts.
watch(
  () => JSON.stringify([props.player, props.section, selected.value, values(entry.value)]),
  () => {
    fields.value = values(entry.value);
  },
  { immediate: true },
);
watch(
  () => [props.player, props.section],
  () => {
    selected.value = 0;
    expanded.value = false;
    query.value = '';
    status.value = 'all';
    page.value = 0;
  },
);
const filtered = computed(() =>
  (props.data?.entries ?? []).filter((e) =>
    `${e.index} ${e.name} ${e.wheel ? t('progressWheel') : ''}`
      .toLocaleLowerCase()
      .includes(query.value.trim().toLocaleLowerCase()) &&
    (status.value === 'all' || (status.value === 'known' ? e.known : status.value === 'unknown' ? !e.known : e.favorite)),
  ),
);
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)));
const visible = computed(() => filtered.value.slice(page.value * pageSize, (page.value + 1) * pageSize));
// Apply while the current form is still mounted, before filtering or paging hides it.
async function search(event: Event) {
  const input = event.target as HTMLInputElement;
  const value = input.value;
  if (!(await applyPending())) { input.value = query.value; return; }
  query.value = value;
  page.value = 0;
}
async function filterStatus(value: string) {
  if (!(await applyPending())) return;
  status.value = value;
  page.value = 0;
}
async function goPage(value: number) {
  if (!(await applyPending())) return;
  if (Number.isInteger(value)) page.value = Math.max(0, Math.min(value, pages.value - 1));
}
async function jumpPage(event: Event) {
  const input = event.target as HTMLInputElement;
  if (input.reportValidity()) await goPage(input.valueAsNumber - 1);
  input.value = String(page.value + 1);
}
watch(pages, () => {
  page.value = Math.min(page.value, pages.value - 1);
});
async function applyPending() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  const payload = {
    index: selected.value,
    ...fields.value,
    dates: [...fields.value.dates],
    read: [...fields.value.read],
  };
  return await props.execute(
    { recipes: 'setRecipe', reactions: 'setReaction', achievements: 'setAchievement', collection: 'setCollection', remakes: 'setCollection' }[props.section],
    { ...payload, section: props.section },
  );
}
async function select(index: number) {
  if (!(await applyPending())) return;
  expanded.value = index !== selected.value || !expanded.value;
  selected.value = index;
}
async function collectionBatch(known: boolean) {
  if (!(await applyPending())) return;
  confirmation.value = { action: 'collectionBatch', index: -1, category: category.value, remakeOnly: remakeOnly.value, known };
  await nextTick(); dialog.value?.showModal();
}
async function batch(action: string, index = -1) {
  if (!(await applyPending())) return;
  if (index === -1) {
    confirmation.value = { action, index };
    await nextTick();
    dialog.value?.showModal();
  } else await props.execute('progressBatch', { section: props.section, action, index });
}
function cancel() {
  dialog.value?.close();
  confirmation.value = null;
}
async function confirm() {
  const operation = confirmation.value;
  cancel();
  if (operation) await props.execute(operation.action === 'collectionBatch' ? 'collectionBatch' : 'progressBatch', { section: props.section, ...operation });
}
defineExpose({ applyPending, hasPending: () => !!pending.value, resetDraft: () => { fields.value = values(entry.value); } });
</script>

<template>
  <section class="progress-page">
    <p class="page-description">{{ t('progressHint') }}</p>
    <div class="segmented progress-tabs" role="group" :aria-label="t('progress')">
      <button
        v-for="sectionId in sections"
        :key="sectionId"
        :class="{ active: section === sectionId }"
        :aria-pressed="section === sectionId"
        :disabled="busy"
        @click="changeSection(sectionId)"
      >
        {{ t(labels[sectionId]) }}
      </button>
    </div>
    <div class="progress-batch">
      <template v-if="section === 'recipes'">
        <button class="button" :disabled="busy || !data" @click="batch('learnAll')">
          {{ t('progressLearnAll') }}
        </button>
        <button class="button" :disabled="busy || !data" @click="batch('craftAll')">
          {{ t('progressCraftAll') }}
        </button>
        <button class="button" :disabled="busy || !data" @click="batch('clearAll')">
          {{ t('progressClearAll') }}
        </button>
      </template>
      <template v-else-if="section === 'reactions'">
        <button class="button" :disabled="busy || !data" @click="batch('learnAll')">
          {{ t('progressLearnAll') }}
        </button>
        <button class="button" :disabled="busy || !data" @click="batch('clearAll')">
          {{ t('progressClearAll') }}
        </button>
      </template>
      <template v-else-if="collecting">
        <label>{{ t('collectionScope') }}<select v-model="category" :disabled="busy"><option v-for="(id, i) in collectionCategories" :key="id" :value="id">{{ t('collectionCategories').split('|')[i] }}</option></select></label>
        <label><input v-model="remakeOnly" type="checkbox" :disabled="busy" />{{ t('collectionVariantsOnly') }}</label>
        <button class="button" :disabled="busy || !data" @click="collectionBatch(true)">{{ t('collectionMark') }}</button>
        <button class="button" :disabled="busy || !data" @click="collectionBatch(false)">{{ t('collectionClear') }}</button>
      </template>
      <template v-else>
        <button class="button" :disabled="busy || !data" @click="batch('complete')">
          {{ t('progressCompleteAll') }}
        </button>
        <button class="button" :disabled="busy || !data" @click="batch('clear')">
          {{ t('progressClearAll') }}
        </button>
      </template>
    </div>
    <p v-if="collecting" class="field-help">{{ t('collectionHint') }}</p>
    <p v-if="section === 'reactions'" class="field-help">{{ t('progressReactionHint') }}</p>
    <div class="progress-layout">
      <div class="progress-browser">
        <label class="search-field"
          ><span class="sr-only">{{ t('progressSearch') }}</span
          ><input :value="query" type="search" @input="search" :placeholder="t('progressSearch')" :disabled="busy"
        /></label>
        <div class="progress-browser-toolbar">
          <div v-if="section === 'recipes' || collecting" class="segmented progress-filters" role="group" :aria-label="t('progress')">
            <button v-for="option in statusOptions" :key="option.id" :class="{ active: status === option.id }"
              :aria-pressed="status === option.id" :disabled="busy" @click="filterStatus(option.id)">{{ option.label }}</button>
          </div>
          <p class="field-help" role="status">{{ filtered.length }} / {{ data?.entries.length ?? 0 }} · {{ t('page') }} {{ page + 1 }} / {{ pages }}</p>
          <div class="pagination progress-pagination-top">
            <button class="button" :disabled="busy || page === 0" @click="goPage(0)">{{ t('firstPage') }}</button>
            <button class="button" :disabled="busy || page === 0" @click="goPage(page - 1)">{{ t('previous') }}</button>
            <label class="progress-page-jump"><span class="sr-only">{{ t('jumpToPage') }}</span><input
              :value="page + 1" :aria-label="t('jumpToPage')" type="number" min="1" :max="pages" step="1" :disabled="busy"
              @change="jumpPage" /> / {{ pages }}</label>
            <button class="button" :disabled="busy || page >= pages - 1" @click="goPage(page + 1)">{{ t('next') }}</button>
            <button class="button" :disabled="busy || page >= pages - 1" @click="goPage(pages - 1)">{{ t('lastPage') }}</button>
          </div>
        </div>
        <div class="progress-list" role="group" :aria-label="t(labels[section])">
          <div v-for="row in visible" :key="row.index" class="progress-record" :class="{ active: row.index === selected }">
            <button
              :aria-expanded="row.index === selected && expanded"
              :aria-controls="row.index === selected && expanded ? 'progress-entry-editor' : undefined"
              class="progress-entry"
              :class="{ active: row.index === selected }"
              :aria-pressed="row.index === selected"
              :disabled="busy"
              @click="select(row.index)"
            >
              <small class="progress-entry-id"
                >#{{ row.index }}
                <template v-if="section === 'reactions'"
                  >· {{ t(row.wheel ? 'progressWheel' : 'progressLearnedSlot') }}
                  {{ (row.slot ?? 0) + 1 }}</template
                ></small
              >
              <span class="progress-entry-name">{{ row.name }}</span>
              <small v-if="section === 'recipes' || collecting" class="progress-entry-status" :class="{ known: row.known }">{{
                t(collecting ? (row.known ? 'collectionKnown' : 'collectionUnknown') : (row.known ? 'progressKnown' : 'progressUnknown'))
              }}</small>
              <small v-if="section === 'achievements'">{{ row.count }}</small>
              <span v-if="section === 'reactions'" aria-hidden="true"></span>
            </button>
            <form v-if="entry && row.index === selected && expanded" id="progress-entry-editor" :ref="(element) => { form = element as HTMLFormElement | null; }" class="progress-editor" @submit.prevent="applyPending">
              <h2 class="sr-only">{{ entry.name }}</h2>
              <div v-if="section === 'recipes'" class="progress-flags">
                <label
                  ><input v-model="fields.known" type="checkbox" :disabled="busy" />{{ t('progressKnown') }}</label
                >
                <label
                  ><input v-model="fields.made" type="checkbox" :disabled="busy" />{{ t('progressMade') }}</label
                >
                <label
                  ><input v-model="fields.fresh" type="checkbox" :disabled="busy" />{{ t('progressNew') }}</label
                >
                <label
                  ><input v-model="fields.favorite" type="checkbox" :disabled="busy" />{{
                    t('progressFavorite')
                  }}</label
                >
              </div>
              <div v-else-if="section === 'reactions'" class="progress-fields">
                <label
                  >{{ t('progressReactionValue')
                  }}<select v-model.number="fields.value" :disabled="busy">
                    <option v-for="option in data?.options" :key="option.id" :value="option.id">
                      {{ option.id }} · {{ option.name }}
                    </option>
                  </select></label
                >
                <label v-if="!entry.wheel"
                  ><input v-model="fields.fresh" type="checkbox" :disabled="busy" />{{ t('progressNew') }}</label
                >
              </div>
              <div v-else-if="collecting" class="progress-flags">
                <label><input v-model="fields.known" type="checkbox" :disabled="busy" />{{ t('collectionKnown') }}</label>
              </div>
              <div v-else class="progress-fields progress-achievement-fields">
                <div class="progress-counters">
                <label
                  >{{ t('progressCount')
                  }}<input
                    v-model.number="fields.count"
                    type="number"
                    min="0"
                    max="4294967295"
                    step="1"
                    required
                    :disabled="busy"
                /></label>
                <label
                  >{{ t('progressFlag')
                  }}<input
                    v-model.number="fields.flag"
                    type="number"
                    min="0"
                    max="255"
                    step="1"
                    required
                    :disabled="busy"
                /></label>
                </div>
                <table class="progress-milestones">
                  <thead><tr>
                    <th scope="col">{{ t('progressMilestone') }}</th>
                    <th scope="col">{{ t('progressDate') }}</th>
                    <th scope="col">{{ t('progressRead') }}</th>
                  </tr></thead>
                  <tbody>
                    <tr v-for="(_, i) in fields.dates" :key="i" class="progress-milestone">
                      <th scope="row">{{ i + 1 }} <span>· {{ entry.thresholds?.[i] || '—' }}</span></th>
                      <td><label><span class="sr-only">{{ t('progressDate') }}</span><input
                        v-model="fields.dates[i]" type="text" pattern="[0-9]{4}-[0-9]{2}-[0-9]{2}"
                        placeholder="YYYY-MM-DD" :disabled="busy"
                      /></label></td>
                      <td><label><span class="sr-only">{{ t('progressRead') }}</span><input
                        v-model="fields.read[i]" type="checkbox" :disabled="busy"
                      /></label></td>
                    </tr>
                  </tbody>
                </table>
                <p class="field-help">{{ t('progressMilestoneHint') }}</p>
              </div>
              <div class="progress-editor-actions">
                <button class="button primary progress-apply" :disabled="busy || !pending">{{ t('apply') }}</button>
                <template v-if="section === 'achievements'">
                  <button type="button" class="button" :disabled="busy" @click="batch('complete', selected)">{{ t('progressCompleteSelected') }}</button>
                  <button type="button" class="button" :disabled="busy" @click="batch('clear', selected)">{{ t('progressClearSelected') }}</button>
                </template>
              </div>
            </form>
          </div>
          <p v-if="!visible.length" class="empty-result">{{ t('noResults') }}</p>
        </div>
        <div class="pagination progress-pagination-bottom">
          <button class="button" :disabled="busy || page === 0" @click="goPage(page - 1)">{{ t('previous') }}</button>
          <span class="field-help">{{ t('page') }} {{ page + 1 }} / {{ pages }}</span>
          <button class="button" :disabled="busy || page >= pages - 1" @click="goPage(page + 1)">{{ t('next') }}</button>
        </div>
      </div>
    </div>
    <dialog
      v-if="confirmation"
      ref="dialog"
      class="modal"
      :aria-label="t('progressBatchTitle')"
      @cancel.prevent="cancel"
    >
      <h2>{{ t('progressBatchTitle') }}</h2>
      <p>{{ t('progressBatchHint') }}</p>
      <p v-if="confirmation.action === 'collectionBatch'" class="collection-confirm">{{ t('collectionScope') }}: {{ t('collectionCategories').split('|')[collectionCategories.indexOf(confirmation.category!)] }} · {{ t(confirmation.known ? 'collectionMark' : 'collectionClear') }} · {{ t(confirmation.remakeOnly ? 'collectionVariantsOnly' : 'collectionWithVariants') }}</p>
      <div class="form-actions">
        <button class="button" @click="cancel">{{ t('cancel') }}</button
        ><button class="button primary" @click="confirm">{{ t('apply') }}</button>
      </div>
    </dialog>
  </section>
</template>

<style scoped>
.progress-tabs,
.progress-batch {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-block: 1rem;
}
.progress-batch { align-items: center; }
.progress-batch label { display: flex; align-items: center; gap: .5rem; }
.progress-batch input[type=checkbox] { width: auto; }
.progress-batch select { max-width: 100%; padding: .5rem; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
.progress-page { max-width: 1120px; }
.progress-browser > .search-field { display: block; max-width: 28rem; }
.progress-layout, .progress-browser { min-width: 0; }
.progress-browser-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: .75rem;
  margin-block: .75rem;
}
.progress-browser-toolbar .field-help { margin: 0; }
.progress-filters { flex-wrap: wrap; }
.progress-page-jump { display: flex; align-items: center; gap: .35rem; font-size: .85rem; }
.progress-page-jump input { width: 4rem; min-width: 0; padding: .5rem; border: 1px solid var(--line); border-radius: .5rem; background: var(--surface); color: inherit; font: inherit; }
.progress-pagination-top { margin: 0; margin-inline-start: auto; flex-wrap: wrap; }
.progress-pagination-top .button, .progress-page-jump { flex: 0 0 auto; white-space: nowrap; }
.progress-pagination-bottom { justify-content: center; align-items: center; gap: 1rem; }
.progress-list {
  border: 1px solid var(--line);
  border-radius: .75rem;
  background: var(--surface);
}
.progress-record + .progress-record { border-top: 1px solid var(--line); }
.progress-record:first-child { border-radius: .75rem .75rem 0 0; }
.progress-record:last-child { border-radius: 0 0 .75rem .75rem; }
.progress-entry.active { background: var(--green-soft); }
.progress-entry {
  display: grid;
  grid-template-columns: minmax(4rem, auto) minmax(0, 1fr) auto 1rem;
  align-items: center;
  gap: 1rem;
  width: 100%;
  min-height: 48px;
  padding: .65rem 1rem;
  border: 0;
  border-radius: inherit;
  background: transparent;
  color: inherit;
  text-align: start;
  cursor: pointer;
}
.progress-entry::after { content: '⌄'; text-align: center; }
.progress-entry[aria-expanded="true"]::after { transform: rotate(180deg); }
.progress-entry:hover { background: var(--green-soft); }
.progress-entry:focus-visible { outline: 2px solid var(--green); outline-offset: -2px; }
.progress-entry-id { color: var(--muted); font-variant-numeric: tabular-nums; }
.progress-entry-name { overflow-wrap: anywhere; font-weight: 600; }
.progress-entry-status { padding: .2rem .5rem; border-radius: .35rem; background: var(--canvas); white-space: nowrap; }
.progress-entry-status.known { color: var(--green); background: var(--green-soft); }
.progress-editor {
  max-width: 34rem;
  min-width: 0;
  padding: .75rem 1rem 1rem;
  border-top: 1px dashed var(--line);
}
.progress-editor:has(.progress-flags) { display: flex; flex-wrap: wrap; align-items: center; gap: 1rem; }
.progress-editor-actions { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; margin-top: .75rem; }
.progress-editor:has(.progress-flags) .progress-editor-actions { margin-top: 0; }
.progress-fields,
.progress-flags {
  display: grid;
  gap: 1rem;
}
.progress-achievement-fields { max-width: 30rem; gap: .65rem; }
.progress-counters { display: flex; flex-wrap: wrap; align-items: end; gap: .75rem 1.5rem; }
.progress-counters input { max-width: 11rem; }
.progress-counters label:last-child input { max-width: 7rem; }
.progress-achievement-fields > .field-help { margin: 0; line-height: 1.5; }
.progress-fields:not(.progress-achievement-fields) { max-width: 24rem; }
.progress-flags { display: flex; flex-wrap: wrap; gap: .75rem 1.5rem; }
.progress-flags label { min-height: 32px; }
.progress-fields label {
  display: grid;
  gap: 0.5rem;
}
.progress-flags label,
.progress-fields label:has(input[type='checkbox']) {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
.progress-flags input[type='checkbox'],
.progress-fields input[type='checkbox'] {
  width: auto;
  flex: 0 0 auto;
  margin: 0;
}
.progress-fields input:not([type='checkbox']),
.progress-fields select {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  padding: .4rem .55rem;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
  background: var(--surface);
  color: inherit;
  font: inherit;
}
.progress-milestones { width: 100%; max-width: 26rem; border-collapse: collapse; font-size: .875rem; }
.progress-milestones th { text-align: start; font-weight: 500; }
.progress-milestones thead { color: var(--muted); }
.progress-milestones th, .progress-milestones td { padding: .15rem .5rem; border-bottom: 1px solid var(--line); }
.progress-milestones th:first-child { padding-inline-start: 0; }
.progress-milestone th span { color: var(--muted); font-variant-numeric: tabular-nums; }
.progress-milestones input:not([type=checkbox]) { padding-block: .3rem; }
.progress-milestones td:nth-child(2) { width: 11rem; }
.progress-milestones td:last-child { width: 3rem; }
.progress-milestones td:last-child label { justify-content: center; min-height: 28px; }
.progress-apply { margin: 0; }
@media (max-width: 760px) {
  .progress-entry { gap: .5rem; padding: .65rem .75rem; grid-template-columns: auto minmax(0, 1fr) auto 1rem; }
  .progress-browser-toolbar { align-items: start; }
  .progress-pagination-top { margin-inline-start: 0; }
  .progress-milestones th, .progress-milestones td { padding-inline: .25rem; }
  .progress-milestones td:nth-child(2) { width: 9rem; }
  .progress-entry-id { max-width: 6rem; overflow-wrap: anywhere; }
}
</style>
