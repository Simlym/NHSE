<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { Check, Users, ArrowRight } from '@lucide/vue';
import { t } from './i18n';
import type { Resident } from './bridge';
import VillagerPicker from './VillagerPicker.vue';

const props = defineProps<{
  residents: Resident[];
  busy: boolean;
  execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean>;
}>();
const target = ref(props.residents.find((v) => !v.empty)?.index ?? 0);
const resident = computed(() => props.residents.find((v) => v.index === target.value));
const names = computed(() => t('villagerPersonalityNames').split('|'));
const species = computed(() => t('villagerSpeciesNames').split('|'));
const form = ref<HTMLFormElement | null>(null);
const fields = ref({ personality: 0, catchphrase: '', movingOut: false });
const pending = computed(
  () =>
    resident.value &&
    Object.entries(fields.value).some(([k, v]) => v !== resident.value![k as keyof Resident]),
);
const pickerOpen = ref(false);
const dialog = ref<HTMLDialogElement | null>(null);
const pickerContext = computed(() => ({ index: target.value, residents: props.residents }));
watch(
  () =>
    JSON.stringify(
      resident.value && {
        index: resident.value.index,
        id: resident.value.id,
        personality: resident.value.personality,
        catchphrase: resident.value.catchphrase,
        movingOut: resident.value.movingOut,
      },
    ),
  () => {
    const v = resident.value;
    if (v) fields.value = { personality: v.personality, catchphrase: v.catchphrase, movingOut: v.movingOut };
  },
  { immediate: true },
);
async function applyPending() {
  if (!pending.value) return true;
  if (!form.value?.reportValidity()) return false;
  return await props.execute('setResident', { index: target.value, ...fields.value });
}
async function select(index: number) {
  if (await applyPending()) target.value = index;
}
async function showPicker() {
  if (!(await applyPending())) return;
  pickerOpen.value = true;
  await nextTick();
  dialog.value?.showModal();
}
function closePicker() {
  dialog.value?.close();
  pickerOpen.value = false;
}
async function pick(choice: { index: number; id?: string }) {
  closePicker();
  if (choice.id && !(await props.execute('replaceResident', choice))) return;
  target.value = choice.index;
}
async function advanced() {
  if (await applyPending()) await props.execute('legacy', { section: 'villagers', resident: target.value });
}
defineExpose({ applyPending });
</script>

<template>
  <section class="residents-page">
    <p class="page-description">{{ t('residentEditHint') }}</p>
    <div class="resident-strip" :aria-label="t('currentResidents')" role="group">
      <button
        v-for="r in residents"
        :key="r.index"
        class="resident-chip"
        :class="{ active: target === r.index }"
        :aria-pressed="target === r.index"
        :disabled="busy || r.empty"
        @click="select(r.index)"
      >
        <img v-if="r.image" :src="r.image" alt="" /><Users v-else :size="26" />
        <span
          >{{ r.empty ? t('emptyResident') : r.name }}<small>{{ t('slot') }} {{ r.index + 1 }}</small></span
        >
        <Check v-if="target === r.index" :size="16" />
      </button>
    </div>
    <div v-if="resident && !resident.empty" class="resident-editor">
      <aside class="resident-profile">
        <img v-if="resident.image" :src="resident.image" alt="" /><Users v-else :size="80" />
        <h2>{{ resident.name }}</h2>
        <p>{{ species[resident.species] }} · {{ names[resident.personality] }}</p>
        <small>{{ resident.id }}</small>
        <button class="button primary" :disabled="busy || !resident.canReplace" @click="showPicker">
          {{ t('villagerCatalogTitle') }}
        </button>
        <small v-if="!resident.canReplace" class="field-help">{{ t('oldResidentFormat') }}</small>
      </aside>
      <form ref="form" class="balance-form resident-form" @submit.prevent="applyPending">
        <div class="form-section-title">
          <Users :size="19" />
          <h2>{{ t('residentProfile') }}</h2>
        </div>
        <label
          ><span>{{ t('villagerPersonality') }}</span
          ><select
            v-model.number="fields.personality"
            :aria-label="t('villagerPersonality')"
            :disabled="busy"
          >
            <option v-for="(name, index) in names" :key="index" :value="index">{{ name }}</option>
          </select></label
        >
        <label
          ><span>{{ t('catchphrase') }}</span
          ><input
            v-model="fields.catchphrase"
            :aria-label="t('catchphrase')"
            aria-describedby="resident-phrase-help"
            maxlength="24"
            :disabled="busy"
          />
          <small id="resident-phrase-help" class="field-help">{{ t('catchphraseHint') }}</small>
          <button
            type="button"
            class="text-button"
            :disabled="busy"
            @click="fields.catchphrase = resident.defaultCatchphrase"
          >
            {{ t('restorePhrase') }}
          </button>
        </label>
        <label class="moving-out-field"
          ><input v-model="fields.movingOut" type="checkbox" :disabled="busy" /><span>{{
            t('movingOut')
          }}</span></label
        >
        <p class="field-help resident-warning">{{ t('movingOutHint') }}</p>
        <div class="form-actions">
          <button class="button primary" :disabled="busy || !pending">{{ t('apply') }}</button>
          <button
            type="button"
            class="button"
            :disabled="busy"
            @click="
              applyPending().then((ok) => ok && execute('tool', { tool: 'residentHouses', resident: target }))
            "
          >
            {{ t('residentHouse') }}
          </button>
          <button type="button" class="button" :disabled="busy" @click="advanced">
            {{ t('advanced') }}<ArrowRight :size="16" />
          </button>
        </div>
      </form>
    </div>
    <p v-else class="empty-result">{{ t('noResidents') }}</p>
    <dialog
      v-if="pickerOpen"
      ref="dialog"
      class="resident-picker-dialog"
      :aria-label="t('villagerCatalogTitle')"
      @cancel.prevent="closePicker"
    >
      <VillagerPicker :context="pickerContext" embedded @pick="pick" @cancel="closePicker" />
    </dialog>
  </section>
</template>
