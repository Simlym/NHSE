<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Map, ArrowRight, Palette } from '@lucide/vue';
import TurnipsEditor from './TurnipsEditor.vue';
import type { TurnipSettings, IslandSettings } from './bridge';
import { t } from './i18n';
const props = defineProps<{
  settings: IslandSettings;
  turnips: TurnipSettings;
  busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>;
}>();
const turnipsEditor = ref<InstanceType<typeof TurnipsEditor> | null>(null);
const fields = ref({ ...props.settings });
const form = ref<HTMLFormElement | null>(null);
const colors = ['#5796c4', '#e8bf46', '#dd8849', '#5a9f76'];
const colorNames = computed(() => t('airportColors').split('|'));
const hemispheres = computed(() => t('hemisphereNames').split('|'));
const mapTools = [
  'terrain',
  'playerHouses',
  'residentHouses',
  'campsite',
  'fruitFlowers',
  'recycle',
  'turnips',
];
const designTools = ['patterns', 'proDesigns', 'flag', 'tailor'];
const mapNames = computed(() => t('islandToolNames').split('|'));
const designNames = computed(() => t('designToolNames').split('|'));
const pending = computed(() =>
  Object.entries(fields.value).some(([key, value]) => value !== props.settings[key as keyof IslandSettings]),
);
watch(
  () => JSON.stringify(props.settings),
  () => (fields.value = { ...props.settings }),
);
async function applyIsland() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  return await props.execute('setIsland', { ...fields.value });
}
async function applyPending() {
  if (!(await applyIsland())) return false;
  return (await turnipsEditor.value?.applyPending()) ?? true;
}
async function openTool(tool: string) {
  if (tool === 'turnips') {
    document.getElementById('turnips-editor')?.scrollIntoView({ block: 'start' });
    return;
  }
  if (await applyPending()) await props.execute('tool', { tool });
}
defineExpose({ applyPending, hasPending: () => pending.value || !!turnipsEditor.value?.hasPending(), resetDraft: () => { fields.value = { ...props.settings }; turnipsEditor.value?.resetDraft(); } });
</script>

<template>
  <section class="island-page">
    <p class="page-description">{{ t('islandSettingsHint') }}</p>
    <form ref="form" class="balance-form island-form" @submit.prevent="applyIsland">
      <div class="form-section-title">
        <Map :size="19" />
        <h2>{{ t('islandBasics') }}</h2>
      </div>
      <fieldset class="airport-field">
        <legend>{{ t('airportColor') }}</legend>
        <div class="airport-colors">
          <label
            v-for="(color, index) in colors"
            :key="color"
            :class="{ active: fields.airportColor === index }"
          >
            <input
              v-model.number="fields.airportColor"
              type="radio"
              :value="index"
              :disabled="busy"
              name="airport-color"
            />
            <span :style="{ background: color }" class="airport-swatch"></span>{{ colorNames[index] }}
          </label>
        </div>
      </fieldset>
      <label
        ><span>{{ t('islandHemisphere') }}</span
        ><select v-model.number="fields.hemisphere" :disabled="busy">
          <option v-for="(name, index) in hemispheres" :key="index" :value="index">
            {{ name }}
          </option></select
        ><small class="field-help">{{ t('hemisphereHint') }}</small></label
      >
      <div class="form-section-title">
        <h2>{{ t('weatherSettings') }}</h2>
      </div>
      <p class="field-help world-help">{{ t('weatherSeedHint') }}</p>
      <label
        ><span>{{ t('islandWeatherSeed') }}</span
        ><input
          v-model.number="fields.weatherSeed"
          type="number"
          min="0"
          max="4294967295"
          step="1"
          required
          :disabled="busy"
      /></label>
      <label
        ><span>{{ t('tourHemisphere') }}</span
        ><select v-model.number="fields.tourHemisphere" :disabled="busy">
          <option v-for="(name, index) in hemispheres" :key="index" :value="index">
            {{ name }}
          </option>
        </select></label
      >
      <label
        ><span>{{ t('tourWeatherSeed') }}</span
        ><input
          v-model.number="fields.tourWeatherSeed"
          type="number"
          min="0"
          max="4294967295"
          step="1"
          required
          :disabled="busy"
      /></label>
      <div class="form-actions">
        <button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
      </div>
    </form>
    <TurnipsEditor ref="turnipsEditor" :settings="turnips" :busy="busy" :execute="execute" />
    <div class="island-tools">
      <h2 class="world-tools-title">{{ t('mapEditor') }}</h2>
      <p class="page-description">{{ t('specializedHint') }}</p>
      <div class="world-shortcuts">
        <button
          v-for="(tool, index) in mapTools"
          :key="tool"
          class="task-card"
          :disabled="busy"
          @click="openTool(tool)"
        >
          <Map :size="24" /><strong>{{ mapNames[index] }}</strong
          ><ArrowRight :size="18" />
        </button>
      </div>
      <div class="form-actions"><button v-for="tool in ['bulletin', 'visitors']" :key="tool" class="button" :disabled="busy" @click="openTool(tool)">{{ t(tool as 'bulletin' | 'visitors') }}</button></div>
      <button class="button" :disabled="busy" @click="openTool('museum')">{{ t('museum') }}</button>
      <h2 class="world-tools-title">{{ t('designs') }}</h2>
      <div class="world-shortcuts">
        <button
          v-for="(tool, index) in designTools"
          :key="tool"
          class="task-card"
          :disabled="busy"
          @click="openTool(tool)"
        >
          <Palette :size="24" /><strong>{{ designNames[index] }}</strong
          ><ArrowRight :size="18" />
        </button>
      </div>
    </div>
  </section>
</template>
