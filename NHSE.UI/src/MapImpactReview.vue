<script setup lang="ts">
import { t, hasText } from './i18n';
import type { GridPastePreview } from './bridge';
defineProps<{ preview: GridPastePreview | null; loading: boolean }>();
function errorText(key?: string | null) { return key && hasText(key) ? t(key) : t('invalidRequest'); }
</script>
<template>
  <div class="map-impact-review" :aria-busy="loading" role="status" aria-live="polite">
    <p v-if="loading">{{ t('mapPasteChecking') }}</p>
    <p v-else-if="preview && !preview.valid" role="alert">{{ errorText(preview.error) }}</p>
    <template v-else-if="preview">
      <p>{{ t('mapBatchImpact').replace('{0}', String(preview.placements)).replace('{1}', String(preview.changedCells)).replace('{2}', String(preview.replacedItems)) }}</p>
      <p v-if="!preview.changedCells">{{ t('mapPasteUnchanged') }}</p>
    </template>
  </div>
</template>
<style scoped>
.map-impact-review { border-inline-start: 3px solid var(--green); padding-inline-start: .8rem; margin-block: 1rem; }
p { line-height: 1.6; overflow-wrap: anywhere; }
[role=alert] { color: var(--danger, #b44032); }
</style>
