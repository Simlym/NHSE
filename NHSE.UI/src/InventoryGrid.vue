<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue';
import { LockKeyhole, Plus } from '@lucide/vue';
import { t } from './i18n';
import type { Inventory, ItemView, GridClipboardInfo, GridTarget } from './bridge';
import { useGridSelection, pointKey, type GridPoint } from './gridSelection';
import GridToolbar from './GridToolbar.vue';
const props = defineProps<{
  data: Inventory | null; player: number; container: string; scope: string; busy: boolean;
  selected: ItemView | null; clipboard: GridClipboardInfo | null;
  choose: (item: ItemView) => void;
  execute: (command: string, payload: Record<string, unknown>) => Promise<boolean>;
}>();
const element = ref<HTMLElement | null>(null);
const emit = defineEmits<{ clear: []; replace: [item: ItemView] }>();
function clear() { selection.clear(); emit('clear'); }
function replace(event: MouseEvent) {
  if (props.busy || event.ctrlKey || event.metaKey || event.shiftKey) return;
  // Selection captures the pointer on the grid, so double-clicks may target the grid itself.
  const slot = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>('[data-slot-index]');
  if (!slot || !element.value?.contains(slot)) return;
  const item = items.value[Number(slot.dataset.slotIndex)];
  if (item?.available) emit('replace', item);
}
function escape(event: KeyboardEvent) {
  if (event.key !== 'Escape' || document.querySelector('dialog[open], [aria-modal="true"]')) return;
  event.preventDefault();
  clear();
}
function outside(event: PointerEvent) {
  const target = event.target;
  if (!(target instanceof Element) || !target.closest('.workspace-content')) return;
  if (target.closest('button, input, textarea, select, a, summary, .inspector, .grid-toolbar, dialog')) return;
  clear();
}
const columns = ref(10);
const items = computed(() => props.data?.items ?? []);
const point = (index: number) => ({ x: index % columns.value, y: Math.floor(index / columns.value) });
const selection = useGridSelection({
  cells: () => items.value.flatMap((item, i) => item.available ? [point(i)] : []),
  scope: () => JSON.stringify([props.scope, props.player, props.container, columns.value, items.value.map(i => [i.index, i.available])]),
  active: () => point(Math.max(0, items.value.findIndex(i => i.index === props.selected?.index))),
  busy: () => props.busy,
  activate: async p => { const item = items.value[p.y * columns.value + p.x]; if (item?.available) props.choose(item); },
  action,
});
async function action(name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') {
  const points = selection.selected.value.map(p => ({ ...p }));
  if (props.busy || !points.length) return;
  const target: GridTarget = { kind: 'inventory', player: props.player, container: props.container,
    slots: items.value.map(i => i.index), columns: columns.value };
  const anchor: GridPoint = { x: Math.min(...points.map(p => p.x)), y: Math.min(...points.map(p => p.y)) };
  await props.execute(name === 'delete' || name === 'rotate' ? 'editGrid' : name,
    { target, selection: points, anchor, operation: name, noOverwrite: true });
}
let observer: ResizeObserver | undefined;
onMounted(() => {
  window.addEventListener('keydown', escape);
  window.addEventListener('pointerdown', outside);
  observer = new ResizeObserver(() => {
    if (element.value) columns.value = getComputedStyle(element.value).gridTemplateColumns.split(' ').length;
  });
  if (element.value) observer.observe(element.value);
});
onBeforeUnmount(() => {
  observer?.disconnect();
  window.removeEventListener('keydown', escape);
  window.removeEventListener('pointerdown', outside);
});
</script>
<template>
  <div ref="element" class="item-grid inventory-selection-grid" :class="{ 'pocket-grid': container === 'pocket' }"
    tabindex="0" role="group" :aria-label="t(container === 'pocket' ? 'pocket' : container === 'recycle' ? 'recycle' : 'storage')"
    :data-columns="columns" :data-rows="Math.ceil(items.length / columns)"
    @pointerdown="selection.down" @pointermove="selection.move" @pointerup="selection.up"
    @pointercancel="selection.cancel" @lostpointercapture="selection.cancel" @dblclick="replace" @keydown="($event.key === 'Escape' ? (clear(), $event.preventDefault(), $event.stopPropagation()) : selection.keydown($event))">
    <button v-for="(item, index) in items" :key="item.index" tabindex="-1" class="item-slot"
      :class="{ selected: selected?.index === item.index, 'multi-selected': selection.keys.value.has(pointKey(point(index))), 'is-empty': item.empty, locked: !item.available }"
      :disabled="busy || !item.available" :data-slot-index="index"
      :aria-label="`${t('slot')} ${item.index + 1}: ${!item.available ? t('locked') : item.empty ? t('empty') : item.name}`"
      :aria-pressed="selection.keys.value.has(pointKey(point(index)))"
      @click="selection.click($event, point(index))">
      <span class="slot-index">{{ item.index + 1 }}</span>
      <LockKeyhole v-if="!item.available" :size="18" /><img v-else-if="item.image" :src="item.image" alt="" />
      <Plus v-else :size="17" /><span v-if="!item.empty && item.quantity > 1" class="item-count">{{ item.quantity }}</span>
    </button>
  </div>
  <GridToolbar :count="selection.selected.value.length" :clipboard="clipboard" kind="items"
    :busy="busy || selection.dragging.value" :action="action" :clear="clear" />
</template>
<style scoped>
.inventory-selection-grid { touch-action: none; user-select: none; }
.inventory-selection-grid:focus { outline: none; }
.inventory-selection-grid:focus-visible { outline: 2px solid var(--green); outline-offset: 4px; }
.item-slot.multi-selected { outline: 2px solid var(--green); outline-offset: -2px; background: var(--surface); }
</style>
