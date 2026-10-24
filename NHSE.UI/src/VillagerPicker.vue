<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { Check, ChevronLeft, ChevronRight, Search, Users, X, ArrowRight } from '@lucide/vue';
import { request, type VillagerCatalog, type VillagerEntry, type VillagerPickerContext } from './bridge';
import { hasText, t } from './i18n';

const props = defineProps<{ context: VillagerPickerContext }>();
const target = ref(props.context.index);
const query = ref('');
const species = ref(-1);
const personality = ref(-1);
const catalog = ref<VillagerCatalog | null>(null);
const selected = ref<VillagerEntry | null>(null);
const loading = ref(false);
const error = ref('');
const jump = ref(1);
const resident = computed(() => props.context.residents.find((r) => r.index === target.value));
const speciesNames = computed(() => t('villagerSpeciesNames').split('|'));
const personalityNames = computed(() => t('villagerPersonalityNames').split('|'));
const unavailable = (id: string) =>
  props.context.residents.some((r) => !r.empty && r.index !== target.value && r.id === id);
const resultPanel = ref<HTMLElement | null>(null);
let pendingPage: number | null = null;
let searchTimer: ReturnType<typeof setTimeout> | undefined;
async function search(page = 0) {
  if (loading.value) {
    pendingPage = page;
    return;
  }
  loading.value = true;
  error.value = '';
  const filters = { query: query.value, species: species.value, personality: personality.value, page };
  try {
    const result = await request<VillagerCatalog>('villagerCatalog', filters);
    if (
      filters.query === query.value &&
      filters.species === species.value &&
      filters.personality === personality.value
    ) {
      catalog.value = result;
      jump.value = result.page + 1;
      await nextTick();
      resultPanel.value?.scrollTo(0, 0);
    }
  } catch (reason) {
    const key = reason instanceof Error ? reason.message : '';
    error.value = t(hasText(key) ? key : 'operationFailed');
  } finally {
    loading.value = false;
  }
  if (pendingPage !== null) {
    const page = pendingPage;
    pendingPage = null;
    await search(page);
  }
}
async function finish(editOnly = false) {
  if (
    loading.value ||
    (!editOnly && (resident.value?.empty || !selected.value || unavailable(selected.value.id)))
  )
    return;
  loading.value = true;
  error.value = '';
  try {
    await request(editOnly ? 'pickResident' : 'pickVillager', {
      index: target.value,
      id: selected.value?.id,
    });
  } catch (reason) {
    const key = reason instanceof Error ? reason.message : '';
    error.value = t(hasText(key) ? key : 'operationFailed');
  } finally {
    loading.value = false;
  }
}
function cancel() {
  void request('cancelVillager');
}
function keyboard(e: KeyboardEvent) {
  if (e.key === 'Escape' && !loading.value) {
    e.preventDefault();
    cancel();
  }
}
watch(query, () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => void search(0), 250);
});
watch([species, personality], () => void search(0));
watch(target, () => {
  selected.value = null;
});
onMounted(() => {
  window.addEventListener('keydown', keyboard);
  void search();
});
onUnmounted(() => {
  clearTimeout(searchTimer);
  window.removeEventListener('keydown', keyboard);
});
</script>

<template>
  <main class="villager-picker" :aria-busy="loading">
    <header class="villager-picker-heading">
      <div>
        <div class="eyebrow">NHSE · {{ t('villagers') }}</div>
        <h1>{{ t('villagerCatalogTitle') }}</h1>
        <p>{{ t('villagerCatalogHint') }}</p>
      </div>
      <button class="icon-button" :aria-label="t('cancel')" :disabled="loading" @click="cancel">
        <X :size="22" />
      </button>
    </header>
    <section class="resident-section" :aria-label="t('currentResidents')">
      <div class="section-caption">{{ t('currentResidents') }}</div>
      <div class="resident-strip">
        <button
          v-for="r in context.residents"
          :key="r.index"
          class="resident-chip"
          :class="{ active: target === r.index }"
          :aria-pressed="target === r.index"
          :disabled="loading || r.empty"
          @click="target = r.index"
        >
          <img v-if="r.image" :src="r.image" alt="" /><Users v-else :size="26" /><span
            >{{ r.empty ? t('emptyResident') : r.name }}<small>{{ t('slot') }} {{ r.index + 1 }}</small></span
          ><Check v-if="target === r.index" :size="16" />
        </button>
      </div>
    </section>
    <div class="villager-filters">
      <label class="search-field"
        ><Search :size="18" /><span class="sr-only">{{ t('searchVillagers') }}</span
        ><input v-model="query" type="search" autofocus :placeholder="t('searchVillagers')" /></label
      ><label
        >{{ t('villagerPersonality')
        }}<select v-model.number="personality" :disabled="loading">
          <option :value="-1">{{ t('allPersonalities') }}</option>
          <option
            v-for="p in catalog?.personalities ?? []"
            :key="p.id"
            :value="p.id"
            :disabled="p.total === 0"
          >
            {{ personalityNames[p.id] }} ({{ p.total }})
          </option>
        </select></label
      >
    </div>
    <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
    <div class="villager-browser">
      <nav class="catalog-categories villager-species" :aria-label="t('villagerSpecies')">
        <button
          :class="{ active: species === -1 }"
          :aria-pressed="species === -1"
          :disabled="loading"
          @click="species = -1"
        >
          {{ t('allSpecies') }}</button
        ><button
          v-for="s in catalog?.species ?? []"
          :key="s.id"
          :class="{ active: species === s.id }"
          :aria-pressed="species === s.id"
          :disabled="loading || s.total === 0"
          @click="species = s.id"
        >
          <span>{{ speciesNames[s.id] }}</span
          ><small>{{ s.total }}</small>
        </button>
      </nav>
      <section class="villager-catalog-main" :aria-label="t('villagerCatalogTitle')">
        <div class="catalog-summary" role="status">
          <span
            >{{ species < 0 ? t('allSpecies') : speciesNames[species] }} · {{ catalog?.total ?? 0 }}</span
          ><span>{{
            loading ? t('loading') : `${t('page')} ${1 + (catalog?.page ?? 0)} / ${catalog?.pages ?? 1}`
          }}</span>
        </div>
        <div ref="resultPanel" class="villager-grid">
          <button
            v-for="v in catalog?.items ?? []"
            :key="v.id"
            class="villager-tile"
            :class="{ selected: selected?.id === v.id }"
            :aria-label="v.name"
            :aria-pressed="selected?.id === v.id"
            :disabled="loading || unavailable(v.id)"
            @click="selected = v"
          >
            <img v-if="v.image" :src="v.image" alt="" loading="lazy" /><Users v-else :size="48" /><strong>{{
              v.name
            }}</strong
            ><small>{{ speciesNames[v.species] }} · {{ personalityNames[v.personality] }}</small
            ><span v-if="unavailable(v.id) || resident?.id === v.id" class="villager-owned">{{
              resident?.id === v.id ? t('currentVillager') : t('alreadyOnIsland')
            }}</span>
          </button>
          <p v-if="catalog?.items.length === 0 && !loading" class="empty-result">{{ t('noVillagers') }}</p>
        </div>
        <form v-if="catalog" class="catalog-pagination" @submit.prevent="search(jump - 1)">
          <div class="catalog-page-buttons">
            <button
              type="button"
              class="icon-button"
              :aria-label="t('previous')"
              :disabled="loading || catalog.page === 0"
              @click="search(catalog.page - 1)"
            >
              <ChevronLeft :size="18" /></button
            ><button
              type="button"
              class="icon-button"
              :aria-label="t('next')"
              :disabled="loading || catalog.page >= catalog.pages - 1"
              @click="search(catalog.page + 1)"
            >
              <ChevronRight :size="18" />
            </button>
          </div>
          <label
            >{{ t('jumpToPage')
            }}<input
              v-model.number="jump"
              type="number"
              required
              min="1"
              :max="catalog.pages"
              :disabled="loading" /></label
          ><button class="button" :disabled="loading">{{ t('go') }}</button>
        </form>
      </section>
      <aside class="villager-preview">
        <div class="section-caption">{{ t('replacementPreview') }}</div>
        <div class="villager-before">
          <img v-if="resident?.image" :src="resident.image" alt="" /><strong>{{ resident?.name }}</strong>
        </div>
        <ArrowRight :size="20" /><template v-if="selected"
          ><img v-if="selected.image" class="villager-after" :src="selected.image" alt="" />
          <h2>{{ selected.name }}</h2>
          <p>{{ speciesNames[selected.species] }} · {{ personalityNames[selected.personality] }}</p></template
        >
        <p v-else class="field-help">{{ t('chooseVillagerHint') }}</p>
        <p class="field-help">{{ t('villagerReplaceHint') }}</p>
        <button
          class="button primary"
          :disabled="
            loading || resident?.empty || !selected || selected.id === resident?.id || (selected && unavailable(selected.id))
          "
          @click="finish()"
        >
          {{ t('confirmVillager') }}</button
        ><button class="button" :disabled="loading || resident?.empty" @click="finish(true)">
          {{ t('editSelectedResident') }}
        </button>
      </aside>
    </div>
  </main>
</template>
