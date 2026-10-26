<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { hasText, t } from './i18n';
import type { ImageLibraryState } from './bridge';
const props = defineProps<{ data: ImageLibraryState | null; busy: boolean; execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean> }>();
const source = ref(props.data?.source ?? 0), dialog = ref<HTMLDialogElement | null>(null), confirmation = ref(false);
const running = computed(() => ['downloading', 'extracting', 'installing', 'cancelling'].includes(props.data?.phase ?? ''));
const phases = ['idle', 'downloading', 'extracting', 'installing', 'complete', 'failed', 'cancelled', 'cancelling'];
const selected = computed(() => props.data?.sources.find(s => s.id === source.value));
const size = computed(() => props.data?.source === source.value ? props.data.size : null);
watch(() => props.data?.source, value => { if (value !== undefined) source.value = value; });
const percent = computed(() => props.data?.phase === 'extracting' ? props.data.fileCount ? props.data.files / props.data.fileCount * 100 : undefined
  : props.data?.total ? Math.min(100, props.data.received / props.data.total * 100) : undefined);
const mb = (bytes: number) => `${(bytes / 1e6).toFixed(1)} MB`;
async function start() { confirmation.value = true; await nextTick(); dialog.value?.showModal(); }
function cancel() { dialog.value?.close(); confirmation.value = false; }
async function confirm() { cancel(); await props.execute('imageStart', { source: source.value }); }
</script>
<template>
  <section class="image-library-tools">
    <h2>{{ t('imagesTitle') }}</h2><p class="field-help">{{ t('imagesHint') }}</p>
    <template v-if="data">
      <p>{{ t('imagesInstalled') }}: {{ data.installed }}</p>
      <label>{{ t('imagesSource') }}<select v-model.number="source" :disabled="busy || running"><option v-for="s in data.sources" :key="s.id" :value="s.id">{{ s.name }}</option></select></label>
      <p v-if="selected" class="images-source-url">{{ selected.url }}</p>
      <div class="form-actions"><button class="button" :disabled="busy || running" @click="execute('imageSourceSize', { source })">{{ t('imagesCheckSize') }}</button><span>{{ size == null ? t('imagesUnknownSize') : mb(size) }}</span><button class="button primary" :disabled="busy || running || !selected" @click="start">{{ t('imagesDownload') }}</button><button v-if="running" class="button" :disabled="busy || ['installing', 'cancelling'].includes(data.phase)" @click="execute('imageCancel')">{{ t('imagesCancel') }}</button></div>
      <div class="images-status" role="status"><p>{{ t('imagesPhases').split('|')[phases.indexOf(data.phase)] }}</p>
        <progress v-if="running" :value="percent" max="100" :aria-label="t('imagesProgress')" />
        <p v-if="data.phase === 'downloading'">{{ mb(data.received) }} / {{ data.total == null ? t('imagesUnknownSize') : mb(data.total) }}</p>
        <p v-if="data.phase === 'extracting'">{{ data.files }} / {{ data.fileCount }}</p>
        <p v-if="data.phase === 'complete'" class="field-help">{{ t('imagesRefreshHint') }}</p>
      </div>
      <p v-if="data.error" role="alert">{{ t(hasText(data.error) ? data.error : 'imagesDownloadFailed') }}</p>
    </template>
    <button v-else class="button" :disabled="busy" @click="execute('imageLibrary')">{{ t('imagesLoadState') }}</button>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="t('imagesDownload')" @cancel.prevent="cancel"><h2>{{ t('imagesDownload') }}</h2><p>{{ selected?.url }}</p><p>{{ t('imagesConfirm') }}</p><div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('imagesDownload') }}</button></div></dialog>
  </section>
</template>
<style scoped>
.image-library-tools { padding: 1.25rem; margin-block: 2rem; border: 1px solid var(--line); border-radius: 1rem; background: var(--surface); }
label { display: grid; gap: .4rem; min-width: 0; }
select { box-sizing: border-box; max-width: 100%; min-width: 0; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; padding: .6rem; font: inherit; }
.images-source-url, dialog p { overflow-wrap: anywhere; }
.images-source-url { opacity: .7; font-size: .8rem; }
.form-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; margin-block: 1rem; }
progress { width: 100%; accent-color: var(--green); }
.images-status p { margin-block: .4rem; }
</style>
