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
const query = ref('');
const page = ref(0);
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
    query.value = '';
    page.value = 0;
  },
);
const filtered = computed(() =>
  (props.data?.entries ?? []).filter((e) =>
    `${e.index} ${e.name} ${e.wheel ? t('progressWheel') : ''}`
      .toLocaleLowerCase()
      .includes(query.value.trim().toLocaleLowerCase()),
  ),
);
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / 60)));
const visible = computed(() => filtered.value.slice(page.value * 60, (page.value + 1) * 60));
watch(query, () => {
  page.value = 0;
});
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
  if (await applyPending()) selected.value = index;
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
defineExpose({ applyPending });
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
          ><input v-model="query" type="search" :placeholder="t('progressSearch')" :disabled="busy"
        /></label>
        <p class="field-help" role="status">
          {{ filtered.length }} · {{ t('page') }} {{ page + 1 }} / {{ pages }}
        </p>
        <div class="progress-list" role="group" :aria-label="t(labels[section])">
          <button
            v-for="row in visible"
            :key="row.index"
            class="progress-entry"
            :class="{ active: row.index === selected }"
            :aria-pressed="row.index === selected"
            :disabled="busy"
            @click="select(row.index)"
          >
            <small
              >#{{ row.index }}
              <template v-if="section === 'reactions'"
                >· {{ t(row.wheel ? 'progressWheel' : 'progressLearnedSlot') }}
                {{ (row.slot ?? 0) + 1 }}</template
              ></small
            >
            <span>{{ row.name }}</span>
            <small v-if="section === 'recipes' || collecting">{{
              t(collecting ? (row.known ? 'collectionKnown' : 'collectionUnknown') : (row.known ? 'progressKnown' : 'progressUnknown'))
            }}</small>
            <small v-if="section === 'achievements'">{{ row.count }}</small>
          </button>
          <p v-if="!visible.length" class="empty-result">{{ t('noResults') }}</p>
        </div>
        <div class="pagination">
          <button class="button" :disabled="busy || page === 0" @click="page--">{{ t('previous') }}</button
          ><button class="button" :disabled="busy || page >= pages - 1" @click="page++">
            {{ t('next') }}
          </button>
        </div>
      </div>
      <form v-if="entry" ref="form" class="progress-editor" @submit.prevent="applyPending">
        <h2>{{ entry.name }}</h2>
        <p class="field-help">#{{ entry.index }}</p>
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
        <div v-else class="progress-fields">
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
          <p class="field-help">{{ t('progressMilestoneHint') }}</p>
          <fieldset v-for="(_, i) in fields.dates" :key="i" class="progress-milestone">
            <legend>{{ t('progressMilestone') }} {{ i + 1 }} · {{ entry.thresholds?.[i] || '—' }}</legend>
            <label
              >{{ t('progressDate')
              }}<input
                v-model="fields.dates[i]"
                type="text"
                pattern="[0-9]{4}-[0-9]{2}-[0-9]{2}"
                placeholder="YYYY-MM-DD"
                :disabled="busy"
            /></label>
            <label
              ><input v-model="fields.read[i]" type="checkbox" :disabled="busy" />{{
                t('progressRead')
              }}</label
            >
          </fieldset>
          <div class="progress-batch">
            <button type="button" class="button" :disabled="busy" @click="batch('complete', selected)">
              {{ t('progressCompleteSelected') }}</button
            ><button type="button" class="button" :disabled="busy" @click="batch('clear', selected)">
              {{ t('progressClearSelected') }}
            </button>
          </div>
        </div>
        <button class="button primary progress-apply" :disabled="busy || !pending">{{ t('apply') }}</button>
      </form>
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
.progress-layout {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) minmax(260px, 1fr);
  gap: 1.5rem;
  align-items: start;
}
.progress-browser,
.progress-editor {
  min-width: 0;
}
.progress-list {
  display: grid;
  gap: 0.35rem;
  max-height: 660px;
  overflow-y: auto;
}
.progress-entry {
  display: grid;
  gap: 0.25rem;
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.6rem;
  background: var(--surface);
  color: inherit;
  text-align: start;
  overflow-wrap: anywhere;
}
.progress-entry.active {
  border-color: var(--green);
  background: var(--green-soft);
}
.progress-entry small {
  opacity: 0.7;
}
.progress-editor {
  padding: 1rem;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 1rem;
}
.progress-editor h2 {
  overflow-wrap: anywhere;
}
.progress-fields,
.progress-flags {
  display: grid;
  gap: 1rem;
}
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
  padding: 0.65rem;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
  background: var(--surface);
  color: inherit;
  font: inherit;
}
.progress-milestone {
  min-width: 0;
  border: 1px solid var(--line);
  border-radius: 0.6rem;
  padding: 0.75rem;
  display: grid;
  gap: 0.75rem;
}
.progress-apply {
  margin-top: 1rem;
}
@media (max-width: 760px) {
  .progress-layout {
    grid-template-columns: minmax(0, 1fr);
  }
  .progress-list {
    max-height: 300px;
  }
}
</style>
