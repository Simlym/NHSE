import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import { request, type MapData, type MapTarget, type RoomItem, type GridPastePreview } from './bridge';
type Point = { x: number; y: number };
type Mode = 'select' | 'move' | 'stamp';
const key = (point: Point) => `${point.x},${point.y}`;
export function useMapPlacement(options: {
  data: () => MapData | null; viewport: () => HTMLElement | null; selection: () => Point[];
  values: () => Record<string, string | number | boolean>; busy: () => boolean; hasPending: () => boolean;
  noOverwrite: () => boolean; snapFieldItems: () => boolean;
  execute: (name: string, payload: Record<string, unknown>) => Promise<boolean>;
  onModeChange: (mode: Mode) => void; onCommitted?: () => void;
}) {
  const mode = ref<Mode>('select'), preview = shallowRef<GridPastePreview | null>(null), checking = ref(false), committing = ref(false), dragging = ref(false);
  const template = shallowRef<RoomItem | null>(null), buildingGhost = shallowRef<Point | null>(null);
  let token = 0, timer: ReturnType<typeof setTimeout> | undefined, hover: Point | null = null;
  let gesture: { id: number; element: HTMLElement; start: Point; target: MapTarget; selection: Point[]; origin: Point; building: Point | null; moved: boolean } | null = null;
  const placedKeys = computed(() => new Set(checking.value ? [] : (preview.value?.placedCells ?? preview.value?.cells ?? []).map(key)));
  const conflictKeys = computed(() => new Set(checking.value ? [] : preview.value?.conflicts?.map(key) ?? []));
  const clearedKeys = computed(() => new Set(checking.value ? [] : preview.value?.clearedCells?.map(key) ?? []));
  function point(event: PointerEvent): Point | null {
    const element = options.viewport(); if (!element) return null;
    const box = element.getBoundingClientRect();
    return { x: Math.floor((event.clientX - box.left - element.clientLeft) / element.clientWidth * 32), y: Math.floor((event.clientY - box.top - element.clientTop) / element.clientHeight * 32) };
  }
  function release() {
    const current = gesture; gesture = null; dragging.value = false;
    if (current?.element.hasPointerCapture(current.id)) current.element.releasePointerCapture(current.id);
    return current;
  }
  function cancelGesture() { ++token; clearTimeout(timer); release(); preview.value = null; checking.value = false; buildingGhost.value = null; }
  function cancel() { cancelGesture(); mode.value = 'select'; options.onModeChange('select'); }
  function setTemplate(item: RoomItem) { template.value = { ...item }; if (mode.value === 'stamp' && hover) schedule(stampPayload(hover)); }
  function useCurrentItem() {
    const values = options.values();
    setTemplate({ itemId: Number(values['Map.ItemId']), systemParam: Number(values['Map.SystemParam']), additionalParam: Number(values['Map.AdditionalParam']), freeParam: Number(values['Map.FreeParam']) });
  }
  function setMode(value: Mode) {
    if (options.busy() || committing.value || value !== 'select' && options.hasPending()) return;
    cancelGesture(); mode.value = value;
    if (value === 'stamp' && !template.value) useCurrentItem();
    options.onModeChange(value);
  }
  function stampPayload(destination: Point) {
    const data = options.data();
    if (!data || !template.value || destination.x < 0 || destination.y < 0 || destination.x >= 32 || destination.y >= 32) return null;
    return { target: { ...data.target, ...destination }, current: { ...template.value }, noOverwrite: options.noOverwrite(), snapFieldItems: options.snapFieldItems() };
  }
  function movePayload(current: NonNullable<typeof gesture>, destination: Point) {
    return { target: { ...current.target }, selection: current.selection.map(point => ({ ...point })), anchor: { x: current.origin.x + destination.x - current.start.x, y: current.origin.y + destination.y - current.start.y }, noOverwrite: options.noOverwrite(), snapFieldItems: options.snapFieldItems() };
  }
  async function check(payload: Record<string, unknown> | null, command: string) {
    clearTimeout(timer); const sequence = ++token;
    if (!payload) { preview.value = null; checking.value = false; return null; }
    checking.value = true;
    try {
      const result = await request<GridPastePreview>(command, payload);
      if (sequence !== token) return null;
      preview.value = result; return result;
    } catch (error) {
      if (sequence === token) preview.value = { valid: false, error: error instanceof Error ? error.message : 'invalidRequest', placements: 0, changedCells: 0, replacedItems: 0, cells: [] };
      return null;
    } finally { if (sequence === token) checking.value = false; }
  }
  function schedule(payload: Record<string, unknown> | null, command = 'previewPlaceMap') {
    clearTimeout(timer); ++token; preview.value = null; checking.value = !!payload;
    if (payload) timer = setTimeout(() => { void check(payload, command); }, 70);
  }
  function down(event: PointerEvent) {
    const data = options.data();
    if (!data || options.busy() || committing.value || options.hasPending() || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || gesture) return false;
    const building = data.target.mode === 'buildings' ? data.buildingPins?.find(pin => pin.id === data.target.building) : null;
    if (!building && (data.target.mode !== 'items' || mode.value === 'select')) return false;
    const start = point(event); if (!start) return false;
    let selection = options.selection().some(p => key(p) === key(start)) ? options.selection().map(p => ({ ...p })) : [start];
    const roots = selection.map(p => data.cells.find(cell => cell.x === p.x && cell.y === p.y)).filter(cell => cell && (cell.name || cell.extension));
    if (!building && mode.value === 'move' && !roots.length) return false;
    const origin = roots.length ? { x: Math.min(...roots.map(cell => cell!.x + cell!.rootX - cell!.relativeX)), y: Math.min(...roots.map(cell => cell!.y + cell!.rootY - cell!.relativeY)) } : start;
    const element = event.currentTarget as HTMLElement;
    event.preventDefault(); event.stopPropagation();
    gesture = { id: event.pointerId, element, start, target: { ...data.target }, selection, origin, building: building ? { x: building.x, y: building.y } : null, moved: false };
    dragging.value = true; element.focus({ preventScroll: true }); element.setPointerCapture(event.pointerId);
    if (!building && mode.value === 'stamp') schedule(stampPayload(start));
    return true;
  }
  function move(event: PointerEvent) {
    const destination = point(event); hover = destination;
    if (gesture && gesture.id === event.pointerId && destination) {
      gesture.moved ||= key(destination) !== key(gesture.start);
      if (gesture.building) buildingGhost.value = { x: gesture.building.x + destination.x - gesture.start.x, y: gesture.building.y + destination.y - gesture.start.y };
      else schedule(mode.value === 'move' ? movePayload(gesture, destination) : stampPayload(destination), mode.value === 'move' ? 'previewMoveMap' : 'previewPlaceMap');
      return true;
    }
    if (mode.value === 'stamp' && destination && !options.busy() && !committing.value) schedule(stampPayload(destination));
    return false;
  }
  async function up(event: PointerEvent) {
    if (!gesture || gesture.id !== event.pointerId) return false;
    const current = gesture, destination = point(event);
    release(); clearTimeout(timer); ++token;
    if (!destination || options.busy() || options.hasPending() || committing.value || (!current.moved && mode.value !== 'stamp')) { cancelGesture(); return true; }
    if (current.building) {
      const x = current.building.x + destination.x - current.start.x, y = current.building.y + destination.y - current.start.y;
      buildingGhost.value = null;
      if (x < 0 || x >= 288 || y < 0 || y >= 256) return true;
      committing.value = true;
      try { if (await options.execute('editMap', { target: current.target, values: { 'Map.X': x, 'Map.Y': y }, action: 'apply', autoExtensions: true, noOverwrite: options.noOverwrite(), snapFieldItems: true })) options.onCommitted?.(); }
      finally { committing.value = false; }
      return true;
    }
    const moving = mode.value === 'move', payload = moving ? movePayload(current, destination) : stampPayload(destination);
    const result = await check(payload, moving ? 'previewMoveMap' : 'previewPlaceMap');
    if (!payload || !result?.valid || !result.changedCells || options.busy()) return true;
    committing.value = true;
    try { if (await options.execute(moving ? 'moveMap' : 'placeMap', payload)) { preview.value = null; options.onCommitted?.(); } }
    finally { committing.value = false; }
    return true;
  }
  function leave() { if (!gesture) { hover = null; ++token; clearTimeout(timer); preview.value = null; checking.value = false; } }
  function lostCapture(event: PointerEvent) { if (gesture?.id === event.pointerId) cancelGesture(); }
  function keydown(event: KeyboardEvent) {
    if (gesture && (event.ctrlKey || event.metaKey) && ['z', 'y'].includes(event.key.toLowerCase())) {
      event.preventDefault(); event.stopPropagation(); cancelGesture(); return true;
    }
    if (event.key === 'Escape' && (gesture || mode.value !== 'select')) { event.preventDefault(); event.stopPropagation(); cancel(); return true; }
    if (event.key.toLowerCase() === 'r' && mode.value === 'stamp' && !event.ctrlKey && !event.metaKey && !event.altKey && template.value && !committing.value) {
      event.preventDefault(); event.stopPropagation(); setTemplate({ ...template.value, systemParam: template.value.systemParam & ~3 | (template.value.systemParam + 1) & 3 }); return true;
    }
    return false;
  }
  watch(() => JSON.stringify([options.data()?.target.mode, options.data()?.target.acre, options.data()?.target.layer]), () => { cancelGesture(); if (options.data()?.target.mode !== 'items') mode.value = 'select'; });
  watch(() => [options.noOverwrite(), options.snapFieldItems()], () => { if (mode.value === 'stamp' && hover) schedule(stampPayload(hover)); });
  onMounted(() => window.addEventListener('blur', cancelGesture));
  onBeforeUnmount(() => { cancelGesture(); window.removeEventListener('blur', cancelGesture); });
  return { mode, template, preview, checking, committing, dragging, buildingGhost, placedKeys, conflictKeys, clearedKeys, setMode, setTemplate, useCurrentItem, down, move, up, leave, lostCapture, cancelGesture, cancel, keydown };
}
