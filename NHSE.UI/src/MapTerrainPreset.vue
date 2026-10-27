<script setup lang="ts">
import { computed } from 'vue';
const props = defineProps<{ index: number; water: boolean; corner: string }>();
const isCorner = computed(() => [0, 2, 6, 8].includes(props.index));
const rotation = computed(() => [0, 0, 90, 270, 0, 90, 270, 180, 180][props.index] ?? 0);
const shape = computed(() => props.index === 4 ? 'M0 0H40V40H0Z' : !isCorner.value ? 'M0 0H40V20H0Z' : props.corner === 'angle' ? 'M0 0H40L0 40Z' : props.corner === 'inside' ? 'M0 0H40V40H20Q20 20 0 20Z' : 'M0 0H40V0Q40 40 0 40Z');
</script>
<template>
  <svg viewBox="0 0 40 40" width="32" height="32" aria-hidden="true" focusable="false">
    <rect width="40" height="40" rx="3" :fill="water ? '#81b878' : '#c5ab85'" />
    <path :d="shape" :transform="`rotate(${rotation} 20 20)`" :fill="water ? '#68b8d2' : '#81b878'" stroke="#396846" stroke-width=".8" />
    <path d="M0 20H40M20 0V40" stroke="#ffffff50" stroke-width=".6" />
  </svg>
</template>
