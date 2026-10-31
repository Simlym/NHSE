<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { CalendarDays, Check } from '@lucide/vue';
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
defineExpose({ applyPending, hasPending: () => pending.value, resetDraft: () => { fields.value = values(); validation.value = ''; } });
</script>
<template>
  <form ref="form" class="misc-profile player-card" aria-labelledby="misc-profile-title" @submit.prevent="applyPending">
    <div class="form-section-title"><CalendarDays :size="19" /><h2 id="misc-profile-title">{{ t('miscProfile') }}</h2></div>
    <div class="misc-fields">
      <label>{{ t('miscBirthMonth') }}<input v-model.number="fields.month" type="number" min="0" max="255" required :disabled="busy" /></label>
      <label>{{ t('miscBirthDay') }}<input v-model.number="fields.day" type="number" min="0" max="255" required :disabled="busy" /></label>
      <label>{{ t('miscDate') }}<input v-model="fields.timestamp" type="text" placeholder="YYYY-MM-DD" :disabled="busy" /></label>
      <label>{{ t('miscFruit') }}<select v-model.number="fields.fruit" :disabled="busy"><option v-if="!data.fruits.some(f => f.id === fields.fruit)" :value="fields.fruit">#{{ fields.fruit }}</option><option v-for="fruit in data.fruits" :key="fruit.id" :value="fruit.id">{{ fruit.name }}</option></select></label>
      <label class="misc-checkbox"><input v-model="fields.madeVillage" type="checkbox" :disabled="busy" />{{ t('miscMadeVillage') }}</label>
    </div>
    <p v-if="validation" class="misc-validation" role="alert">{{ validation }}</p>
    <div class="misc-actions">
      <p class="field-help">{{ t('miscProfileHint') }}</p>
      <button class="button primary" :disabled="busy || !pending">{{ t('apply') }}<Check :size="17" /></button>
    </div>
  </form>
</template>
<style scoped>
.misc-profile { margin-top: 14px; display: flex; flex-direction: column; gap: 14px; }
.misc-fields { display: grid; grid-template-columns: 80px 80px 160px minmax(130px, 180px) minmax(140px, 1fr); gap: 14px; }
label { display: grid; align-content: start; gap: 5px; min-width: 0; font-size: 12px; font-weight: 550; }
input, select { box-sizing: border-box; width: 100%; max-width: none; min-width: 0; min-height: 36px; padding: 7px 10px; font: inherit; font-weight: 400; color: inherit; background: var(--canvas); border: 1px solid var(--line-strong); border-radius: 8px; }
.misc-checkbox { display: flex; align-items: center; align-self: end; min-height: 36px; gap: 9px; font-weight: 400; }
.misc-checkbox input { width: 16px; height: 16px; min-height: 0; margin: 0; accent-color: var(--green); }
.misc-actions { display: flex; align-items: center; justify-content: space-between; gap: 12px; border-top: 1px solid var(--line); padding-top: 10px; }
.misc-actions .field-help { margin: 0; max-width: 70ch; line-height: 1.7; }
.misc-actions .button { flex-shrink: 0; align-self: center; }
.misc-validation { margin: 0; color: var(--danger); }
@media(max-width: 1000px) { .misc-fields { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media(max-width: 560px) {
  .misc-fields { grid-template-columns: minmax(0, 1fr); }
  .misc-actions { flex-wrap: wrap; }
  .misc-actions .field-help { flex-basis: 100%; }
  .misc-actions .button { margin-left: auto; }
}
</style>
