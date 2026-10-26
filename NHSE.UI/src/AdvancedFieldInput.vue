<script setup lang="ts">
import type { AdvancedField } from './bridge';
import { t } from './i18n';

defineProps<{ field: AdvancedField; label: string; modelValue: string; busy: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: string] }>();
function update(event: Event) { emit('update:modelValue', (event.target as HTMLInputElement).value); }
</script>

<template>
  <label class="advanced-field" :class="{ 'wide-field': field.key === 'Message' }" :data-field-key="field.key">
    {{ label }}
    <small>{{ field.key }}<template v-if="field.type === 'number'"> · {{ field.min }} … {{ field.max }}</template></small>
    <select v-if="field.type === 'boolean'" :value="modelValue" :disabled="busy" @change="update">
      <option value="False">{{ t('mapBooleanNames').split('|')[0] }}</option><option value="True">{{ t('mapBooleanNames').split('|')[1] }}</option>
    </select>
    <textarea v-else-if="field.key === 'Message'" :value="modelValue" :maxlength="field.length" rows="7" :disabled="busy" @input="update" />
    <input v-else :value="modelValue" type="text" :inputmode="field.type === 'number' ? 'numeric' : 'text'" :pattern="field.type === 'number' ? '-?[0-9]+' : undefined" :required="field.type === 'number'" :maxlength="field.type === 'text' ? field.length : undefined" :disabled="busy" @input="update" />
    <span v-if="field.type === 'text'" class="character-count">{{ modelValue.length }} / {{ field.length }}</span>
  </label>
</template>

<style scoped>
.advanced-field { display: grid; gap: .3rem; min-width: 0; align-content: start; }
.wide-field { grid-column: 1 / -1; }
small { font-size: .75rem; overflow-wrap: anywhere; opacity: .75; }
input, select, textarea { box-sizing: border-box; max-width: 100%; min-width: 0; padding: .65rem; width: 100%; font: inherit; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
.character-count { color: var(--muted); text-align: end; font-size: .75rem; }
</style>
