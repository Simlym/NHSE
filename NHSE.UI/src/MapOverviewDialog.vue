<script setup lang="ts">
import { nextTick, ref } from 'vue';
import { Compass, X } from '@lucide/vue';
import { t } from './i18n';
const props = defineProps<{ overview: string | null; acre: number; acres: { id: number; name: string }[]; busy: boolean; navigate: (target: { acre: number }) => Promise<void> }>();
const dialog = ref<HTMLDialogElement | null>(null), active = ref(0);
async function open() {
  if (props.busy || !props.overview) return;
  active.value = props.acre; dialog.value?.showModal();
  await nextTick(); focus();
}
function focus() { dialog.value?.querySelector<HTMLButtonElement>(`[data-acre="${active.value}"]`)?.focus(); }
function close() { dialog.value?.close(); }
async function choose(acre: number) { close(); await props.navigate({ acre }); }
async function keydown(event: KeyboardEvent) {
  const delta = ({ ArrowLeft: -1, ArrowRight: 1, ArrowUp: -9, ArrowDown: 9 } as Record<string, number>)[event.key];
  if (!delta) return;
  event.preventDefault();
  const next = active.value + delta;
  if (!props.acres.some(acre => acre.id === next) || Math.abs(delta) === 1 && Math.floor(next / 9) !== Math.floor(active.value / 9)) return;
  active.value = next; await nextTick(); focus();
}
</script>
<template>
  <button type="button" :disabled="busy || !overview" :title="t('mapWholeOverview')" :aria-label="t('mapWholeOverview')" @click="open"><Compass :size="17" /></button>
  <dialog ref="dialog" class="modal map-whole-overview" :aria-label="t('mapWholeOverview')" aria-describedby="map-whole-hint" @cancel.prevent="close">
    <header><h2>{{ t('mapWholeOverview') }}</h2><button type="button" :aria-label="t('cancel')" @click="close"><X :size="18" /></button></header>
    <p id="map-whole-hint">{{ t('mapWholeOverviewHint') }}</p>
    <div class="whole-overview-map" @keydown="keydown">
      <img v-if="overview" :src="overview" alt="" draggable="false" />
      <button v-for="entry in acres" :key="entry.id" type="button" :data-acre="entry.id" :class="{ current: entry.id === acre }" :aria-label="`${t('mapAcre')} ${entry.name}`" :aria-current="entry.id === acre ? 'location' : undefined" :tabindex="entry.id === active ? 0 : -1" :style="{ left: `${entry.id % 9 / 9 * 100}%`, top: `${Math.floor(entry.id / 9) / 8 * 100}%` }" @focus="active = entry.id" @click="choose(entry.id)"><span>{{ entry.name }}</span></button>
    </div>
    <div class="form-actions"><button type="button" class="button" @click="close">{{ t('cancel') }}</button></div>
  </dialog>
</template>
<style scoped>
.map-whole-overview { width: min(92vw, 800px); max-height: 92dvh; padding: 1rem; }
header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
h2 { margin: 0; font-size: 1.1rem; }
header button { display: grid; place-items: center; padding: .4rem; border: 1px solid var(--line); border-radius: .4rem; background: var(--surface); color: inherit; }
p { color: var(--muted); line-height: 1.5; font-size: .85rem; }
.whole-overview-map { position: relative; aspect-ratio: 9/8; width: min(100%, 72dvh); margin: 1rem auto; border: 1px solid var(--line-strong); }
img { display: block; width: 100%; height: 100%; pointer-events: none; image-rendering: pixelated; }
.whole-overview-map button { position: absolute; width: calc(100% / 9); height: calc(100% / 8); border: 1px solid #263f3826; padding: 0; border-radius: 0; background: transparent; color: white; }
.whole-overview-map button span { display: block; overflow: hidden; max-width: 100%; padding: 2px; font-size: clamp(8px, 1.2vw, 12px); text-shadow: 0 1px 3px #000, 0 0 3px #000; }
.whole-overview-map button.current { outline: 3px solid #ffcf45; outline-offset: -3px; }
.whole-overview-map button:hover, .whole-overview-map button:focus-visible { background: #ffffff35; outline: 3px solid #176cbb; outline-offset: -3px; z-index: 1; }
.form-actions { justify-content: flex-end; margin: .5rem 0 0; }
</style>
