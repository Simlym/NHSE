import { nextTick, type Ref } from 'vue';

type Point = { x: number; y: number };
interface Snapshot { scope: string; zoom: number; camera: Point | null; left: number; top: number }
let saved: Snapshot | null = null, epoch = 0;
export function clearMapCameraMemory() { saved = null; ++epoch; }

export function useMapCamera(options: {
  element: Ref<HTMLElement | null>; zoom: Ref<number>; baseSize: Ref<number>; camera: Ref<Point | null>;
  acre: () => number; scope: () => string; cancelPan: () => void; interactionBusy?: () => boolean;
}) {
  const mountedEpoch = epoch;
  let sequence = 0;
  const clamp = (value: number, maximum: number) => Math.max(0, Math.min(maximum, value));
  async function setZoom(value: number, anchor?: { clientX: number; clientY: number }) {
    if (!Number.isFinite(value) || options.interactionBusy?.()) return;
    options.cancelPan();
    const element = options.element.value, token = ++sequence;
    const viewport = element?.querySelector<HTMLElement>('.map-viewport');
    const frame = element?.getBoundingClientRect(), before = viewport?.getBoundingClientRect();
    const x = anchor?.clientX ?? (frame ? frame.left + element!.clientLeft + element!.clientWidth / 2 : 0);
    const y = anchor?.clientY ?? (frame ? frame.top + element!.clientTop + element!.clientHeight / 2 : 0);
    const point = before && viewport?.clientWidth && viewport.clientHeight ? {
      x: (x - before.left - viewport.clientLeft) / viewport.clientWidth,
      y: (y - before.top - viewport.clientTop) / viewport.clientHeight,
    } : null;
    options.zoom.value = Math.max(1, Math.min(4, value));
    await nextTick();
    if (!element || !viewport || !point || token !== sequence) return;
    // Native scroll absorbs movement first; the camera handles the remainder at scroll boundaries.
    let current = viewport.getBoundingClientRect();
    element.scrollLeft += current.left + viewport.clientLeft + point.x * viewport.clientWidth - x;
    element.scrollTop += current.top + viewport.clientTop + point.y * viewport.clientHeight - y;
    current = viewport.getBoundingClientRect();
    const dx = current.left + viewport.clientLeft + point.x * viewport.clientWidth - x;
    const dy = current.top + viewport.clientTop + point.y * viewport.clientHeight - y;
    if (Math.abs(dx) > .1 || Math.abs(dy) > .1) {
      const camera = options.camera.value ?? { x: options.acre() % 9, y: Math.floor(options.acre() / 9) };
      const size = options.baseSize.value * options.zoom.value;
      options.camera.value = { x: clamp(camera.x + dx / size, 8), y: clamp(camera.y + dy / size, 7) };
    }
  }
  async function fitCanvas() {
    if (options.interactionBusy?.()) return;
    options.cancelPan(); ++sequence;
    options.camera.value = null; options.zoom.value = 1;
    await nextTick(); options.element.value?.scrollTo({ left: 0, top: 0 });
  }
  function canvasWheel(event: WheelEvent) {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    if (event.deltaY) void setZoom(options.zoom.value + (event.deltaY < 0 ? .25 : -.25), event);
  }
  function rememberView() {
    const element = options.element.value;
    if (!element || epoch !== mountedEpoch) return;
    saved = { scope: options.scope(), zoom: options.zoom.value, camera: options.camera.value && { ...options.camera.value }, left: element.scrollLeft, top: element.scrollTop };
  }
  async function restoreView() {
    const view = saved;
    if (!view || epoch !== mountedEpoch || view.scope !== options.scope()) return;
    options.zoom.value = view.zoom; options.camera.value = view.camera && { ...view.camera };
    await nextTick();
    // Restore scroll after the container's initial ResizeObserver measurement has reached layout.
    await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    if (epoch === mountedEpoch) options.element.value?.scrollTo({ left: view.left, top: view.top });
  }
  return { setZoom, fitCanvas, canvasWheel, rememberView, restoreView };
}
