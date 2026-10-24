<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
const props = defineProps<{ bitmap: string; label: string }>();
const canvas = ref<HTMLCanvasElement | null>(null);
function draw() {
  const context = canvas.value?.getContext('2d');
  if (!context) return;
  const bytes = atob(props.bitmap);
  if (bytes.length !== 32 * 32 * 4) {
    context.clearRect(0, 0, 32, 32);
    return;
  }
  const pixels = new Uint8ClampedArray(bytes.length);
  for (let i = 0; i < bytes.length; i += 4) {
    pixels[i] = bytes.charCodeAt(i + 2);
    pixels[i + 1] = bytes.charCodeAt(i + 1);
    pixels[i + 2] = bytes.charCodeAt(i);
    pixels[i + 3] = bytes.charCodeAt(i + 3);
  }
  context.putImageData(new ImageData(pixels, 32, 32), 0, 0);
}
onMounted(draw);
watch(() => props.bitmap, draw);
</script>
<template><canvas ref="canvas" width="32" height="32" role="img" :aria-label="label"></canvas></template>
<style scoped>
canvas {
  width: 100%;
  height: auto;
  display: block;
  image-rendering: pixelated;
  background: repeating-conic-gradient(#dce4dc 0% 25%, #fff 0% 50%) 0 / 12px 12px;
}
</style>
