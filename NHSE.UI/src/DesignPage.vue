<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { t } from './i18n';
import type { DesignData, DesignKind } from './bridge';
import DesignCanvas from './DesignCanvas.vue';
const props = defineProps<{
  data: DesignData | null;
  busy: boolean;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>;
  navigate: (kind: DesignKind, index: number) => Promise<void>;
}>();
const kinds = ['patterns', 'proDesigns', 'flag', 'tailor'] as const;
const names = computed(() => t('designToolNames').split('|'));
const query = ref('');
const useCurrentPlayer = ref(false);
const sheet = ref(0);
const dialog = ref<HTMLDialogElement | null>(null);
const confirming = ref(false);
const entries = computed(() =>
  (props.data?.entries ?? []).filter((e) =>
    `${e.index + 1} ${e.name}`.toLocaleLowerCase().includes(query.value.toLocaleLowerCase().trim()),
  ),
);
async function navigate(kind: DesignKind, index: number) {
  await props.navigate(kind, index);
  sheet.value = 0;
  if (props.data?.kind !== kind) return;
  query.value = '';
}
async function importDesign(all = false) {
  if (!props.data) return;
  if (all) {
    confirming.value = true;
    await nextTick();
    dialog.value?.showModal();
  } else
    await props.execute('designImport', {
      kind: props.data.kind,
      index: props.data.index,
      all: false,
      useCurrentPlayer: useCurrentPlayer.value,
    });
}
function cancel() {
  dialog.value?.close();
  confirming.value = false;
}
async function confirm() {
  cancel();
  if (props.data)
    await props.execute('designImport', {
      kind: props.data.kind,
      index: props.data.index,
      all: true,
      useCurrentPlayer: useCurrentPlayer.value,
    });
}
async function exportDesign(all: boolean) {
  if (props.data)
    await props.execute('designExport', { kind: props.data.kind, index: props.data.index, all });
}
async function exportPng(palette: boolean) {
  if (props.data)
    await props.execute('designPng', {
      kind: props.data.kind,
      index: props.data.index,
      sheet: Math.min(sheet.value, props.data.selected.sheets.length - 1),
      palette,
    });
}
</script>

<template>
  <section class="design-page">
    <p class="page-description">{{ t('designWorkspaceHint') }}</p>
    <div class="segmented design-tabs" role="group" :aria-label="t('designs')">
      <button
        v-for="(kind, i) in kinds"
        :key="kind"
        :class="{ active: data?.kind === kind }"
        :aria-pressed="data?.kind === kind"
        :disabled="busy"
        @click="navigate(kind, 0)"
      >
        {{ names[i] }}
      </button>
    </div>
    <div v-if="data" class="design-layout">
      <section class="design-library" :aria-label="t('designLibrary')">
        <label class="search-field"
          ><span class="sr-only">{{ t('designSearch') }}</span
          ><input v-model="query" type="search" :placeholder="t('designSearch')" :disabled="busy"
        /></label>
        <p class="field-help" role="status">{{ entries.length }} / {{ data.entries.length }}</p>
        <div class="design-entries">
          <button
            v-for="entry in entries"
            :key="entry.index"
            class="design-entry"
            :class="{ active: entry.index === data.index }"
            :aria-pressed="entry.index === data.index"
            :aria-label="`${entry.index + 1} · ${entry.name || t('designUnnamed')}`"
            :disabled="busy"
            @click="
              props.navigate(data.kind, entry.index);
              sheet = 0;
            "
          >
            <DesignCanvas :bitmap="entry.preview" :label="entry.name || t('designUnnamed')" /><span>{{
              entry.name || t('designUnnamed')
            }}</span
            ><small>#{{ entry.index + 1 }}</small>
          </button>
          <p v-if="!entries.length" class="empty-result">{{ t('noResults') }}</p>
        </div>
      </section>
      <section class="design-detail" :aria-label="t('designPreview')">
        <h2>{{ data.selected.name || t('designUnnamed') }}</h2>
        <dl class="design-meta">
          <div>
            <dt>{{ t('designCreator') }}</dt>
            <dd>{{ data.selected.player || '—' }}</dd>
          </div>
          <div>
            <dt>{{ t('designIsland') }}</dt>
            <dd>{{ data.selected.town || '—' }}</dd>
          </div>
        </dl>
        <div class="design-sheets" :class="{ pro: data.selected.sheets.length > 1 }">
          <button
            v-for="(bitmap, i) in data.selected.sheets"
            :key="i"
            class="design-sheet"
            :class="{ active: sheet === i }"
            :aria-pressed="sheet === i"
            :aria-label="`${t('designSheet')} ${i + 1}`"
            :disabled="busy"
            @click="sheet = i"
          >
            <DesignCanvas :bitmap="bitmap" :label="`${t('designSheet')} ${i + 1}`" /><span
              >{{ t('designSheet') }} {{ i + 1 }}</span
            >
          </button>
        </div>
        <div class="design-palette" role="group" :aria-label="t('designPalette')">
          <span
            v-for="(color, i) in data.selected.palette"
            :key="i"
            :style="{ background: color }"
            :title="`${i + 1}: ${color}`"
          ></span
          ><span class="transparent" :title="t('designTransparent')">×</span>
        </div>
        <div class="design-actions">
          <button class="button" :disabled="busy" @click="exportPng(false)">{{ t('designSavePng') }}</button
          ><button class="button" :disabled="busy" @click="exportPng(true)">
            {{ t('designSavePalette') }}
          </button>
        </div>
        <div class="design-files">
          <label
            >{{ t('designOwnership')
            }}<select v-model="useCurrentPlayer" :disabled="busy">
              <option :value="false">{{ t('designKeepCreator') }}</option>
              <option :value="true">{{ t('designCurrentPlayer') }}</option>
            </select></label
          >
          <p class="field-help">{{ t('designOwnershipHint') }}</p>
          <div class="design-actions">
            <button class="button primary" :disabled="busy" @click="importDesign()">
              {{ t('designImport') }}</button
            ><button class="button" :disabled="busy" @click="exportDesign(false)">
              {{ t('designExport') }}
            </button>
          </div>
          <div v-if="data.entries.length > 1" class="design-actions">
            <button class="button" :disabled="busy" @click="importDesign(true)">
              {{ t('designImportAll') }}</button
            ><button class="button" :disabled="busy" @click="exportDesign(true)">
              {{ t('designExportAll') }}
            </button>
          </div>
          <p class="field-help">{{ t('designFileHint') }}</p>
        </div>
      </section>
    </div>
    <dialog
      v-if="confirming"
      ref="dialog"
      class="modal"
      :aria-label="t('designImportAll')"
      @cancel.prevent="cancel"
    >
      <h2>{{ t('designImportAll') }}</h2>
      <p>{{ t('designBatchHint') }}</p>
      <div class="form-actions">
        <button class="button" @click="cancel">{{ t('cancel') }}</button
        ><button class="button primary" @click="confirm">{{ t('designChooseFolder') }}</button>
      </div>
    </dialog>
  </section>
</template>

<style scoped>
.design-tabs,
.design-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  margin-block: 1rem;
}
.design-layout {
  display: grid;
  grid-template-columns: minmax(230px, 1fr) minmax(300px, 1.2fr);
  gap: 1.5rem;
  align-items: start;
}
.design-library,
.design-detail {
  min-width: 0;
}
.design-entries {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(90px, 1fr));
  gap: 0.6rem;
  max-height: 720px;
  overflow-y: auto;
  padding: 0.2rem;
}
.design-entry {
  display: grid;
  gap: 0.45rem;
  padding: 0.5rem;
  border: 1px solid var(--line);
  border-radius: 0.7rem;
  background: var(--surface);
  text-align: start;
  min-width: 0;
}
.design-entry span {
  overflow-wrap: anywhere;
  font-size: 0.85rem;
}
.design-entry small {
  color: var(--muted);
}
.design-entry.active,
.design-sheet.active {
  border-color: var(--green);
  background: var(--green-soft);
}
.design-detail {
  padding: 1.1rem;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: var(--surface);
}
.design-detail h2 {
  overflow-wrap: anywhere;
}
.design-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  margin-block: 1rem;
}
.design-meta dt {
  color: var(--muted);
  font-size: 0.8rem;
}
.design-meta dd {
  margin: 0.2rem 0;
  overflow-wrap: anywhere;
}
.design-sheets {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  max-width: 320px;
  gap: 0.6rem;
}
.design-sheets.pro {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  max-width: 100%;
}
.design-sheet {
  border: 1px solid var(--line);
  border-radius: 0.5rem;
  background: var(--surface);
  padding: 0.4rem;
  min-width: 0;
}
.design-sheet span {
  display: block;
  margin-top: 0.4rem;
}
.design-palette {
  display: grid;
  grid-template-columns: repeat(16, minmax(0, 1fr));
  margin-top: 1rem;
  border: 1px solid var(--line);
}
.design-palette > span {
  aspect-ratio: 1;
  display: grid;
  place-items: center;
  min-width: 0;
}
.transparent {
  background: repeating-conic-gradient(#dce4dc 0% 25%, #fff 0% 50%) 0 / 8px 8px;
  color: #253c30;
}
.design-files {
  border-top: 1px solid var(--line);
  padding-top: 1rem;
}
.design-files label {
  display: grid;
  gap: 0.5rem;
}
.design-files select {
  width: 100%;
  min-width: 0;
  padding: 0.65rem;
  background: var(--surface);
  color: inherit;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
}
@media (max-width: 760px) {
  .design-layout {
    grid-template-columns: minmax(0, 1fr);
  }
  .design-entries {
    max-height: 300px;
  }
}
</style>
