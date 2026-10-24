<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { TurnipSettings } from './bridge';
import { t } from './i18n';
const props = defineProps<{
  settings: TurnipSettings;
  busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>;
}>();
const copy = () => ({ ...props.settings, sellPrices: [...props.settings.sellPrices] });
const fields = ref(copy());
const form = ref<HTMLFormElement | null>(null);
const days = computed(() => t('turnipDays').split('|'));
const patterns = computed(() => t('turnipPatterns').split('|'));
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(props.settings));
watch(
  () => JSON.stringify(props.settings),
  () => {
    fields.value = copy();
  },
);
async function applyPending() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  return await props.execute('setTurnips', { ...fields.value, sellPrices: [...fields.value.sellPrices] });
}
defineExpose({ applyPending });
</script>

<template>
  <section id="turnips-editor" class="turnips-editor">
    <h2>{{ t('turnips') }}</h2>
    <p class="page-description">{{ t('turnipHint') }}</p>
    <form ref="form" @submit.prevent="applyPending">
      <div class="balance-form">
        <label
          ><span>{{ t('turnipBuy') }}</span
          ><input
            v-model.number="fields.buyPrice"
            type="number"
            min="0"
            max="4294967295"
            step="1"
            required
            :disabled="busy"
        /></label>
        <label
          ><span>{{ t('turnipPattern') }}</span
          ><select v-model.number="fields.pattern" :disabled="busy">
            <option v-for="(name, index) in patterns" :key="index" :value="index">
              {{ name }}
            </option>
          </select></label
        >
        <label
          ><span>{{ t('turnipFever') }}</span
          ><input
            v-model.number="fields.feverStart"
            type="number"
            min="0"
            max="4294967295"
            step="1"
            required
            :disabled="busy"
          /><small class="field-help">{{ t('turnipFeverHint') }}</small></label
        >
      </div>
      <div class="turnip-table-wrap">
        <table class="turnip-table">
          <thead>
            <tr>
              <th scope="col">{{ t('turnipDay') }}</th>
              <th scope="col">{{ t('turnipAM') }}</th>
              <th scope="col">{{ t('turnipPM') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(day, index) in days" :key="index">
              <th scope="row">{{ day }}</th>
              <td v-for="half in [0, 1]" :key="half">
                <input
                  v-model.number="fields.sellPrices[index * 2 + half]"
                  :aria-label="`${day} ${t(half === 0 ? 'turnipAM' : 'turnipPM')}`"
                  type="number"
                  min="0"
                  max="4294967295"
                  step="1"
                  required
                  :disabled="busy"
                />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="form-actions">
        <button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
      </div>
    </form>
  </section>
</template>

<style scoped>
.turnips-editor {
  margin-block: 2rem;
  scroll-margin-top: 1rem;
}
.turnip-table-wrap {
  overflow-x: auto;
  margin-block: 1rem;
}
.turnip-table {
  width: 100%;
  border-collapse: collapse;
  text-align: start;
}
th,
td {
  padding: 0.6rem 0.35rem;
  text-align: start;
}
input,
select {
  width: 100%;
  min-width: 0;
  padding: 0.65rem;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
  background: var(--surface, #fff);
  color: inherit;
  font: inherit;
}
.turnip-table input {
  box-sizing: border-box;
}
.turnip-table th {
  width: 24%;
}
</style>
