<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import { MousePointer2, Hand, Paintbrush, ZoomIn, ZoomOut, Maximize, Grid2X2, Layers, ChevronLeft, ChevronRight, ChevronUp, ChevronDown, Compass, SlidersHorizontal, Package, Building2, Mountain, Flag, Search } from '@lucide/vue';
import { t } from './i18n';
import { request, type MapData, type MapTarget, type ItemView, type MapBrush, type GridClipboardInfo, type GridTarget, type GridPastePreview } from './bridge';
import HousingFields from './HousingFields.vue';
import MapTools from './MapTools.vue';
import MapAdvanced from './MapAdvanced.vue';
import GridToolbar from './GridToolbar.vue';
import MapPastePreview from './MapPastePreview.vue';
import MapPlacementTools from './MapPlacementTools.vue';
import { useMapPlacement } from './useMapPlacement';
import MapOverviewDialog from './MapOverviewDialog.vue';
import { useMapCamera } from './useMapCamera';
import { useGridSelection, pointKey, editableTarget } from './gridSelection';
import { buildingTypeName } from './mapBuildingLabels';
const props = defineProps<{
  data: MapData | null; dirty?: boolean; busy: boolean; loading?: boolean; clipboard: GridClipboardInfo | null;
  execute: (command: string, payload?: Record<string, unknown>) => Promise<boolean>;
  navigate: (target: Partial<MapTarget>) => Promise<void>; pickItem: () => void;
}>();
const modes: MapTarget['mode'][] = ['terrain', 'items', 'buildings', 'acres', 'markers'];
const modeIcons = [Mountain, Package, Building2, Grid2X2, Flag];
const handTool = ref(false), showGrid = ref(true), showItems = ref(true), buildingSearch = ref('');
const showNavigator = ref(true), showInspector = ref(true);
function toggleFocus() { const focus = showNavigator.value || showInspector.value; showNavigator.value = !focus; showInspector.value = !focus; }
const zoom = ref(1), canvasBaseSize = ref(480), canvasScroll = ref<HTMLElement | null>(null);
let canvasObserver: ResizeObserver | null = null;
let canvasResizeFrame = 0;
const panning = ref(false), spaceHeld = ref(false), canvasHovered = ref(false);
const camera = ref<{ x: number; y: number } | null>(null);
const panMode = computed(() => handTool.value || spaceHeld.value);
const canvasSize = computed(() => canvasBaseSize.value * zoom.value);
const canvasOffset = computed(() => camera.value && props.data ? {
  x: (props.data.target.acre % 9 - camera.value.x) * canvasSize.value,
  y: (Math.floor(props.data.target.acre / 9) - camera.value.y) * canvasSize.value,
} : { x: 0, y: 0 });
let panAcre: number | null = null, skipCanvasClickUntil = 0;
let panPointer: { id: number; element: HTMLElement; x: number; y: number; left: number; top: number;
  camera: { x: number; y: number } | null; acre: number; size: number; moved: boolean } | null = null;
watch(canvasScroll, element => {
  canvasObserver?.disconnect();
  cancelAnimationFrame(canvasResizeFrame);
  if (!element) return;
  canvasObserver = new ResizeObserver(() => {
    cancelAnimationFrame(canvasResizeFrame);
    canvasResizeFrame = requestAnimationFrame(() => {
      if (element.isConnected) canvasBaseSize.value = Math.max(160, Math.min(element.clientWidth, element.clientHeight) - 48);
    });
  });
  canvasObserver.observe(element);
}, { flush: 'post' });
const { setZoom, fitCanvas, canvasWheel, rememberView, restoreView } = useMapCamera({
  element: canvasScroll, zoom, baseSize: canvasBaseSize, camera,
  acre: () => props.data?.target.acre ?? 0,
  scope: () => JSON.stringify([props.data?.target.mode, props.data?.target.acre, props.data?.target.layer]),
  cancelPan, interactionBusy: () => !!strokeMeta.value || gridSelection.dragging.value,
});
function startPan(event: PointerEvent) {
  if (!panPointer) skipCanvasClickUntil = 0;
  if ((!panMode.value && event.button !== 1) || ![0, 1].includes(event.button) || !props.data || props.busy || panPointer || strokeMeta.value || gridSelection.dragging.value) return;
  const element = canvasScroll.value;
  if (!element) return;
  placement.cancelGesture();
  event.preventDefault(); event.stopPropagation();
  panPointer = { id: event.pointerId, element, x: event.clientX, y: event.clientY, left: element.scrollLeft, top: element.scrollTop,
    camera: camera.value && { ...camera.value }, acre: props.data.target.acre, size: canvasSize.value, moved: false };
  panning.value = true; element.focus({ preventScroll: true }); element.setPointerCapture(event.pointerId);
}
function movePan(event: PointerEvent) {
  const p = panPointer;
  if (!p || p.id !== event.pointerId) return;
  p.moved ||= Math.hypot(event.clientX - p.x, event.clientY - p.y) > 3;
  const left = p.left + p.x - event.clientX, top = p.top + p.y - event.clientY;
  p.element.scrollLeft = left; p.element.scrollTop = top;
  const dx = left - p.element.scrollLeft, dy = top - p.element.scrollTop;
  if (p.camera || dx || dy) camera.value = {
    x: Math.max(0, Math.min(8, (p.camera?.x ?? p.acre % 9) + dx / p.size)),
    y: Math.max(0, Math.min(7, (p.camera?.y ?? Math.floor(p.acre / 9)) + dy / p.size)),
  };
}
function releasePan() {
  const pointer = panPointer; panPointer = null; panning.value = false;
  if (pointer) skipCanvasClickUntil = performance.now() + 250;
  if (pointer?.element.hasPointerCapture(pointer.id)) pointer.element.releasePointerCapture(pointer.id);
  return pointer;
}
function cancelPan() {
  const p = releasePan();
  if (p) { camera.value = p.camera; p.element.scrollTo({ left: p.left, top: p.top }); }
}
async function finishPan(event: PointerEvent) {
  if (!panPointer || panPointer.id !== event.pointerId) return;
  movePan(event);
  const p = releasePan();
  if (!p) return;
  if (!p.moved) { camera.value = p.camera; p.element.scrollTo({ left: p.left, top: p.top }); return; }
  const acre = camera.value ? Math.round(camera.value.y) * 9 + Math.round(camera.value.x) : p.acre;
  if (acre !== p.acre) {
    panAcre = acre;
    try {
      await props.navigate({ acre }); await nextTick();
      if (props.data?.target.acre !== acre) { camera.value = p.camera; p.element.scrollTo({ left: p.left, top: p.top }); }
    } finally { panAcre = null; }
  }
  if (!handTool.value) canvasScroll.value?.querySelector<HTMLElement>('.map-grid')?.focus({ preventScroll: true });
}
function canvasClick(event: MouseEvent) {
  if (event.detail > 0 && (panning.value || performance.now() < skipCanvasClickUntil)) { event.preventDefault(); event.stopPropagation(); return; }
  if (!camera.value || panMode.value || props.busy || !props.data || (event.target as Element).closest('.map-grid, .map-building-label')) return;
  const viewport = canvasScroll.value?.querySelector<HTMLElement>('.map-viewport');
  const box = viewport?.getBoundingClientRect();
  if (!box || !viewport) return;
  const x = props.data.target.acre % 9 + (event.clientX - box.left - viewport.clientLeft) / (box.width - 2 * viewport.clientLeft);
  const y = Math.floor(props.data.target.acre / 9) + (event.clientY - box.top - viewport.clientTop) / (box.height - 2 * viewport.clientTop);
  if (x < 0 || x >= 9 || y < 0 || y >= 8) return;
  const acre = Math.floor(y) * 9 + Math.floor(x), size = props.data.gridSize;
  if (acre !== props.data.target.acre) {
    panAcre = acre;
    void props.navigate({ acre, x: Math.floor((x % 1) * size), y: Math.floor((y % 1) * size) }).finally(() => { panAcre = null; });
  }
}
function canvasKeyboard(event: KeyboardEvent) {
  if (editableTarget(event.target) || document.querySelector('dialog[open]')) return;
  if (placement.keydown(event)) return;
  if (event.key === 'Escape' && panPointer) { event.preventDefault(); event.stopPropagation(); cancelPan(); return; }
  if (event.key === 'Escape' && pasteOpen.value) { event.preventDefault(); event.stopPropagation(); closePastePreview(); return; }
  if (event.code === 'Space' && (canvasHovered.value || canvasScroll.value?.contains(event.target as Node))) {
    event.preventDefault(); if (!strokeMeta.value && !gridSelection.dragging.value) spaceHeld.value = true; return;
  }
  if (event.ctrlKey || event.metaKey || event.altKey || panPointer || strokeMeta.value || gridSelection.dragging.value) return;
  const key = event.key.toLowerCase();
  if (['h', 'v', 'b'].includes(key)) {
    if (key === 'b' && props.data?.target.mode !== 'terrain') return;
    event.preventDefault(); setTool(key === 'h' ? 'pan' : key === 'b' ? 'brush' : 'select');
  }
}
function canvasKeyup(event: KeyboardEvent) { if (event.code === 'Space') spaceHeld.value = false; }
function canvasBlur() { spaceHeld.value = false; cancelPan(); }
watch(() => [props.data?.target.acre, props.data?.target.mode], ([acre, mode], old) => {
  if (mode !== old?.[1] || acre !== old?.[0] && acre !== panAcre) camera.value = null;
});
onMounted(() => { void restoreView(); void loadBrushMasks(); window.addEventListener('keydown', canvasKeyboard, true); window.addEventListener('keyup', canvasKeyup); window.addEventListener('blur', canvasBlur); });

function moveAcre(dx: number, dy: number) {
  if (!props.data || props.busy) return;
  const x = Math.max(0, Math.min(8, props.data.target.acre % 9 + dx));
  const y = Math.max(0, Math.min(7, Math.floor(props.data.target.acre / 9) + dy));
  void props.navigate({ acre: y * 9 + x });
}
type BuildingPin = NonNullable<MapData['buildingPins']>[number];
function buildingName(pin: BuildingPin) {
  return buildingTypeName(pin.type);
}
function buildingChoice(id: number, fallback: string) {
  const pin = props.data?.buildingPins?.find(pin => pin.id === id);
  return pin ? `${id + 1} · ${buildingName(pin)} (${pin.x}, ${pin.y})` : fallback;
}
function selectBuilding(pin: BuildingPin) {
  handTool.value = false;
  void props.navigate({ mode: 'buildings', building: pin.id, acre: Math.floor(pin.y / 32) * 9 + Math.floor(pin.x / 32), x: 0, y: 0 });
}
function chooseBuilding(id: number) {
  const pin = props.data?.buildingPins?.find(pin => pin.id === id);
  if (pin) selectBuilding(pin); else void props.navigate({ building: id });
}
const filteredBuildings = computed(() => props.data?.buildingPins?.filter(pin => `${pin.id + 1} ${buildingName(pin)}`.toLocaleLowerCase().includes(buildingSearch.value.toLocaleLowerCase())) ?? []);
const visibleBuildings = computed(() => props.data?.buildingPins?.filter(pin =>
  Math.floor(pin.x / 32) === props.data!.target.acre % 9 && Math.floor(pin.y / 32) === Math.floor(props.data!.target.acre / 9)) ?? []);
const form = ref<HTMLFormElement | null>(null);
const autoExtensions = ref(true), noOverwrite = ref(true), snapFieldItems = ref(true);
const stored = () => Object.fromEntries((props.data?.fields ?? []).map(f => [f.key, f.value]));
const brush = ref<MapBrush>({ enabled: false, radius: 1, randomVariation: false });
const selectedDraft = ref(stored()), brushTemplate = ref<Record<string, number | boolean | string> | null>(null);
const brushEditing = computed(() => brush.value.enabled && props.data?.target.mode === 'terrain');
const fields = computed({ get: () => brushEditing.value ? brushTemplate.value ?? selectedDraft.value : selectedDraft.value,
  set: value => { if (brushEditing.value) brushTemplate.value = value; else selectedDraft.value = value; } });
const draftItem = shallowRef<ItemView | null>(null);
const currentCell = computed(() => props.data?.cells.find(cell => cell.x === props.data?.target.x && cell.y === props.data?.target.y));
const selectionName = computed(() => props.data?.target.mode === 'buildings'
  ? buildingChoice(props.data.target.building, t('mapEmptyBuilding'))
  : draftItem.value?.name || currentCell.value?.name || t(props.data?.target.mode === 'items' ? 'mapEmptyCell' : 'mapTerrainCell'));
const primaryKeys: Record<string, string[]> = { terrain: ['Map.Elevation', 'Map.UnitModel', 'Map.LandMakingAngle', 'Map.UnitModelRoad', 'Map.LandMakingAngleRoad'], items: ['Map.ItemId', 'Map.FreeParam'], buildings: ['Map.BuildingType', 'Map.X', 'Map.Y', 'Map.Angle'] };
const primaryFields = computed(() => { const data = props.data; return data?.fields.filter(field => !primaryKeys[data.target.mode] || primaryKeys[data.target.mode]!.includes(field.key)) ?? []; });
const advancedFields = computed(() => { const data = props.data; return data?.fields.filter(field => primaryKeys[data.target.mode] && !primaryKeys[data.target.mode]!.includes(field.key)) ?? []; });
function setTool(tool: 'select' | 'brush' | 'pan') {
  placement.cancel(); cancelPan();
  if (tool !== 'select') closePastePreview(false);
  handTool.value = tool === 'pan';
  if (tool !== 'pan') brush.value.enabled = tool === 'brush';
}
watch(() => JSON.stringify(props.data?.target), () => { draftItem.value = null; });
watch(() => props.data?.target.mode, () => { handTool.value = false; });
const strokePoints = ref<{ x: number; y: number }[]>([]);
const strokeMeta = shallowRef<{ target: MapTarget; radius: number; randomVariation: boolean; values: Record<string, number | boolean | string> } | null>(null);
let pointerGrid: HTMLElement | null = null, pointerId = -1;
let lastPoint: { x: number; y: number } | null = null;
const brushMasks = shallowRef<{ radius: number; offsets: { x: number; y: number }[] }[]>([]);
const brushMaskLoading = ref(true), brushMaskError = ref(false), brushHover = ref<{ x: number; y: number } | null>(null);
async function loadBrushMasks() {
  try { brushMasks.value = await request('mapBrushMask'); }
  catch { brushMaskError.value = true; }
  finally { brushMaskLoading.value = false; }
}
const brushFootprint = computed(() => {
  if (!brushEditing.value || panMode.value || props.busy) return [];
  const centers = strokeMeta.value ? strokePoints.value : brushHover.value ? [brushHover.value] : [];
  const offsets = brushMasks.value.find(mask => mask.radius === (strokeMeta.value?.radius ?? brush.value.radius))?.offsets ?? [];
  return [...new Map(centers.flatMap(point => offsets.map(offset => ({ x: point.x + offset.x, y: point.y + offset.y }))).map(point => [pointKey(point), point])).values()];
});
const brushKeys = computed(() => {
  const valid = new Set(props.data?.cells.filter(cell => cell.valid).map(pointKey));
  return new Set(brushFootprint.value.filter(point => valid.has(pointKey(point))).map(pointKey));
});
const strokeKeys = computed(() => strokeMeta.value ? brushKeys.value : new Set<string>());
const hoverBrushKeys = computed(() => !strokeMeta.value ? brushKeys.value : new Set<string>());
const brushOutside = computed(() => brushFootprint.value.filter(point => point.x < 0 || point.y < 0 || point.x >= 16 || point.y >= 16).length);
const selectionBusy = computed(() => props.busy && !props.loading);
const gridSelection = useGridSelection({
  cells: () => props.data?.cells.filter(c => c.valid).map(c => ({ x: c.x, y: c.y })) ?? [],
  scope: () => JSON.stringify([props.data?.target.mode, props.data?.target.acre, props.data?.target.layer]),
  active: () => ({ x: props.data?.target.x ?? 0, y: props.data?.target.y ?? 0 }),
  busy: () => selectionBusy.value,
  actionBusy: () => props.busy,
  activate: point => props.navigate(point), action: gridAction,
});
function gridPayload() {
  if (!props.data || !gridSelection.selected.value.length) return null;
  const target: GridTarget = { kind: props.data.target.mode === 'terrain' ? 'terrain' : 'mapItems', map: { ...props.data.target } };
  const selection = gridSelection.selected.value.map(p => ({ ...p }));
  const anchor = selection.length > 1 ? { x: Math.min(...selection.map(p => p.x)), y: Math.min(...selection.map(p => p.y)) } : { x: props.data.target.x, y: props.data.target.y };
  return { target, selection, anchor, noOverwrite: noOverwrite.value };
}
const placement = useMapPlacement({
  data: () => props.data, viewport: () => canvasScroll.value?.querySelector<HTMLElement>('.map-viewport') ?? null,
  selection: () => gridSelection.selected.value, values: () => fields.value, busy: () => props.busy,
  hasPending: () => selectedDraftPending.value, noOverwrite: () => noOverwrite.value, snapFieldItems: () => snapFieldItems.value,
  execute: props.execute,
  onModeChange: mode => { if (mode !== 'select') { cancelPan(); handTool.value = false; brush.value.enabled = false; closePastePreview(false); } },
  onCommitted: resetDraft,
});
function placementUp(event: PointerEvent) {
  if (!placement.dragging.value) return false;
  skipCanvasClickUntil = performance.now() + 250; void placement.up(event); return true;
}
function canvasPointerMove(event: PointerEvent) { if (panPointer) movePan(event); else placement.move(event); }
function canvasPointerUp(event: PointerEvent) { if (!placementUp(event)) void finishPan(event); }
function canvasPointerCancel() { placement.cancelGesture(); cancelPan(); }
const pasteOpen = ref(false), pasteLoading = ref(false), pastePreview = shallowRef<GridPastePreview | null>(null);
let pasteToken = 0, pasteTimer: ReturnType<typeof setTimeout> | undefined, pasteNeedsReveal = false;
const pasteKeys = computed(() => new Set(pasteLoading.value ? [] : pastePreview.value?.cells.map(pointKey) ?? []));
const pasteConflictKeys = computed(() => new Set(pasteLoading.value ? [] : pastePreview.value?.conflicts?.map(pointKey) ?? []));
const pastePlacedKeys = computed(() => new Set(pasteLoading.value ? [] : pastePreview.value?.placedCells?.map(pointKey) ?? []));
const pasteClearedKeys = computed(() => new Set(pasteLoading.value ? [] : pastePreview.value?.clearedCells?.map(pointKey) ?? []));
async function locatePasteConflict() {
  const point = pastePreview.value?.conflicts?.[0], data = props.data;
  if (!point || !data) return;
  camera.value = { x: Math.max(0, Math.min(8, data.target.acre % 9 + (point.x + .5) / data.gridSize - .5)), y: Math.max(0, Math.min(7, Math.floor(data.target.acre / 9) + (point.y + .5) / data.gridSize - .5)) };
  await nextTick();
  const el = canvasScroll.value;
  el?.scrollTo({ left: (el.scrollWidth - el.clientWidth) / 2, top: (el.scrollHeight - el.clientHeight) / 2 }); focusGrid();
}
const pasteOutside = computed(() => pastePreview.value?.cells.filter(p => p.x < 0 || p.y < 0 || p.x >= (props.data?.gridSize ?? 0) || p.y >= (props.data?.gridSize ?? 0)).length ?? 0);
function focusGrid() { canvasScroll.value?.querySelector<HTMLElement>('.map-grid')?.focus({ preventScroll: true }); }
async function revealPastePreview() {
  await nextTick();
  const panel = canvasScroll.value?.parentElement?.querySelector<HTMLElement>('.map-paste-preview');
  if (panel) panel.scrollTop = 0;
}
function closePastePreview(focus = true) {
  ++pasteToken; clearTimeout(pasteTimer); pasteNeedsReveal = false; pasteOpen.value = false; pasteLoading.value = false; pastePreview.value = null;
  if (focus) focusGrid();
}
function refreshPastePreview() {
  // Keep the previous result's geometry while checking. Collapsing it during pointerdown resizes the grid beneath the gesture.
  const token = ++pasteToken; clearTimeout(pasteTimer);
  const payload = gridPayload();
  if (!pasteOpen.value) return;
  if (!payload || !props.clipboard || !['terrain', 'items'].includes(props.data?.target.mode ?? '')) { closePastePreview(false); return; }
  pasteLoading.value = true;
  if (props.busy || gridSelection.dragging.value) return;
  pasteTimer = setTimeout(async () => {
    try {
      const result = await request<GridPastePreview>('previewGridPaste', payload);
      if (token === pasteToken) pastePreview.value = result;
    } catch (error) {
      if (token === pasteToken) pastePreview.value = { valid: false, error: error instanceof Error ? error.message : 'invalidRequest', placements: 0, changedCells: 0, replacedItems: 0, cells: [] };
    } finally {
      if (token === pasteToken) {
        if (pasteNeedsReveal) { pasteNeedsReveal = false; await revealPastePreview(); }
        if (token === pasteToken) pasteLoading.value = false;
      }
    }
  }, 80);
}
watch(() => [props.data, props.clipboard, props.busy, gridSelection.dragging.value, JSON.stringify(gridPayload())], refreshPastePreview, { flush: 'sync' });
async function confirmPaste() {
  const payload = gridPayload();
  if (!payload || props.busy || pasteLoading.value || !pastePreview.value?.valid || !pastePreview.value.changedCells) return;
  if (await props.execute('pasteGrid', payload)) { fields.value = stored(); closePastePreview(); }
  else refreshPastePreview();
}
async function gridAction(name: 'copyGrid' | 'pasteGrid' | 'delete' | 'rotate') {
  if (!props.data || props.busy || !gridSelection.selected.value.length || strokeMeta.value) return;
  if (name === 'pasteGrid') {
    setTool('select'); pasteNeedsReveal = !pasteOpen.value; pasteOpen.value = true; refreshPastePreview(); focusGrid();
    return;
  }
  closePastePreview(false);
  const success = await props.execute(name === 'delete' || name === 'rotate' ? 'editGrid' : name, { ...gridPayload(), operation: name });
  if (success && name !== 'copyGrid' && !brush.value.enabled) fields.value = stored();
}
function gridPointerDown(event: PointerEvent) {
  if (placement.down(event)) return;
  pasteNeedsReveal = false;
  if (brush.value.enabled && !event.ctrlKey && !event.metaKey && !event.shiftKey) startStroke(event);
  else gridSelection.down(event);
}
function gridPointerMove(event: PointerEvent) {
  if (placement.move(event)) { event.stopPropagation(); return; }
  const grid = event.currentTarget as HTMLElement, box = grid.getBoundingClientRect();
  const x = Math.floor((event.clientX - box.left) / box.width * 16), y = Math.floor((event.clientY - box.top) / box.height * 16);
  brushHover.value = x >= 0 && y >= 0 && x < 16 && y < 16 ? { x, y } : null;
  if (gridSelection.dragging.value) gridSelection.move(event); else moveStroke(event); }
function gridPointerUp(event: PointerEvent) { if (placementUp(event)) { event.stopPropagation(); return; } if (gridSelection.dragging.value) void gridSelection.up(event); else void endStroke(event); }
function cancelGridPointer() { placement.cancelGesture(); gridSelection.cancel(); cancelStroke(); }
function gridLostCapture(event: PointerEvent) { placement.lostCapture(event); gridSelection.cancel(); cancelStroke(); }
function gridKeydown(event: KeyboardEvent) {
  if (event.key.startsWith('Arrow')) pasteNeedsReveal = false;
  if (pasteOpen.value && event.key === 'Enter' && !event.ctrlKey && !event.metaKey && !event.altKey) { event.preventDefault(); event.stopPropagation(); if (!event.repeat) void confirmPaste(); return; }
  if (strokeMeta.value) {
    strokeKeyboard(event);
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); cancelStroke(); }
    return;
  }
  gridSelection.keydown(event);
}
function gridPoint(event: PointerEvent) {
  if (!pointerGrid) return null;
  const box = pointerGrid.getBoundingClientRect();
  const x = Math.floor((event.clientX - box.left) / box.width * 16), y = Math.floor((event.clientY - box.top) / box.height * 16);
  return x >= 0 && x < 16 && y >= 0 && y < 16 && props.data?.cells.some(c => c.x === x && c.y === y && c.valid) ? { x, y } : null;
}
function collect(event: PointerEvent) {
  const point = gridPoint(event);
  if (!point) { lastPoint = null; return; }
  let x = lastPoint?.x ?? point.x, y = lastPoint?.y ?? point.y;
  const dx = Math.abs(point.x - x), dy = -Math.abs(point.y - y), sx = x < point.x ? 1 : -1, sy = y < point.y ? 1 : -1;
  let error = dx + dy;
  while (true) {
    if (!strokePoints.value.some(p => p.x === x && p.y === y)) strokePoints.value.push({ x, y });
    if (x === point.x && y === point.y) break;
    const twice = 2 * error;
    if (twice >= dy) { error += dy; x += sx; }
    if (twice <= dx) { error += dx; y += sy; }
  }
  lastPoint = point;
}
function startStroke(event: PointerEvent) {
  if (!brush.value.enabled || props.data?.target.mode !== 'terrain' || props.busy || brushMaskLoading.value || brushMaskError.value || event.button !== 0 || strokeMeta.value || !Number.isInteger(brush.value.radius) || brush.value.radius < 1 || brush.value.radius > 8 || !form.value?.reportValidity()) return;
  event.preventDefault();
  pointerGrid = event.currentTarget as HTMLElement; pointerId = event.pointerId;
  if (!gridPoint(event)) { pointerGrid = null; return; }
  strokeMeta.value = { target: { ...props.data.target }, radius: brush.value.radius, randomVariation: brush.value.randomVariation, values: { ...fields.value } };
  strokePoints.value = []; lastPoint = null;
  pointerGrid.focus({ preventScroll: true }); pointerGrid.setPointerCapture(pointerId); collect(event);
}
function strokeKeyboard(event: KeyboardEvent) {
  if (strokeMeta.value && (event.ctrlKey || event.metaKey) && ['z', 'y'].includes(event.key.toLowerCase())) {
    event.preventDefault(); event.stopPropagation(); cancelStroke();
  }
}
function moveStroke(event: PointerEvent) { if (strokeMeta.value && event.pointerId === pointerId) collect(event); }
function cancelStroke() {
  const grid = pointerGrid, id = pointerId;
  strokeMeta.value = null; strokePoints.value = []; lastPoint = null; pointerGrid = null; pointerId = -1;
  if (grid?.hasPointerCapture(id)) grid.releasePointerCapture(id);
}
async function endStroke(event: PointerEvent) {
  if (!strokeMeta.value || event.pointerId !== pointerId) return;
  collect(event);
  const payload = { ...strokeMeta.value, points: strokePoints.value.map(p => ({ ...p })) };
  const coversCurrent = brushKeys.value.has(pointKey({ x: props.data?.target.x ?? 0, y: props.data?.target.y ?? 0 }));
  cancelStroke();
  if (payload.points.length && await props.execute('paintMapStroke', payload) && coversCurrent) resetDraft();
}
function preset(values: Record<string, number>) { fields.value = { ...fields.value, ...values }; }
watch(() => brush.value.enabled, enabled => {
  if (enabled && !brushTemplate.value) brushTemplate.value = { ...selectedDraft.value };
  if (!enabled) { cancelStroke(); brushHover.value = null; }
}, { flush: 'sync' });
const selectedDraftPending = computed(() => JSON.stringify(selectedDraft.value) !== JSON.stringify(stored()));
const pending = computed(() => JSON.stringify(fields.value) !== JSON.stringify(stored()));
let lastStored = JSON.stringify(stored()), lastTarget = JSON.stringify(props.data?.target);
watch(() => JSON.stringify([props.data?.target, stored()]), () => {
  const target = JSON.stringify(props.data?.target), clean = JSON.stringify(selectedDraft.value) === lastStored;
  if (target !== lastTarget || clean) selectedDraft.value = stored();
  lastStored = JSON.stringify(stored()); lastTarget = target;
  if (props.data?.target.mode !== 'terrain') brush.value.enabled = false;
});
function resetDraft() { selectedDraft.value = stored(); draftItem.value = null; }
function hasPending() { return selectedDraftPending.value; }
async function edit(action = 'apply', values = fields.value) {
  if (!props.data || !form.value?.reportValidity()) return false;
  const success = await props.execute('editMap', { target: { ...props.data.target }, values: { ...values }, action,
    autoExtensions: autoExtensions.value, noOverwrite: noOverwrite.value, snapFieldItems: snapFieldItems.value });
  if (success) resetDraft();
  return success;
}
async function applyPending() {
  if (strokeMeta.value || gridSelection.dragging.value) return false;
  return !selectedDraftPending.value || await edit('apply', selectedDraft.value);
}
function chooseItem(item: ItemView) {
  if (placement.mode.value === 'stamp') { placement.setTemplate({ itemId: item.itemId, systemParam: 0, additionalParam: 0, freeParam: 0 }); return; }
  draftItem.value = item;
  fields.value = { 'Map.ItemId': item.itemId, 'Map.SystemParam': 0, 'Map.AdditionalParam': 0, 'Map.FreeParam': 0 };
}
function chooseFieldObject(id: number) { chooseItem({ itemId: id, name: props.data?.fieldObjects.find(o => o.id === id)?.name } as ItemView); }
const overviewAcre = ref<number | null>(null);
const overviewOutline = ref<HTMLElement | null>(null);
let overviewFrame = 0;
let overviewPointer: { element: HTMLElement; id: number; box: DOMRect; offsetX: number; offsetY: number; x: number; y: number } | null = null;
const displayedAcre = computed(() => overviewAcre.value ?? (camera.value ? Math.round(camera.value.y) * 9 + Math.round(camera.value.x) : props.data?.target.acre ?? 0));
const overviewPosition = computed(() => overviewAcre.value !== null ? { x: displayedAcre.value % 9, y: Math.floor(displayedAcre.value / 9) }
  : camera.value ?? { x: displayedAcre.value % 9, y: Math.floor(displayedAcre.value / 9) });
const acreName = computed(() => props.data?.acres.find(a => a.id === props.data?.target.acre)?.name ?? '');
function paintOverview() {
  overviewFrame = 0;
  if (!overviewPointer || !overviewOutline.value) return;
  overviewOutline.value.style.left = `${overviewPointer.x / 9 * 100}%`;
  overviewOutline.value.style.top = `${overviewPointer.y / 8 * 100}%`;
}
function moveOverview(event: PointerEvent) {
  if (!overviewPointer) return;
  const p = overviewPointer;
  p.x = Math.max(0, Math.min(8, (event.clientX - p.box.left) / p.box.width * 9 - p.offsetX));
  p.y = Math.max(0, Math.min(7, (event.clientY - p.box.top) / p.box.height * 8 - p.offsetY));
  if (!overviewFrame) overviewFrame = requestAnimationFrame(paintOverview);
}
function overviewDown(event: PointerEvent) {
  if (props.busy || event.button !== 0 || overviewPointer) return;
  event.preventDefault();
  const element = event.currentTarget as HTMLElement;
  const box = element.getBoundingClientRect();
  const x = (event.clientX - box.left) / box.width * 9, y = (event.clientY - box.top) / box.height * 8;
  const currentX = displayedAcre.value % 9, currentY = Math.floor(displayedAcre.value / 9);
  const inside = x >= currentX && x < currentX + 1 && y >= currentY && y < currentY + 1;
  overviewPointer = { element, id: event.pointerId, box, offsetX: inside ? x - currentX : .5, offsetY: inside ? y - currentY : .5, x: currentX, y: currentY };
  overviewAcre.value = props.data?.target.acre ?? 0;
  moveOverview(event);
  element.focus({ preventScroll: true });
  element.setPointerCapture(event.pointerId);
}
function overviewMove(event: PointerEvent) {
  if (overviewPointer?.id === event.pointerId)
    moveOverview(event);
}
function overviewCancel() {
  if (overviewFrame) cancelAnimationFrame(overviewFrame);
  overviewFrame = 0;
  const pointer = overviewPointer;
  overviewPointer = null; overviewAcre.value = null;
  if (overviewOutline.value && props.data) {
    overviewOutline.value.style.left = `${(camera.value?.x ?? props.data.target.acre % 9) / 9 * 100}%`;
    overviewOutline.value.style.top = `${(camera.value?.y ?? Math.floor(props.data.target.acre / 9)) / 8 * 100}%`;
  }
  if (pointer?.element.hasPointerCapture(pointer.id)) pointer.element.releasePointerCapture(pointer.id);
}
function overviewLostCapture() { if (overviewPointer) overviewCancel(); }
async function overviewUp(event: PointerEvent) {
  if (overviewPointer?.id !== event.pointerId) return;
  moveOverview(event);
  const pointer = overviewPointer;
  const acre = Math.round(pointer.y) * 9 + Math.round(pointer.x);
  if (overviewFrame) cancelAnimationFrame(overviewFrame);
  overviewFrame = 0; overviewPointer = null; overviewAcre.value = acre;
  if (pointer.element.hasPointerCapture(pointer.id)) pointer.element.releasePointerCapture(pointer.id);
  try { if (!props.busy && acre !== props.data?.target.acre) await props.navigate({ acre }); }
  finally { overviewCancel(); }
}
function overviewClick(event: MouseEvent) {
  // Pointer gestures commit on release; keyboard activation keeps the current acre.
  if (event.detail === 0 && !props.busy) void props.navigate({ acre: displayedAcre.value });
}
onBeforeUnmount(() => { closePastePreview(false); overviewCancel(); cancelPan(); rememberView(); canvasObserver?.disconnect(); cancelAnimationFrame(canvasResizeFrame); window.removeEventListener('keydown', canvasKeyboard, true); window.removeEventListener('keyup', canvasKeyup); window.removeEventListener('blur', canvasBlur); });
defineExpose({ applyPending, chooseItem, hasPending, resetDraft });
</script>
<template>
  <section v-if="data" class="map-page">
    <header class="map-modebar">
      <div class="map-tabs" role="group" :aria-label="t('mapEditor')">
        <button v-for="(mode, i) in modes" :key="mode" :disabled="busy" :aria-pressed="data.target.mode === mode"
          @click="navigate({ mode, x: 0, y: 0 })"><component :is="modeIcons[i]" :size="16" />{{ t('mapModes').split('|')[i] }}</button>
      </div>
      <div class="map-layout-tools">
        <button :aria-pressed="showNavigator" :aria-label="t('mapToggleNavigator')" :title="t('mapToggleNavigator')" @click="showNavigator = !showNavigator"><Compass :size="16" /></button>
        <button :aria-pressed="showInspector" :aria-label="t('mapToggleInspector')" :title="t('mapToggleInspector')" @click="showInspector = !showInspector"><SlidersHorizontal :size="16" /></button>
        <button :aria-pressed="!showNavigator && !showInspector" @click="toggleFocus"><Maximize :size="16" />{{ t('mapFocusMode') }}</button>
        <button class="original-editor" :disabled="busy" :title="t('mapAdvanced')" @click="execute('nativeMap')">{{ t('mapOriginalEditor') }}</button>
      </div>
    </header>
    <div class="map-workspace" :class="{ 'navigator-collapsed': !showNavigator, 'inspector-collapsed': !showInspector }">
      <aside class="map-navigator map-panel">
        <div class="panel-heading"><Compass :size="16" /><h2>{{ t('mapNavigator') }}</h2></div>
        <div class="navigator-content">
          <div class="map-overview">
            <button class="map-overview-image" :class="{ 'is-dragging': overviewAcre !== null }" :disabled="busy" :aria-label="t('mapAcre')" @click="overviewClick"
              @pointerdown="overviewDown" @pointermove="overviewMove" @pointerup="overviewUp" @pointercancel="overviewCancel" @lostpointercapture="overviewLostCapture"
              @keydown.esc.prevent.stop="overviewCancel" @dragstart.prevent>
              <img v-if="data.overview" :src="data.overview" alt="" draggable="false" />
              <span ref="overviewOutline" class="acre-outline" :style="{ left: `${overviewPosition.x / 9 * 100}%`, top: `${overviewPosition.y / 8 * 100}%` }" />
            </button>
            <button v-for="pin in data.buildingPins ?? []" :key="pin.id" class="map-building-pin" :disabled="busy"
              :class="{ active: data.target.mode === 'buildings' && data.target.building === pin.id }"
              :style="{ left: `${pin.x / 288 * 100}%`, top: `${pin.y / 256 * 100}%` }"
              :title="`${pin.id + 1} · ${buildingName(pin)}`" :aria-label="`${pin.id + 1} · ${buildingName(pin)}`"
              @click="selectBuilding(pin)">{{ pin.id + 1 }}</button>
          </div>
          <div class="navigator-location">
            <p class="panel-hint">{{ t('mapNavigatorHint') }}</p>
            <div class="map-controls">
              <label>{{ t('mapAcre') }}<select :value="data.target.acre" :disabled="busy" @change="navigate({ acre: Number(($event.target as HTMLSelectElement).value) })"><option v-for="a in data.acres" :key="a.id" :value="a.id">{{ a.name }}</option></select></label>
              <label v-if="data.target.mode === 'items'">{{ t('mapLayer') }}<select :value="data.target.layer" :disabled="busy" @change="navigate({ layer: Number(($event.target as HTMLSelectElement).value) })"><option :value="0">0</option><option :value="1">1</option></select></label>
            </div>
            <div class="acre-directions">
              <button :disabled="busy || data.target.acre % 9 === 0" :aria-label="t('mapMoveNames').split('|')[0]" @click="moveAcre(-1, 0)"><ChevronLeft :size="17" /></button>
              <button :disabled="busy || data.target.acre < 9" :aria-label="t('mapMoveNames').split('|')[1]" @click="moveAcre(0, -1)"><ChevronUp :size="17" /></button>
              <button :disabled="busy || data.target.acre >= 63" :aria-label="t('mapMoveNames').split('|')[2]" @click="moveAcre(0, 1)"><ChevronDown :size="17" /></button>
              <button :disabled="busy || data.target.acre % 9 === 8" :aria-label="t('mapMoveNames').split('|')[3]" @click="moveAcre(1, 0)"><ChevronRight :size="17" /></button>
            </div>
          </div>
        </div>
        <section v-if="data.target.mode === 'buildings'" class="building-navigator">
          <h3>{{ t('mapBuildingList') }} <span>{{ data.buildingPins?.length ?? 0 }}</span></h3>
          <label class="building-search"><Search :size="15" /><input v-model="buildingSearch" type="search" :aria-label="t('mapSearchBuildings')" :placeholder="t('mapSearchBuildings')" /></label>
          <div class="map-building-list">
            <button v-for="pin in filteredBuildings" :key="pin.id" :disabled="busy" :aria-label="`${pin.id + 1} · ${buildingName(pin)}`" :aria-pressed="data.target.building === pin.id" @click="selectBuilding(pin)"><span class="building-number">{{ pin.id + 1 }}</span>{{ buildingName(pin) }}</button>
            <p v-if="!filteredBuildings.length" class="panel-hint">{{ t('mapNoBuildings') }}</p>
          </div>
        </section>
        <div class="map-legend"><span v-for="(name, i) in t('mapLegend').split('|')" :key="name"><i :class="`legend-${i}`" />{{ name }}</span></div>
      </aside>

      <section class="map-canvas-panel map-panel">
        <div class="canvas-toolbar">
          <div class="canvas-tools" role="group" :aria-label="t('mapCanvasTools')">
            <button :aria-pressed="!handTool && !brush.enabled && placement.mode.value === 'select'" :disabled="selectionBusy" :title="`${t('mapSelectTool')} · V`" :aria-label="t('mapSelectTool')" @click="setTool('select')"><MousePointer2 :size="18" /><span>{{ t('mapSelectTool') }}</span></button>
            <button v-if="data.target.mode === 'terrain'" :aria-pressed="!handTool && brush.enabled" :disabled="busy || !data.fields.length" :title="`${t('mapDragBrush')} · B`" :aria-label="t('mapDragBrush')" @click="setTool('brush')"><Paintbrush :size="18" /></button>
            <button :aria-pressed="handTool" :title="`${t('mapPanTool')} · H`" :aria-label="t('mapPanTool')" @click="setTool('pan')"><Hand :size="18" /><span>{{ t('mapPanTool') }}</span></button>
          </div>
          <div v-if="data.target.mode === 'items'" class="canvas-direct-tools">
            <button :aria-pressed="placement.mode.value === 'move'" :disabled="busy || selectedDraftPending" :title="t('mapMoveHint')" @click="placement.setMode('move')">{{ t('mapMoveObjects') }}</button>
            <button :aria-pressed="placement.mode.value === 'stamp'" :disabled="busy || selectedDraftPending" :title="t('mapStampHint')" @click="placement.setMode('stamp')">{{ t('mapStampObjects') }}</button>
          </div>
          <div class="canvas-display-tools">
            <button :aria-pressed="showGrid" :title="t('mapShowGrid')" :aria-label="t('mapShowGrid')" @click="showGrid = !showGrid"><Grid2X2 :size="17" /></button>
            <button :aria-pressed="showItems" :title="t('mapShowItems')" :aria-label="t('mapShowItems')" @click="showItems = !showItems"><Layers :size="17" /></button>
          </div>
          <div class="canvas-zoom">
            <button :disabled="zoom <= 1" :aria-label="t('mapZoomOut')" @click="setZoom(zoom - .25)"><ZoomOut :size="17" /></button>
            <output>{{ Math.round(zoom * 100) }}%</output>
            <button :disabled="zoom >= 4" :aria-label="t('mapZoomIn')" @click="setZoom(zoom + .25)"><ZoomIn :size="17" /></button>
            <button :title="t('mapFitView')" :aria-label="t('mapFitView')" @click="fitCanvas"><Maximize :size="17" /></button>
            <MapOverviewDialog :overview="data.overview" :acre="data.target.acre" :acres="data.acres" :busy="busy" :navigate="navigate" />
          </div>
        </div>
        <div ref="canvasScroll" class="map-canvas-scroll" :class="{ 'hand-tool': panMode, panning }" tabindex="0"
          @pointerenter="canvasHovered = true" @pointerleave="canvasHovered = false" @pointerdown.capture="startPan" @pointermove="canvasPointerMove" @pointerup="canvasPointerUp" @pointercancel="canvasPointerCancel" @lostpointercapture="placement.lostCapture($event); cancelPan()" @click.capture="canvasClick" @auxclick.prevent @wheel="canvasWheel">
          <div class="map-canvas-stage">
            <div v-if="camera && data.overview" class="map-pan-preview" aria-hidden="true"><img :src="data.overview" draggable="false" :style="{ width: `${canvasSize * 9}px`, height: `${canvasSize * 8}px`, left: `calc(50% - ${(camera.x + .5) * canvasSize}px)`, top: `calc(50% - ${(camera.y + .5) * canvasSize}px)` }" /></div>
            <div class="map-viewport" :style="{ width: `${canvasSize}px`, height: `${canvasSize}px`, transform: `translate(${canvasOffset.x}px, ${canvasOffset.y}px)` }">
              <span class="edit-acre-label" :title="t('mapBoundaryHint')">{{ acreName }} · {{ t('mapEditAcre') }}</span>
              <img v-if="data.viewport" :src="data.viewport" alt="" draggable="false" />
              <img v-if="data.itemImage" v-show="showItems" class="map-item-layer" :src="data.itemImage" alt="" draggable="false" />
              <div v-show="showGrid" class="map-grid-lines" :style="{ '--grid-size': ['terrain', 'items'].includes(data.target.mode) ? data.gridSize : 32 }" />
              <button v-for="pin in data.target.mode === 'buildings' ? visibleBuildings : []" :key="pin.id"
                class="map-building-label" :class="{ 'map-building-number': data.target.building !== pin.id }" :disabled="busy" :aria-pressed="data.target.building === pin.id"
                :aria-label="`${pin.id + 1} · ${buildingName(pin)}`"
                :style="{ left: data.target.building === pin.id ? `clamp(32.5%, ${pin.x % 32 / 32 * 100}%, 67.5%)` : `${pin.x % 32 / 32 * 100}%`, top: `clamp(1rem, ${pin.y % 32 / 32 * 100}%, calc(100% - 1rem))` }"
                @pointerdown="data.target.building === pin.id && !panMode && placement.down($event)" @click="selectBuilding(pin)">{{ data.target.building === pin.id ? `${pin.id + 1} · ${buildingName(pin)}` : pin.id + 1 }}</button>
              <div v-if="placement.buildingGhost.value" class="building-drag-ghost" :style="{ left: `${(placement.buildingGhost.value.x - data.target.acre % 9 * 32) / 32 * 100}%`, top: `${(placement.buildingGhost.value.y - Math.floor(data.target.acre / 9) * 32) / 32 * 100}%` }">{{ selectionName }}</div>
              <div v-if="['terrain', 'items'].includes(data.target.mode)" class="map-grid" tabindex="0" role="group" :aria-label="t('gridArea')" :class="{ 'brush-enabled': brush.enabled && data.target.mode === 'terrain' }"
                @pointerdown="gridPointerDown" @pointermove="gridPointerMove" @pointerleave="brushHover = null; placement.leave()" @pointerup="gridPointerUp" @pointercancel="cancelGridPointer" @lostpointercapture="gridLostCapture" @keydown="gridKeydown"
                :aria-busy="brushEditing && brushMaskLoading" :data-columns="data.gridSize" :data-rows="data.gridSize" :style="{ gridTemplateColumns: `repeat(${data.gridSize}, 1fr)` }">
                <button tabindex="-1" :aria-pressed="gridSelection.keys.value.has(pointKey(cell))" v-for="cell in data.cells" :key="`${cell.x},${cell.y}`" :disabled="selectionBusy || !cell.valid"
                  :aria-label="`${cell.x}, ${cell.y}${cell.name ? ' · ' + cell.name : ''}`" :title="`${cell.relativeX}, ${cell.relativeY} ${cell.name}`"
                  :class="{ selected: gridSelection.selected.value.length === 1 && gridSelection.keys.value.has(pointKey(cell)), occupied: !!cell.name, extension: cell.extension, 'stroke-preview': strokeKeys.has(`${cell.x},${cell.y}`), 'brush-hover': hoverBrushKeys.has(pointKey(cell)), 'paste-preview': pasteKeys.has(pointKey(cell)), 'paste-conflict': pasteConflictKeys.has(pointKey(cell)), 'paste-placed': pastePlacedKeys.has(pointKey(cell)), 'paste-cleared': pasteClearedKeys.has(pointKey(cell)), 'placement-preview': placement.placedKeys.value.has(pointKey(cell)), 'placement-conflict': placement.conflictKeys.value.has(pointKey(cell)), 'placement-cleared': placement.clearedKeys.value.has(pointKey(cell)), 'multi-selected': gridSelection.keys.value.has(pointKey(cell)) }"
                  @click="placement.mode.value === 'select' && !brush.enabled && !handTool && gridSelection.click($event, cell)" />
              </div>
            </div>
          </div>
        </div>
        <div class="canvas-status"><span v-if="['terrain', 'items'].includes(data.target.mode)">{{ acreName }} · <span>X {{ data.target.x }} · Y {{ data.target.y }}</span><template v-if="currentCell?.valid"> · {{ t('mapIslandCoordinate') }} {{ currentCell.relativeX }}, {{ currentCell.relativeY }}</template></span><span v-else>{{ acreName }}</span><span :class="{ 'loading-view': loading }">{{ loading ? t('mapLoadingView') : t(panMode || panning ? 'mapPanCanvasHint' : brush.enabled ? 'mapBrushCanvasHint' : 'mapCanvasHint') }}</span></div>
        <GridToolbar v-if="['terrain', 'items'].includes(data.target.mode)" :count="gridSelection.selected.value.length" :clipboard="clipboard" :kind="data.target.mode === 'terrain' ? 'terrain' : 'items'" :busy="busy || !!strokeMeta || gridSelection.dragging.value" :action="gridAction" :clear="gridSelection.clear" :preview-paste="true" />
        <MapPastePreview v-if="pasteOpen" :preview="pastePreview" :loading="pasteLoading" :busy="busy" :outside="pasteOutside" :pending="pending" :confirm="confirmPaste" :cancel="closePastePreview" :locate-conflict="locatePasteConflict" />
      </section>

      <aside class="map-inspector map-panel">
        <div class="panel-heading"><SlidersHorizontal :size="16" /><h2>{{ t('mapProperties') }}</h2><span class="draft-badge" :class="{ pending }">{{ t(brushEditing ? 'mapBrushTemplate' : selectedDraftPending ? 'mapDraftPending' : dirty ? 'mapDraftApplied' : 'saved') }}</span></div>
        <div class="inspector-scroll">
          <div class="selection-card">
            <img v-if="draftItem?.image" :src="draftItem.image" alt="" />
            <div v-else class="selection-symbol"><component :is="modeIcons[modes.indexOf(data.target.mode)]" :size="22" /></div>
            <div><span>{{ brushEditing ? t('mapBrushTemplate') : gridSelection.selected.value.length > 1 ? `${t('mapCurrentSelection')} · ${t('gridSelected').replace('{0}', String(gridSelection.selected.value.length))}` : t('mapCurrentCell') }}</span><strong>{{ selectionName }}</strong></div>
          </div>
          <div v-if="brushEditing" class="brush-template-status">
            <p>{{ t('mapTemplateHint') }}</p>
            <p v-if="selectedDraftPending">{{ t('mapSelectedDraftHint') }}</p>
            <p v-if="brushMaskLoading" role="status">{{ t('mapBrushMaskLoading') }}</p>
            <p v-if="brushMaskError" role="alert">{{ t('mapBrushMaskError') }}</p>
            <p v-if="brushOutside">{{ t('mapBrushOutside').replace('{0}', String(brushOutside)) }}</p>
          </div>
          <form v-if="data.fields.length" id="map-property-form" ref="form" class="map-property-form" @submit.prevent="edit()">
            <div class="property-fields">
              <div v-if="data.target.mode === 'buildings'" class="map-controls"><label>{{ t('mapModes').split('|')[2] }}<select :value="data.target.building" :disabled="busy" @change="chooseBuilding(Number(($event.target as HTMLSelectElement).value))"><option v-for="b in data.buildings" :key="b.id" :value="b.id">{{ buildingChoice(b.id, b.name) }}</option></select></label></div>
              <div v-if="data.target.mode === 'markers'" class="map-controls"><label>{{ t('mapSlot') }}<input type="number" min="0" :max="data.markerCount - 1" :value="data.target.marker" :disabled="busy" @change="($event.target as HTMLInputElement).reportValidity() && navigate({ marker: Number(($event.target as HTMLInputElement).value) })" /></label><p class="panel-hint">{{ t('mapSlotHint') }} (0–{{ data.markerCount - 1 }})</p></div>
              <div v-if="data.target.mode === 'items'" class="item-picker-controls">
                <button type="button" class="pick-item" :disabled="busy" @click="pickItem"><Package :size="17" />{{ t('mapPickItem') }}</button>
                <select :aria-label="t('mapFieldObject')" :value="data.fieldObjects.some(o => o.id === fields['Map.ItemId']) ? fields['Map.ItemId'] : ''" :disabled="busy" @change="chooseFieldObject(Number(($event.target as HTMLSelectElement).value))"><option value="" disabled>{{ t('mapFieldObject') }}</option><option v-for="o in data.fieldObjects" :key="o.id" :value="o.id">{{ o.name }}</option></select>
              </div>
              <HousingFields v-model="fields" :fields="primaryFields" :busy="busy" />
              <details v-if="advancedFields.length" class="map-raw-fields"><summary>{{ t('mapMoreParameters') }}</summary><HousingFields v-model="fields" :fields="advancedFields" :busy="busy" /></details>
              <details v-if="data.target.mode === 'items'" class="map-placement-options"><summary>{{ t('mapPlacementOptions') }}</summary><label><input v-model="autoExtensions" type="checkbox" :disabled="busy" />{{ t('mapExtensions') }}</label><label><input v-model="noOverwrite" type="checkbox" :disabled="busy" />{{ t('mapNoOverwrite') }}</label><label><input v-model="snapFieldItems" type="checkbox" :disabled="busy" />{{ t('mapSnap') }}</label></details>
              <p v-if="data.target.mode === 'items' || data.target.mode === 'buildings'" class="panel-hint">{{ t(data.target.mode === 'items' ? 'mapItemReplaceHint' : 'mapBuildingReplaceHint') }}</p>
            </div>
          </form>
          <p v-else class="map-outside">{{ t('mapOutside') }}</p>
          <MapPlacementTools v-if="data.target.mode === 'items'" :mode="placement.mode.value" :busy="busy || placement.committing.value" :pending="selectedDraftPending" :checking="placement.checking.value" :preview="placement.preview.value" :rotation="(placement.template.value?.systemParam ?? 0) & 3" :set-mode="placement.setMode" :pick-item="pickItem" :use-current-item="placement.useCurrentItem" :cancel="placement.cancel" />
          <MapTools v-model:brush="brush" @preset="preset" @painted="resetDraft" :data="data" :busy="busy" :values="fields" :execute="execute" :apply-pending="applyPending" :navigate="navigate" :validate="() => !form || form.reportValidity()" />
          <MapAdvanced @applied="fields = stored()" :data="data" :values="fields" :busy="busy" :execute="execute" :validate="() => !form || form.reportValidity()" />
        </div>
        <div v-if="data.fields.length" class="map-actions"><button type="submit" form="map-property-form" class="primary-action" :disabled="busy || !pending">{{ t(brushEditing ? 'mapApplyTemplate' : data.target.mode === 'items' || data.target.mode === 'buildings' ? 'mapReplaceSelected' : 'apply') }}</button><button v-if="selectedDraftPending" type="button" :disabled="busy" @click="resetDraft">{{ t('mapResetDraft') }}</button><button v-if="['terrain', 'items'].includes(data.target.mode)" type="button" :disabled="busy" @click="edit('delete')">{{ t('mapClear') }}</button><button v-if="data.target.mode === 'terrain'" type="button" :disabled="busy" @click="edit('rotate')">{{ t('mapRotate') }}</button></div>
      </aside>
    </div>
  </section>
</template>
<style scoped>
.map-page { display: flex; flex-direction: column; gap: 12px; min-width: 0; min-height: 0; flex: 1; container-type: inline-size; }
button, select, input[type=number], input[type=search] { font: inherit; box-sizing: border-box; max-width: 100%; padding: 8px 10px; border: 1px solid var(--line-strong); border-radius: 7px; color: inherit; background: var(--surface); }
button { display: inline-flex; justify-content: center; align-items: center; gap: 7px; }
button[aria-pressed=true] { color: var(--green); background: var(--hover); border-color: var(--green); }
button:hover:not(:disabled) { background: var(--hover); }
button svg { flex-shrink: 0; }
.map-modebar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; flex-shrink: 0; }
.map-tabs { display: flex; gap: 3px; padding: 4px; border: 1px solid var(--line); border-radius: 10px; background: var(--surface); }
.map-tabs button { font-size: 12px; padding: 8px 12px; border-color: transparent; background: transparent; }
.map-tabs button[aria-pressed=true] { background: var(--green); color: #fff; box-shadow: 0 2px 5px #153c2320; }
.original-editor { font-size: 11px; color: var(--muted); background: transparent; }
.map-workspace { display: grid; grid-template-columns: 200px minmax(0, 1fr) 300px; gap: 12px; flex: 1; min-height: 0; }
.map-panel { border: 1px solid var(--line); border-radius: 12px; background: var(--surface); overflow: hidden; min-width: 0; }
.panel-heading { min-height: 46px; display: flex; gap: 8px; align-items: center; padding: 10px 14px; border-bottom: 1px solid var(--line); flex-shrink: 0; color: var(--green); }
.panel-heading h2 { font-size: 12px; font-weight: 650; margin: 0; color: var(--ink); }
h3 { font-size: 12px; margin: 0; }
.panel-hint { margin: 0; color: var(--muted); font-size: 11px; line-height: 1.65; }
.map-navigator { display: flex; flex-direction: column; overflow-y: auto; }
.navigator-content { padding: 12px; }
.map-overview { position: relative; aspect-ratio: 9/8; overflow: hidden; }
.map-overview-image { display: block; position: absolute; inset: 0; width: 100%; height: 100%; padding: 0; overflow: hidden; border: 0; border-radius: 0; touch-action: none; user-select: none; cursor: grab; }
.map-overview-image.is-dragging { cursor: grabbing; }
.map-overview img { width: 100%; height: 100%; display: block; image-rendering: pixelated; }
.acre-outline { position: absolute; width: calc(100% / 9); height: 12.5%; border: 2px solid #ffdf63; box-shadow: 0 0 0 1px #634b2080; box-sizing: border-box; pointer-events: none; }
.navigator-location { display: grid; gap: 10px; margin-top: 10px; }
.map-controls { display: grid; gap: 10px; }
.map-controls label { display: grid; gap: 6px; min-width: 0; font-size: 11px; color: var(--muted); }
.map-controls select, .map-controls input { width: 100%; color: var(--ink); }
.acre-directions { display: flex; gap: 5px; }
.acre-directions button { flex: 1; padding: 6px; }
.map-building-pin { position: absolute; transform: translate(-50%, -50%); padding: 0; width: 17px; height: 17px; border: 1px solid white; border-radius: 50%; background: #245b49; color: white; font: bold 9px/1 sans-serif; box-shadow: 0 1px 3px #0005; }
.map-building-pin.active { background: #b55c28; color: white; outline: 2px solid #ffcf40; z-index: 2; }
.building-navigator { padding: 12px; border-top: 1px solid var(--line); display: grid; gap: 10px; }
.building-navigator h3 { display: flex; justify-content: space-between; }
.building-navigator h3 span { color: var(--muted); font-weight: 400; }
.building-search { display: flex; align-items: center; gap: 5px; border: 1px solid var(--line); border-radius: 6px; padding: 0 7px; color: var(--muted); }
.building-search input { min-width: 0; width: 100%; padding: 7px 0; border: 0; background: transparent; font-size: 11px; }
.map-building-list { display: grid; gap: 4px; }
.map-building-list button { justify-content: flex-start; text-align: left; padding: 7px; font-size: 11px; border-color: transparent; }
.building-number { display: grid; place-items: center; min-width: 20px; height: 20px; border-radius: 5px; background: var(--hover); color: var(--green); font-size: 10px; }
.map-legend { display: grid; grid-template-columns: 1fr 1fr; gap: 9px; padding: 14px 12px; margin-top: auto; border-top: 1px solid var(--line); }
.map-legend span { display: flex; gap: 6px; align-items: center; color: var(--muted); font-size: 10px; }
.map-legend i { width: 9px; height: 9px; border-radius: 2px; }
.legend-0 { background: #63a86a; }.legend-1 { background: #80d7c3; }.legend-2 { background: #ee8bc3; }.legend-3 { background: #e7bb41; }
.map-canvas-panel { position: relative; display: flex; flex-direction: column; min-height: 0; }
.canvas-toolbar { display: flex; gap: 8px; align-items: center; padding: 9px 10px; border-bottom: 1px solid var(--line); flex-shrink: 0; flex-wrap: wrap; }
.canvas-tools, .canvas-display-tools, .canvas-zoom { display: flex; align-items: center; gap: 3px; }
.canvas-toolbar button { border-color: transparent; padding: 6px; min-width: 30px; min-height: 30px; background: transparent; font-size: 11px; }
.canvas-toolbar button[aria-pressed=true] { background: var(--hover); border-color: var(--line-strong); }
.canvas-tools { padding-right: 6px; border-right: 1px solid var(--line); }
.canvas-display-tools { margin-right: auto; }
.canvas-zoom output { min-width: 37px; font-size: 10px; text-align: center; font-variant-numeric: tabular-nums; color: var(--muted); }
.map-canvas-scroll { scrollbar-gutter: stable; flex: 1; min-height: 220px; overflow: auto; background-color: var(--canvas); background-image: radial-gradient(var(--line-strong) .7px, transparent .7px); background-size: 14px 14px; overscroll-behavior: contain; }
.map-canvas-stage { position: relative; overflow: clip; display: flex; align-items: center; justify-content: center; width: max-content; min-width: 100%; min-height: 100%; padding: 24px; box-sizing: border-box; }
.map-pan-preview { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
.map-pan-preview img { opacity: .65; position: absolute; max-width: none; image-rendering: pixelated; }
.map-viewport { position: relative; flex: 0 0 auto; border: 1px solid #548c73; outline: 1px dashed #548c7380; outline-offset: 2px; isolation: isolate; background: #ffffed; box-shadow: 0 6px 24px #182b1814; }
.map-viewport img { display: block; width: 100%; height: 100%; image-rendering: pixelated; pointer-events: none; }
.map-viewport img, .map-grid, .map-grid-lines { position: absolute; inset: 0; }
.map-grid-lines { pointer-events: none; background-image: linear-gradient(to right, #4b554b70 1px, transparent 1px), linear-gradient(to bottom, #4b554b70 1px, transparent 1px); background-size: calc(100% / var(--grid-size)) calc(100% / var(--grid-size)); }
.map-building-label { position: absolute; transform: translate(-50%, -50%); max-width: 65%; padding: .3rem .5rem; background: var(--surface); font-size: .8rem; z-index: 2; }
.map-building-label.map-building-number { padding: .2rem; min-width: 1.5rem; border-radius: 50%; z-index: 1; }
.map-grid { display: grid; touch-action: none; user-select: none; }
.map-grid button { display: block; padding: 0; min-width: 0; border: 0; background: transparent; border-radius: 0; }
.map-grid button:disabled { background: #00000012; opacity: 1; }
.map-grid button.multi-selected { box-shadow: inset 0 0 0 1px #1879cd; background: #2f8fe830; }
.map-grid button:hover:not(:disabled) { box-shadow: inset 0 0 0 1px #176cbb; background: #ffffff25; }
.map-grid button.selected { outline: 2px solid #ed651c; outline-offset: -2px; background: #ffb52b30; z-index: 1; }
.map-grid.brush-enabled { cursor: crosshair; }
.map-grid button.brush-hover { background: #36acfa38; box-shadow: inset 0 0 0 1px #278aca; }
.map-grid button.stroke-preview { background: #36acfa80; }
.map-grid button.paste-preview { background: #f3ba4080; box-shadow: inset 0 0 0 1px #a66a00; }
.map-grid button.paste-placed { background: #f3ba4080; }
.map-grid button.paste-cleared { background: repeating-linear-gradient(135deg, #637b8870 0 2px, transparent 2px 5px); }
.map-grid button.paste-conflict { background: repeating-linear-gradient(45deg, #d4474070 0 2px, transparent 2px 5px); box-shadow: inset 0 0 0 2px #bd2626; }
.hand-tool, .hand-tool * { cursor: grab !important; touch-action: none; }
.hand-tool.panning, .hand-tool.panning * { cursor: grabbing !important; }
.canvas-status { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 8px 12px; font-size: 10px; color: var(--muted); border-top: 1px solid var(--line); }
.canvas-status span:first-child { flex-shrink: 0; font-variant-numeric: tabular-nums; color: var(--ink); }
.loading-view { color: var(--green); }
.map-canvas-panel :deep(.grid-toolbar) { padding: 8px 10px; margin: 0; border-top: 1px solid var(--line); font-size: 10px; flex-shrink: 0; }
.map-canvas-panel :deep(.grid-actions) { gap: 4px; }
.map-canvas-panel :deep(.grid-actions button) { padding: 5px 6px; border-color: transparent; font-size: 10px; }
.map-canvas-panel :deep(.grid-actions kbd) { display: none; }
.map-canvas-panel :deep(.grid-toolbar details) { margin-top: 4px; }
.map-canvas-panel :deep(.grid-toolbar details p) { font-size: 10px; }
.map-inspector { display: flex; flex-direction: column; min-height: 0; }
.draft-badge { margin-left: auto; white-space: nowrap; padding: 3px 6px; border-radius: 4px; font-size: 9px; color: var(--muted); background: var(--canvas); }
.draft-badge.pending { color: #9c641e; background: #e6b24a1c; }
.inspector-scroll { overflow-y: auto; min-height: 0; overscroll-behavior: contain; }
.selection-card { display: flex; gap: 10px; padding: 14px; align-items: center; border-bottom: 1px solid var(--line); }
.selection-symbol, .selection-card img { width: 40px; height: 40px; flex: 0 0 auto; display: grid; place-items: center; background: var(--hover); border-radius: 9px; color: var(--green); object-fit: contain; }
.selection-card div:last-child { min-width: 0; }
.selection-card span { display: block; color: var(--muted); font-size: 10px; margin-bottom: 5px; }
.selection-card strong { display: block; font-size: 12px; font-weight: 600; line-height: 1.5; overflow-wrap: anywhere; }
.map-property-form { display: grid; }
.property-fields { padding: 14px; display: grid; gap: 14px; }
.map-inspector :deep(.housing-fields) { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 10px; }
.map-inspector :deep(.housing-fields label) { font-size: 11px; gap: 6px; }
.map-inspector :deep(.housing-fields input), .map-inspector :deep(.housing-fields select) { padding: 7px 8px; border-radius: 6px; font-size: 12px; }
.item-picker-controls { display: grid; gap: 8px; }
.item-picker-controls .pick-item { background: var(--green); border-color: var(--green); color: #fff; }
.item-picker-controls select { width: 100%; font-size: 11px; }
.map-raw-fields, .map-placement-options { border-top: 1px solid var(--line); padding-top: 10px; font-size: 11px; }
summary { cursor: pointer; color: var(--ink); font-weight: 550; }
.map-raw-fields :deep(.housing-fields) { padding-top: 12px; }
.map-placement-options label { display: flex; align-items: center; gap: 7px; margin-top: 10px; color: var(--muted); font-size: 11px; }
.map-placement-options input { width: auto; margin: 0; }
.map-actions { display: flex; gap: 6px; flex-wrap: wrap; padding: 10px 14px; background: var(--surface); border-top: 1px solid var(--line); flex-shrink: 0; }
.map-actions button { font-size: 11px; flex: 1; white-space: nowrap; }
.map-actions .primary-action { background: var(--green); color: white; border-color: var(--green); flex-basis: 100%; }
.map-outside { padding: 14px; color: var(--muted); font-size: 12px; }
.map-inspector :deep(.map-tools), .map-inspector :deep(.map-advanced) { padding: 14px; border-top: 1px solid var(--line); gap: 12px; font-size: 11px; }
.map-inspector :deep(.map-tools p), .map-inspector :deep(.map-advanced p) { font-size: 11px; color: var(--muted); line-height: 1.65; }
.map-inspector :deep(.tool-row), .map-inspector :deep(.advanced-row) { gap: 8px; align-items: stretch; }
.map-inspector :deep(.tool-row label), .map-inspector :deep(.advanced-row label) { width: 100%; display: grid; grid-template-columns: 1fr; gap: 6px; }
.map-inspector :deep(.map-tools select), .map-inspector :deep(.map-advanced select) { width: 100%; min-width: 0; }
.map-inspector :deep(.map-tools input[type=number]), .map-inspector :deep(.map-advanced input) { width: 100%; min-width: 0; }
.map-inspector :deep(.tool-row input[type=checkbox]) { width: auto; }
.map-inspector :deep(label:has(> input[type=checkbox])) { display: flex; flex-direction: row; align-items: center; gap: 7px; }
.map-inspector :deep(label > input[type=checkbox]) { width: auto; margin: 0; flex: 0 0 auto; }
.map-inspector :deep(.map-replace h3) { font-size: 12px; }
.map-inspector :deep(.rule-row), .map-inspector :deep(.change-row) { display: grid; grid-template-columns: minmax(0, 1fr) 32px; }
.map-inspector :deep(.rule-row select), .map-inspector :deep(.change-row select), .map-inspector :deep(.rule-row input), .map-inspector :deep(.change-row input) { grid-column: 1; }
.map-inspector :deep(.rule-row button), .map-inspector :deep(.change-row button) { grid-column: 2; grid-row: 1; }
@container (max-width: 1120px) {
  .map-workspace { grid-template-columns: minmax(0, 1fr) 280px; grid-template-rows: auto minmax(0, 1fr); }
  .map-navigator { grid-column: 2; grid-row: 1; max-height: 220px; }
  .map-navigator .panel-heading, .map-navigator .map-legend { display: none; }
  .navigator-content { display: flex; gap: 10px; }
  .map-overview { width: 120px; flex: 0 0 120px; height: 107px; }
  .navigator-location { flex: 1; min-width: 0; margin: 0; gap: 7px; }
  .navigator-location .panel-hint { display: none; }
  .navigator-location .map-controls { gap: 5px; }
  .navigator-location .map-controls label { display: flex; align-items: center; gap: 5px; font-size: 10px; }
  .navigator-location .map-controls select { width: auto; flex: 1; padding: 4px; }
  .acre-directions { gap: 2px; }
  .acre-directions button { padding: 2px; min-width: 0; }
  .map-canvas-panel { grid-column: 1; grid-row: 1 / span 2; }
  .map-inspector { grid-column: 2; grid-row: 2; }
  .canvas-tools button span { display: none; }
}
@container (max-width: 700px) {
  .map-modebar { gap: 6px; }.map-tabs { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); width: 100%; }.map-tabs button { padding: 7px; font-size: 11px; }
  .map-workspace { display: flex; flex-direction: column; }
  .map-navigator { max-height: none; }.map-navigator .panel-heading { display: flex; }
  .navigator-content { display: flex; }.map-overview { width: 140px; height: 125px; flex-basis: 140px; }
  .navigator-location .map-controls label { font-size: 11px; }
  .map-canvas-panel { min-height: 440px; height: 60dvh; flex-shrink: 0; }
  .map-inspector { min-height: 0; overflow: visible; }.inspector-scroll { overflow: visible; }
  .canvas-status span:last-child { max-width: 65%; text-align: right; }
  .canvas-tools button span { display: inline; }
}
:global(.workspace-content.map-workspace-page) { display: flex; flex-direction: column; padding: 16px 20px; overflow: hidden; }
:global(.map-workspace-page .workspace-heading) { margin-bottom: 12px; flex-shrink: 0; }
:global(.map-workspace-page .workspace-heading h1) { font-size: 22px; }
:global(.map-workspace-page .workspace-heading .eyebrow) { font-size: 10px; margin-bottom: 3px; }
@media (max-width: 900px) { :global(.workspace-content.map-workspace-page) { overflow-y: auto; padding: 14px; }.map-page { flex: 0 0 auto; } }
.map-layout-tools { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
.map-canvas-panel > :deep(.map-paste-preview) { position: absolute; inset-inline: 10px; bottom: 10px; max-height: calc(100% - 70px); overflow-y: auto; z-index: 6; box-shadow: 0 5px 28px #15271c30; }
.edit-acre-label { position: absolute; left: 0; top: 0; transform: translateY(-100%); color: var(--green); background: var(--surface); font-size: 10px; padding: 3px 5px; pointer-events: none; white-space: nowrap; z-index: 3; }
.brush-template-status { padding: 8px 14px; border-bottom: 1px solid var(--line); color: var(--muted); font-size: 11px; line-height: 1.6; }
.navigator-collapsed > .map-navigator, .inspector-collapsed > .map-inspector { display: none; }
.map-workspace.navigator-collapsed { grid-template-columns: minmax(0, 1fr) 300px; }
.navigator-collapsed > .map-canvas-panel { grid-column: 1; grid-row: 1; }
.navigator-collapsed > .map-inspector { grid-column: 2; grid-row: 1; }
.map-workspace.inspector-collapsed { grid-template-columns: 200px minmax(0, 1fr); }
.inspector-collapsed > .map-navigator { grid-column: 1; grid-row: 1; }
.inspector-collapsed > .map-canvas-panel { grid-column: 2; grid-row: 1; }
.map-workspace.navigator-collapsed.inspector-collapsed { grid-template-columns: minmax(0, 1fr); }
.navigator-collapsed.inspector-collapsed > .map-canvas-panel { grid-column: 1; }
@container (max-width: 1120px) {
  .map-workspace.inspector-collapsed:not(.navigator-collapsed) { grid-template-columns: minmax(0, 1fr) 200px; }
  .inspector-collapsed:not(.navigator-collapsed) > .map-navigator { grid-column: 2; grid-row: 1; }
  .inspector-collapsed:not(.navigator-collapsed) > .map-canvas-panel { grid-column: 1; grid-row: 1; }
}
.canvas-direct-tools { display: flex; gap: 4px; }
.canvas-direct-tools button { font-size: 10px; padding: 6px; }
.building-drag-ghost { position: absolute; z-index: 4; pointer-events: none; transform: translate(-50%, -50%); padding: .35rem; border: 2px dashed var(--green); background: var(--surface); opacity: .8; white-space: nowrap; font-size: .75rem; }
.map-grid button.placement-preview { background: #f3ba4080; box-shadow: inset 0 0 0 1px #a66a00; }
.map-grid button.placement-conflict { background: repeating-linear-gradient(45deg, #d4474070 0 2px, transparent 2px 5px); box-shadow: inset 0 0 0 2px #bd2626; }
.map-grid button.placement-cleared { background: repeating-linear-gradient(135deg, #637b8870 0 2px, transparent 2px 5px); }
</style>
