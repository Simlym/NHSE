<script setup lang="ts">
import { t, hasText } from './i18n';
import type { GridPastePreview } from './bridge';
defineProps<{ preview: GridPastePreview | null; loading: boolean; busy: boolean; outside: number; pending: boolean; confirm: () => Promise<void>; cancel: () => void; locateConflict?: () => void }>();
function errorText(error?: string | null) { return error && hasText(error) ? t(error) : t('invalidRequest'); }
</script>
<template>
  <section class="map-paste-preview" :aria-label="t('mapPastePreview')" :aria-busy="loading">
    <h3>{{ t('mapPastePreview') }}</h3>
    <div class="preview-status" aria-live="polite" role="status">
      <p v-if="loading" class="preview-checking">{{ t('mapPasteChecking') }}</p>
      <div :style="{ visibility: loading ? 'hidden' : undefined }">
        <p v-if="preview && !preview.valid" class="preview-error">{{ errorText(preview.error) }}</p>
        <template v-else-if="preview">
          <p>{{ t('mapPasteImpact').replace('{0}', String(preview.placements)).replace('{1}', String(preview.changedCells)).replace('{2}', String(preview.replacedItems)) }}</p>
          <p v-if="!preview.changedCells">{{ t('mapPasteUnchanged') }}</p>
          <p v-if="outside">{{ t('mapPasteOutside').replace('{0}', String(outside)) }}</p>
        </template>
      </div>
    </div>
    <details class="preview-help"><summary>{{ t('gridHelp') }}</summary><p>{{ t('mapPastePreviewHint') }}</p><p>{{ t('mapPasteColorHint') }}</p></details>
    <p v-if="pending" class="preview-help">{{ t('mapPasteDraftHint') }}</p>
    <div class="preview-actions">
      <button v-if="!loading && preview?.conflicts?.length && locateConflict" class="button" @click="locateConflict">{{ t('mapLocateConflict') }} · {{ preview.conflicts.length }}</button>
      <button class="button primary" :disabled="busy || loading || !preview?.valid || !preview.changedCells" @click="confirm">{{ t('mapPasteConfirm') }} <kbd>Enter</kbd></button>
      <button class="button" :disabled="busy" @click="cancel">{{ t('cancel') }} <kbd>Esc</kbd></button>
    </div>
  </section>
</template>
<style scoped>
.map-paste-preview { padding: 1rem; border: 1px solid var(--line-strong); border-radius: .75rem; background: var(--surface); min-width: 0; }
h3 { margin: 0 0 .6rem; font-size: .95rem; }
p { line-height: 1.6; margin: .4rem 0; overflow-wrap: anywhere; }
.preview-help { color: var(--muted); font-size: .8rem; }
summary { cursor: pointer; padding-block: .3rem; }
.preview-error { color: var(--danger, #b44032); }
.preview-status { position: relative; min-height: 1.6em; }
.preview-checking { position: absolute; inset: 0; }
.preview-actions { position: sticky; bottom: 0; display: flex; flex-wrap: wrap; gap: .5rem; margin-top: .75rem; background: var(--surface); padding-block: .35rem; }
kbd { margin-inline-start: .3rem; font: inherit; font-size: .75em; opacity: .7; }
</style>
