import { computed, onBeforeUnmount, ref, watch, type Ref } from 'vue';
export interface GridPoint { x: number; y: number }
export const pointKey = (p: GridPoint) => `${p.x},${p.y}`;
export function editableTarget(target: EventTarget | null) {
  return target instanceof Element && !!target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])');
}
export function useGridSelection(options: {
  cells: () => GridPoint[]; scope: () => string; active: () => GridPoint;
  busy: () => boolean; activate: (point: GridPoint) => Promise<void>;
  action: (name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') => Promise<void>;
}) {
  const selected: Ref<GridPoint[]> = ref([]);
  const keys = computed(() => new Set(selected.value.map(pointKey)));
  const dragging = ref(false);
  let anchor: GridPoint | null = null, start: GridPoint | null = null;
  let pointerId = -1, grid: HTMLElement | null = null, before: GridPoint[] = [];
  let additive = false, toggle = false, last: GridPoint | null = null, skipClickUntil = 0;
  let queued: { name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate'; scope: string }[] = [];
  let flushing = false;
  function enqueue(name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') {
    if (queued.length < 8) queued.push({ name, scope: options.scope() });
    void flush();
  }
  async function flush() {
    if (flushing || options.busy()) return;
    flushing = true;
    try {
      while (queued.length && !options.busy()) {
        const next = queued.shift()!;
        if (next.scope === options.scope()) await options.action(next.name);
      }
    } finally { flushing = false; }
  }
  watch(options.busy, busy => { if (!busy) void flush(); });
  const valid = (p: GridPoint) => options.cells().some(c => c.x === p.x && c.y === p.y);
  watch(options.scope, () => {
    queued = []; cancel(); anchor = null;
    selected.value = valid(options.active()) ? [{ ...options.active() }] : [];
  }, { immediate: true });
  onBeforeUnmount(() => { queued = []; cancel(); });
  function rectangle(a: GridPoint, b: GridPoint) {
    return options.cells().filter(p => p.x >= Math.min(a.x, b.x) && p.x <= Math.max(a.x, b.x) && p.y >= Math.min(a.y, b.y) && p.y <= Math.max(a.y, b.y)).map(p => ({ ...p }));
  }
  function unique(points: GridPoint[]) { return [...new Map(points.map(p => [pointKey(p), p])).values()]; }
  function at(event: PointerEvent) {
    if (!grid) return null;
    const box = grid.getBoundingClientRect();
    const cells = options.cells();
    const width = Number(grid.dataset.columns), height = Number(grid.dataset.rows);
    const x = Math.floor((event.clientX - box.left) / box.width * width), y = Math.floor((event.clientY - box.top) / box.height * height);
    return cells.find(c => c.x === x && c.y === y) ?? null;
  }
  function preview(point: GridPoint) {
    if (!start) return;
    if (toggle && point.x === start.x && point.y === start.y) {
      selected.value = keysBefore().has(pointKey(point)) ? before.filter(p => pointKey(p) !== pointKey(point)) : unique([...before, { ...point }]);
    } else selected.value = unique([...(additive ? before : []), ...rectangle(start, point)]);
    last = { ...point };
  }
  function keysBefore() { return new Set(before.map(pointKey)); }
  function down(event: PointerEvent) {
    if (options.busy() || dragging.value || event.button !== 0) return;
    grid = event.currentTarget as HTMLElement;
    const point = at(event); if (!point) { grid = null; return; }
    event.preventDefault();
    before = selected.value.map(p => ({ ...p }));
    additive = event.ctrlKey || event.metaKey; toggle = additive && !event.shiftKey;
    start = event.shiftKey && anchor ? { ...anchor } : { ...point };
    if (!event.shiftKey) anchor = { ...point };
    pointerId = event.pointerId; dragging.value = true;
    grid.focus({ preventScroll: true }); grid.setPointerCapture(pointerId); preview(point);
  }
  function move(event: PointerEvent) { if (dragging.value && event.pointerId === pointerId) { const point = at(event); if (point) preview(point); } }
  function release() {
    const element = grid, id = pointerId;
    dragging.value = false; grid = null; pointerId = -1; start = null; last = null;
    if (element?.hasPointerCapture(id)) element.releasePointerCapture(id);
  }
  function cancel() { if (dragging.value) selected.value = before.map(p => ({ ...p })); release(); }
  async function up(event: PointerEvent) {
    if (!dragging.value || event.pointerId !== pointerId) return;
    const point = at(event); if (point) preview(point);
    const active = last && keys.value.has(pointKey(last)) ? last : selected.value[0];
    skipClickUntil = performance.now() + 250;
    release();
    if (active) await options.activate({ ...active });
  }
  async function click(event: MouseEvent, point: GridPoint) {
    if (options.busy() || (event.detail > 0 && performance.now() < skipClickUntil)) return;
    if (event.shiftKey && anchor) selected.value = rectangle(anchor, point);
    else if (event.ctrlKey || event.metaKey) selected.value = keys.value.has(pointKey(point)) ? selected.value.filter(p => pointKey(p) !== pointKey(point)) : [...selected.value, { ...point }];
    else { selected.value = [{ ...point }]; anchor = { ...point }; }
    await options.activate({ ...point });
  }
  function keydown(event: KeyboardEvent) {
    if (editableTarget(event.target)) return;
    if (event.key === 'Escape') { queued = []; event.preventDefault(); event.stopPropagation(); cancel(); selected.value = []; return; }
    if (dragging.value) { event.preventDefault(); event.stopPropagation(); if (event.ctrlKey || event.metaKey) cancel(); return; }
    const ctrl = event.ctrlKey || event.metaKey, key = event.key.toLowerCase();
    if (ctrl && !event.altKey && ['a', 'c', 'v'].includes(key)) {
      event.preventDefault(); event.stopPropagation();
      if (key === 'a') selected.value = options.cells().map(p => ({ ...p }));
      else if (!event.repeat) enqueue(key === 'c' ? 'copyGrid' : 'pasteGrid');
      return;
    }
    if (!ctrl && !event.altKey && (key === 'delete' || key === 'backspace' || key === 'r')) {
      event.preventDefault(); event.stopPropagation(); if (!event.repeat) enqueue(key === 'r' ? 'rotate' : 'delete'); return;
    }
    if (options.busy()) return;
    const delta = ({ ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] } as Record<string, number[]>)[event.key];
    if (delta && !ctrl && !event.altKey) {
      const active = options.active(), next = { x: active.x + delta[0]!, y: active.y + delta[1]! };
      event.preventDefault(); event.stopPropagation();
      if (!valid(next)) return;
      if (!anchor) anchor = { ...active };
      selected.value = event.shiftKey ? rectangle(anchor, next) : [next];
      if (!event.shiftKey) anchor = { ...next };
      void options.activate(next);
    }
  }
  return { selected, keys, dragging, down, move, up, cancel, click, keydown, clear: () => { queued = []; cancel(); selected.value = []; } };
}
