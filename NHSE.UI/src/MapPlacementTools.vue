<script setup lang="ts">
import { t, hasText } from './i18n';
import type { GridPastePreview } from './bridge';
defineProps<{ mode: 'select' | 'move' | 'stamp'; busy: boolean; pending: boolean; checking: boolean; preview: GridPastePreview | null; rotation: number; setMode: (mode: 'select' | 'move' | 'stamp') => void; pickItem: () => void; useCurrentItem: () => void; cancel: () => void }>();
function errorText(key?: string | null) { return key && hasText(key) ? t(key) : t('invalidRequest'); }
</script>
<template>
  <section class="map-placement-tools" :aria-label="t('mapDirectActions')">
    <h3>{{ t('mapDirectActions') }}</h3>
    <p v-if="pending" class="field-help">{{ t('mapDirectDraftHint') }}</p>
    <div class="placement-mode-actions">
      <button :aria-pressed="mode === 'move'" :disabled="busy || pending" @click="setMode('move')">{{ t('mapMoveObjects') }}</button>
      <button :aria-pressed="mode === 'stamp'" :disabled="busy || pending" @click="setMode('stamp')">{{ t('mapStampObjects') }}</button>
      <button v-if="mode !== 'select'" :disabled="busy" @click="cancel">{{ t('cancel') }} · Esc</button>
    </div>
    <template v-if="mode === 'stamp'">
      <div class="placement-mode-actions"><button :disabled="busy" @click="pickItem">{{ t('mapPickItem') }}</button><button :disabled="busy" @click="useCurrentItem">{{ t('mapUseCurrentItem') }}</button></div>
      <p>{{ t('mapStampHint') }} · R {{ rotation * 90 }}°</p>
    </template>
    <p v-else-if="mode === 'move'">{{ t('mapMoveHint') }}</p>
    <div role="status" aria-live="polite"><p v-if="checking">{{ t('mapPasteChecking') }}</p><p v-else-if="preview && !preview.valid">{{ errorText(preview.error) }}</p><p v-else-if="preview">{{ t('mapPasteImpact').replace('{0}', String(preview.placements)).replace('{1}', String(preview.changedCells)).replace('{2}', String(preview.replacedItems)) }}</p></div>
  </section>
</template>
<style scoped>
.map-placement-tools { padding: 14px; border-top: 1px solid var(--line); font-size: 12px; }
h3 { margin: 0 0 .6rem; font-size: 12px; }
p { line-height: 1.6; color: var(--muted); }
.placement-mode-actions { display: flex; flex-wrap: wrap; gap: .5rem; margin-block: .5rem; }
button { padding: .6rem; border: 1px solid var(--line-strong); border-radius: .5rem; background: var(--surface); color: inherit; font: inherit; }
button[aria-pressed=true] { color: var(--green); border-color: var(--green); background: var(--hover); }
</style>
