<script setup lang="ts">
import { t } from './i18n';
import type { HousingField } from './bridge';
import { housingLabel } from './housingLabels';
const props = defineProps<{ fields: HousingField[]; busy: boolean }>();
const model = defineModel<Record<string, number | boolean | string>>({ required: true });
function options(field: HousingField) {
  if (field.key === 'SpecialtyFlower' || field.key === 'SisterFlower')
    return field.options?.map((o) => ({ ...o, name: t('housingFlowers').split('|')[o.id] ?? o.name }));
  return field.options;
}
</script>
<template>
  <div class="housing-fields">
    <label
      v-for="field in props.fields"
      :key="field.key"
      :class="{ 'housing-checkbox': field.type === 'boolean' }"
    >
      <input v-if="field.type === 'boolean'" v-model="model[field.key]" type="checkbox" :disabled="busy" />
      <span>{{ housingLabel(field.key) }}</span>
      <select
        v-if="field.type === 'number' && field.options?.length"
        v-model.number="model[field.key]"
        :disabled="busy"
      >
        <option v-if="!field.options.some((o) => o.id === model[field.key])" :value="model[field.key]">
          {{ model[field.key] }}
        </option>
        <option v-for="option in options(field)" :key="option.id" :value="option.id">
          {{ option.name }}
        </option>
      </select>
      <input
        v-else-if="field.type === 'number'"
        v-model.number="model[field.key]"
        type="number"
        :min="field.min"
        :max="field.max"
        step="1"
        required
        :disabled="busy"
      />
      <input
        v-else-if="field.type === 'date'"
        v-model="model[field.key]"
        type="text"
        pattern="[0-9]{4}-[0-9]{2}-[0-9]{2}"
        placeholder="YYYY-MM-DD"
        :disabled="busy"
      />
    </label>
  </div>
</template>
<style scoped>
.housing-fields {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(220px, 100%), 1fr));
  gap: 1rem;
}
label {
  display: grid;
  gap: 0.5rem;
  min-width: 0;
}
label.housing-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
input[type='checkbox'] {
  width: auto;
  flex: 0 0 auto;
}
input,
select {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  padding: 0.65rem;
  border: 1px solid var(--line-strong);
  border-radius: 0.5rem;
  background: var(--surface);
  color: inherit;
  font: inherit;
}
</style>
