<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { t } from './i18n';
import type { MiscProfile } from './bridge';
const props = defineProps<{ player: number; data: MiscProfile; busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean> }>();
const values = () => ({ month: props.data.month, day: props.data.day, madeVillage: props.data.madeVillage,
  timestamp: props.data.timestamp, fruit: props.data.fruit });
const fields = ref(values());
const form = ref<HTMLFormElement | null>(null);
const validation = ref('');
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(values()));
watch(() => JSON.stringify([props.player, values()]), () => { fields.value = values(); validation.value = ''; });
async function applyPending() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  const { month, day, timestamp } = fields.value;
  const birthdayChanged = month !== props.data.month || day !== props.data.day;
  if (birthdayChanged && (month < 1 || month > 12 || day < 1 || day > new Date(Date.UTC(2000, month, 0)).getUTCDate())) {
    validation.value = t('miscInvalidBirthday'); return false;
  }
  if (timestamp !== props.data.timestamp && timestamp !== '') {
    const parsed = new Date(`${timestamp}T00:00:00Z`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(timestamp) || Number.isNaN(parsed.getTime()) ||
      parsed.toISOString().slice(0, 10) !== timestamp || timestamp.startsWith('0000')) {
      validation.value = t('miscInvalidDate'); return false;
    }
  }
  validation.value = '';
  return await props.execute('setMiscProfile', { player: props.player, ...fields.value });
}
defineExpose({ applyPending });
</script>
<template>
  <form ref="form" class="misc-profile player-card" aria-labelledby="misc-profile-title" @submit.prevent="applyPending">
    <h2 id="misc-profile-title">{{ t('miscProfile') }}</h2>
    <p class="field-help">{{ t('miscProfileHint') }}</p>
    <div class="misc-fields">
      <label>{{ t('miscBirthMonth') }}<input v-model.number="fields.month" type="number" min="0" max="255" required :disabled="busy" /></label>
      <label>{{ t('miscBirthDay') }}<input v-model.number="fields.day" type="number" min="0" max="255" required :disabled="busy" /></label>
      <label>{{ t('miscDate') }}<input v-model="fields.timestamp" type="text" placeholder="YYYY-MM-DD" :disabled="busy" /></label>
      <label>{{ t('miscFruit') }}<select v-model.number="fields.fruit" :disabled="busy"><option v-if="!data.fruits.some(f => f.id === fields.fruit)" :value="fields.fruit">#{{ fields.fruit }}</option><option v-for="fruit in data.fruits" :key="fruit.id" :value="fruit.id">{{ fruit.name }}</option></select></label>
      <label class="misc-checkbox"><input v-model="fields.madeVillage" type="checkbox" :disabled="busy" />{{ t('miscMadeVillage') }}</label>
    </div>
    <p v-if="validation" role="alert">{{ validation }}</p>
    <button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
  </form>
</template>
<style scoped>
.misc-profile { margin-top: 1.5rem; padding: 1.25rem; border: 1px solid var(--line); border-radius: 1rem; background: var(--surface); }
.misc-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem; margin-block: 1rem; }
label { display: grid; gap: .5rem; min-width: 0; }
input, select { box-sizing: border-box; width: 100%; min-width: 0; padding: .65rem; font: inherit; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
.misc-checkbox { display: flex; align-items: center; }
.misc-checkbox input { width: auto; }
@media(max-width: 600px) { .misc-fields { grid-template-columns: minmax(0, 1fr); } }
</style>
