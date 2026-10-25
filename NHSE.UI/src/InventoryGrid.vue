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
  observer = new ResizeObserver(() => {
    if (element.value) columns.value = getComputedStyle(element.value).gridTemplateColumns.split(' ').length;
  });
  if (element.value) observer.observe(element.value);
});
onBeforeUnmount(() => observer?.disconnect());
</script>
<template>
  <div ref="element" class="item-grid inventory-selection-grid" :class="{ 'pocket-grid': container === 'pocket' }"
    tabindex="0" role="group" :aria-label="t(container === 'pocket' ? 'pocket' : container === 'recycle' ? 'recycle' : 'storage')"
    :data-columns="columns" :data-rows="Math.ceil(items.length / columns)"
    @pointerdown="selection.down" @pointermove="selection.move" @pointerup="selection.up"
    @pointercancel="selection.cancel" @lostpointercapture="selection.cancel" @keydown="selection.keydown">
    <button v-for="(item, index) in items" :key="item.index" tabindex="-1" class="item-slot"
      :class="{ selected: selected?.index === item.index, 'multi-selected': selection.keys.value.has(pointKey(point(index))), 'is-empty': item.empty, locked: !item.available }"
      :disabled="busy || !item.available"
      :aria-label="`${t('slot')} ${item.index + 1}: ${!item.available ? t('locked') : item.empty ? t('empty') : item.name}`"
      :aria-pressed="selection.keys.value.has(pointKey(point(index)))"
      @click="selection.click($event, point(index))">
      <span class="slot-index">{{ item.index + 1 }}</span>
      <LockKeyhole v-if="!item.available" :size="18" /><img v-else-if="item.image" :src="item.image" alt="" />
      <Plus v-else :size="17" /><span v-if="!item.empty && item.quantity > 1" class="item-count">{{ item.quantity }}</span>
    </button>
  </div>
  <GridToolbar :count="selection.selected.value.length" :clipboard="clipboard" kind="items"
    :busy="busy || selection.dragging.value" :action="action" :clear="selection.clear" />
</template>
<style scoped>
.inventory-selection-grid { touch-action: none; user-select: none; }
.item-slot.multi-selected { outline: 2px solid var(--green); outline-offset: -2px; background: var(--surface); }
</style>
