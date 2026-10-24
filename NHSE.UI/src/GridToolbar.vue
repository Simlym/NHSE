<script setup lang="ts">
import { t } from './i18n';
import type { GridClipboardInfo } from './bridge';
const props = defineProps<{ count: number; clipboard: GridClipboardInfo | null; kind: 'items' | 'terrain'; busy: boolean; action: (name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') => Promise<void>; clear: () => void }>();
</script>
<template>
  <div class="grid-toolbar">
    <div class="grid-actions" role="group" :aria-label="t('gridActions')">
      <span role="status">{{ t('gridSelected').replace('{0}', String(count)) }}</span>
      <button type="button" :disabled="busy || !count" @click="action('copyGrid')">{{ t('gridCopy') }} <kbd>Ctrl+C</kbd></button>
      <button type="button" :disabled="busy || !count || clipboard?.kind !== kind" @click="action('pasteGrid')">{{ t('gridPaste') }} <kbd>Ctrl+V</kbd></button>
      <button type="button" :disabled="busy || !count" @click="action('delete')">{{ t('gridDelete') }} <kbd>Delete</kbd></button>
      <button type="button" :disabled="busy || !count" @click="action('rotate')">{{ t('gridRotate') }} <kbd>R</kbd></button>
      <button type="button" :disabled="busy || !count" @click="clear">{{ t('gridClearSelection') }} <kbd>Esc</kbd></button>
    </div>
    <p v-if="clipboard" class="clipboard-info">{{ t('gridClipboardInfo').replace('{0}', String(clipboard.count)).replace('{1}', `${clipboard.width} × ${clipboard.height}`) }}</p>
    <details><summary>{{ t('gridHelp') }}</summary><p>{{ t('gridSelectionHelp') }}</p><p>{{ t('gridClipboardHelp') }}</p></details>
  </div>
</template>
<style scoped>
.grid-toolbar { min-width: 0; margin-block: .75rem; }
.grid-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
button { padding: .5rem; border: 1px solid var(--line-strong); border-radius: .5rem; background: var(--surface); color: inherit; }
kbd { margin-inline-start: .3rem; font: inherit; font-size: .75em; opacity: .7; }
p { line-height: 1.5; }
summary { cursor: pointer; margin-top: .5rem; }
</style>
