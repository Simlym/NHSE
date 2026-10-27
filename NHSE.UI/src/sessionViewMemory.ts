import { nextTick, onBeforeUnmount, onMounted, type Ref } from 'vue';
let epoch = 0;
const views = new Map<string, { values: unknown[]; details: boolean[]; scroll: number }>();
export function clearSessionViews() { epoch++; views.clear(); }
/** Remember presentation only; save drafts are deliberately excluded. */
export function useSessionView(key: () => string, refs: Ref<unknown>[], root: () => HTMLElement | null) {
  const ownEpoch = epoch;
  let restoring = false;
  onMounted(async () => {
    const saved = views.get(key());
    if (!saved || ownEpoch !== epoch) return;
    restoring = true;
    saved.values.forEach((value, i) => { if (refs[i]) refs[i]!.value = structuredClone(value); });
    await nextTick();
    // Search watchers can reset pagination; restore presentation after they have run.
    saved.values.forEach((value, i) => { if (refs[i]) refs[i]!.value = structuredClone(value); });
    await nextTick();
    root()?.querySelectorAll('details').forEach((detail, i) => { if (saved.details[i] !== undefined) detail.open = saved.details[i]!; });
    const scroller = root()?.closest<HTMLElement>('.workspace-content');
    if (scroller) scroller.scrollTop = saved.scroll;
    restoring = false;
  });
  onBeforeUnmount(() => {
    if (ownEpoch !== epoch || restoring) return;
    views.set(key(), { values: refs.map(value => JSON.parse(JSON.stringify(value.value))), details: Array.from(root()?.querySelectorAll('details') ?? []).map(detail => detail.open), scroll: root()?.closest<HTMLElement>('.workspace-content')?.scrollTop ?? 0 });
  });
}
