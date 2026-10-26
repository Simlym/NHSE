<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { languages, t } from './i18n';
import type { WorkspacePreferences } from './bridge';
const props = defineProps<{ data: WorkspacePreferences; busy: boolean; execute: (payload: Record<string, unknown>) => Promise<boolean>; back: () => void }>();
const fields = ref({ ...props.data }), form = ref<HTMLFormElement | null>(null);
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(props.data));
watch(() => props.data, (next, old) => {
  const updated = { ...next };
  for (const key of Object.keys(next) as (keyof WorkspacePreferences)[])
    if (fields.value[key] !== old[key] && next[key] === old[key]) Object.assign(updated, { [key]: fields.value[key] });
  fields.value = updated;
});
async function applyPending() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  const success = await props.execute({ ...fields.value });
  if (success) { await nextTick(); fields.value = { ...props.data }; }
  return success;
}
function reset() { fields.value = { ...props.data }; }
defineExpose({ applyPending });
</script>
<template>
  <section class="settings-page">
    <button class="button" :disabled="busy" @click="back">← {{ t('back') }}</button><p class="page-description">{{ t('preferencesHint') }}</p>
    <form ref="form" @submit.prevent="applyPending">
      <fieldset><legend>{{ t('preferencesGeneral') }}</legend>
        <label class="checkbox"><input v-model="fields.automaticBackup" type="checkbox" :disabled="busy" @change="fields.backupPrompted = true" />{{ t('preferencesAutoBackup') }}</label>
        <label>{{ t('language') }}<select v-model="fields.language" :disabled="busy"><option v-for="[code, name] in languages" :key="code" :value="code">{{ name }}</option></select></label>
        <label>{{ t('theme') }}<select v-model="fields.theme" :disabled="busy"><option v-for="value in (['system', 'light', 'dark'] as const)" :key="value" :value="value">{{ t(value) }}</option></select></label>
        <p class="field-help">{{ t('preferencesThemeHint') }}</p>
      </fieldset>
      <fieldset><legend>{{ t('preferencesConsole') }}</legend>
        <label>{{ t('consoleHost') }}<input v-model="fields.host" maxlength="255" required :disabled="busy" /></label>
        <label>{{ t('consolePort') }}<input v-model.number="fields.port" type="number" min="1" max="65535" required :disabled="busy" /></label>
        <label>{{ t('preferencesPouchOffset') }}<input v-model="fields.pouchOffset" pattern="[0-9a-fA-F]{1,8}" maxlength="8" required :disabled="busy" spellcheck="false" /></label>
        <label>{{ t('preferencesGenericOffset') }}<input v-model="fields.genericOffset" pattern="[0-9a-fA-F]{1,8}" maxlength="8" required :disabled="busy" spellcheck="false" /></label>
      </fieldset>
      <details><summary>{{ t('recordAdvanced') }}</summary>
        <label>{{ t('preferencesRecentPath') }}<input v-model="fields.lastFilePath" maxlength="4096" :disabled="busy" /></label>
        <label class="checkbox"><input v-model="fields.backupPrompted" type="checkbox" :disabled="busy" />{{ t('preferencesBackupPrompted') }}</label>
        <label class="checkbox"><input v-model="fields.consolePrompted" type="checkbox" :disabled="busy" />{{ t('preferencesConsolePrompted') }}</label>
      </details>
      <div class="form-actions"><button class="button primary" :disabled="busy || !pending">{{ t('preferencesSave') }}</button><button type="button" class="button" :disabled="busy || !pending" @click="reset">{{ t('preferencesReset') }}</button></div>
    </form>
  </section>
</template>
<style scoped>
.settings-page { max-width: 960px; }
form, fieldset { display: grid; gap: 1rem; min-width: 0; }
fieldset { padding: 1rem; border: 1px solid var(--line); border-radius: .8rem; background: var(--surface); }
label { display: grid; gap: .4rem; min-width: 0; }
.checkbox { display: flex; align-items: center; gap: .6rem; }
input, select { box-sizing: border-box; width: 100%; min-width: 0; max-width: 100%; padding: .65rem; color: inherit; font: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
input[type=checkbox] { width: auto; }
summary { cursor: pointer; margin-bottom: 1rem; }
details label { margin-bottom: .8rem; }
.form-actions { display: flex; gap: .75rem; flex-wrap: wrap; }
</style>
