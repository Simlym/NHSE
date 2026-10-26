<script setup lang="ts">
import { nextTick, ref, shallowRef } from 'vue';
import type { UtilityHashes, Player } from './bridge';
import { t } from './i18n';
const props = defineProps<{ players: Player[]; hashes: UtilityHashes | null; busy: boolean; execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean> }>();
const player = ref(props.players[0]?.index ?? 0), dialog = ref<HTMLDialogElement | null>(null);
const confirmation = shallowRef(false);
async function importFiles() { confirmation.value = true; await nextTick(); dialog.value?.showModal(); }
function cancel() { dialog.value?.close(); confirmation.value = false; }
async function confirm() { cancel(); await props.execute('utilityImportDecrypted'); }
</script>
<template>
  <section class="utility-tools">
    <h2>{{ t('utilityTitle') }}</h2><p class="field-help">{{ t('utilityExportHint') }}</p>
    <div class="form-actions"><button class="button" :disabled="busy" @click="execute('verifyHashes')">{{ t('utilityVerifyHashes') }}</button><button class="button" :disabled="busy" @click="execute('utilityExportDecrypted')">{{ t('utilityExportDecrypted') }}</button><button class="button" :disabled="busy" @click="importFiles">{{ t('utilityImportDecrypted') }}</button></div>
    <section v-if="hashes" class="utility-hashes" role="status">
      <p>{{ t(hashes.issues.length ? 'utilityHashesInvalid' : 'utilityHashesValid') }} · {{ hashes.issues.length }}</p>
      <p v-if="hashes.dirty" class="field-help">{{ t('utilityHashesDirty') }}</p>
      <ul v-if="hashes.issues.length"><li v-for="(issue, i) in hashes.issues" :key="i">{{ issue.file }} · {{ issue.region }}</li></ul>
    </section>
    <div class="utility-photo"><label>{{ t('player') }}<select v-model.number="player" :disabled="busy"><option v-for="p in players" :key="p.index" :value="p.index">{{ p.name }}</option></select></label><button class="button" :disabled="busy || !players.length" @click="execute('utilityExportPhoto', { player })">{{ t('utilityExportPhoto') }}</button></div>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="t('utilityImportDecrypted')" @cancel.prevent="cancel"><h2>{{ t('utilityImportDecrypted') }}</h2><p>{{ t('utilityImportHint') }}</p><div class="form-actions"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('utilityChooseFiles') }}</button></div></dialog>
  </section>
</template>
<style scoped>
.utility-tools { padding: 1.25rem; margin-bottom: 2rem; border: 1px solid var(--line); border-radius: 1rem; background: var(--surface); }
.form-actions, .utility-photo { display: flex; flex-wrap: wrap; gap: .75rem; margin-block: 1rem; align-items: end; }
label { display: grid; gap: .4rem; min-width: 0; }
select { max-width: 100%; min-width: 0; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; padding: .6rem; font: inherit; }
.utility-hashes { overflow-wrap: anywhere; }
.utility-hashes ul { max-height: 220px; overflow: auto; }
</style>
