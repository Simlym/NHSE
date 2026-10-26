<script setup lang="ts">
import type { IslandRecordField } from './bridge';
import { islandRecordLabel } from './islandRecordLabels';
import { t } from './i18n';
defineProps<{ fields: IslandRecordField[]; values: Record<string, string>; busy: boolean }>();
</script>
<template>
  <div class="record-fields">
    <div v-for="field in fields" :key="field.key" class="record-field" :data-key="field.key">
      <label :for="`record-field-${field.key}`">{{ islandRecordLabel(field.key) }}</label>
      <select :id="`record-field-${field.key}`" v-if="field.type === 'boolean'" v-model="values[field.key]" :disabled="busy"><option value="False">{{ t('mapBooleanNames').split('|')[0] }}</option><option value="True">{{ t('mapBooleanNames').split('|')[1] }}</option></select>
      <template v-else-if="field.choices">
        <select :id="`record-field-${field.key}`" v-model="values[field.key]" :disabled="busy"><option v-if="!field.choices.some((_, i) => String(i) === values[field.key])" :value="values[field.key]">#{{ values[field.key] }}</option><option v-for="(name, i) in field.choices" :key="i" :value="String(i)">{{ i }} · {{ i === 0 ? t('empty') : name }}</option></select>
        <details><summary>{{ t('recordRawId') }}</summary><input v-model="values[field.key]" :aria-label="`${islandRecordLabel(field.key)} · ${t('recordRawId')}`" inputmode="numeric" :disabled="busy" /></details>
      </template>
      <textarea :id="`record-field-${field.key}`" v-else-if="field.key === 'Body' || field.type === 'hex'" v-model="values[field.key]" :rows="field.type === 'hex' ? 3 : 7" :maxlength="field.length" :spellcheck="field.type !== 'hex'" :disabled="busy" />
      <input :id="`record-field-${field.key}`" v-else v-model="values[field.key]" type="text" :inputmode="field.type === 'number' ? 'numeric' : field.type === 'float' ? 'decimal' : 'text'" :placeholder="field.type === 'date' ? 'YYYY-MM-DD' : undefined" :maxlength="field.type === 'text' ? field.length : undefined" :disabled="busy" />
      <small v-if="field.group === 'advanced' || field.group === 'board' || field.group === 'handwriting'">{{ field.key }}<template v-if="field.type === 'number'"> · {{ field.min }} … {{ field.max }}</template></small>
    </div>
  </div>
</template>
<style scoped>
.record-fields { display: grid; gap: .85rem; min-width: 0; }
.record-field { display: grid; gap: .4rem; min-width: 0; }
input, select, textarea { box-sizing: border-box; width: 100%; min-width: 0; max-width: 100%; padding: .65rem; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; font: inherit; }
textarea { overflow-wrap: anywhere; }
small { opacity: .7; overflow-wrap: anywhere; }
</style>
