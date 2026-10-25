<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue';
import { t } from './i18n';
import type { ConsoleState, Player } from './bridge';
const props = defineProps<{ data: ConsoleState | null; players: Player[]; busy: boolean; execute: (name: string, payload?: Record<string, unknown>) => Promise<boolean> }>();
const form = ref<HTMLFormElement | null>(null), dialog = ref<HTMLDialogElement | null>(null);
const transport = ref<'network' | 'usb'>('network'), host = ref(''), port = ref(6000), offset = ref(''), length = ref(320), raw = ref('');
const player = ref(0), validate = ref(true), spoof = ref(false), autoMode = ref('none'), interval = ref(2000), auto = ref(false);
const confirmation = shallowRef<{ command: string; payload: Record<string, unknown>; repeat: boolean } | null>(null);
const hex = computed(() => raw.value.replace(/\s/g, '').toUpperCase());
const validHex = computed(() => hex.value.length > 0 && hex.value.length <= 32768 && hex.value.length % 2 === 0 && /^[0-9A-F]+$/.test(hex.value));
let timer: ReturnType<typeof setInterval> | undefined, running = false;
watch(() => props.data?.data, value => { raw.value = value ?? ''; });
watch(() => props.data?.connected, connected => { if (!connected) stop(); });
watch(() => props.data?.host, value => { if (!host.value && value) host.value = value; }, { immediate: true });
watch(() => props.data?.offset, value => { if (!offset.value && value) offset.value = value; }, { immediate: true });
watch(() => props.data?.port, value => { if (value) port.value = value; }, { immediate: true });
watch([transport, host, port, offset, player, validate, spoof, autoMode, interval], stop);
onBeforeUnmount(stop);
function stop() { if (timer) clearInterval(timer); timer = undefined; auto.value = false; }
function payload() { return { transport: transport.value, host: host.value, port: port.value, offset: offset.value, length: length.value, data: hex.value, player: player.value, validate: validate.value, spoof: spoof.value }; }
async function connect() { stop(); if (form.value?.reportValidity()) await props.execute('consoleConnect', payload()); }
async function disconnect() { stop(); await props.execute('consoleDisconnect'); }
async function action(command: string, repeat = false) {
  if (!form.value?.reportValidity() || !props.data?.connected || (command === 'consoleWrite' && !validHex.value)) return;
  const values = payload();
  if (command.endsWith('Write')) { confirmation.value = { command, payload: values, repeat }; await nextTick(); dialog.value?.showModal(); }
  else if (repeat) start(command, values); else await props.execute(command, values);
}
function start(command: string, values: Record<string, unknown>) {
  stop(); auto.value = true;
  timer = setInterval(async () => {
    if (props.busy || running || !auto.value) return;
    running = true;
    try { if (!(await props.execute(command, { ...values }))) stop(); }
    finally { running = false; }
  }, interval.value);
}
function cancel() { dialog.value?.close(); confirmation.value = null; }
async function confirm() { const value = confirmation.value; cancel(); if (!value) return; if (await props.execute(value.command, value.payload) && value.repeat) start(value.command, value.payload); }
function startAuto() { if (autoMode.value !== 'none') void action(autoMode.value, true); }
</script>
<template>
  <section class="console-page">
    <p class="page-description">{{ t('consoleHint') }}</p><p class="field-help">{{ t('consoleUntested') }}</p>
    <form ref="form" @submit.prevent="connect">
      <div class="console-row">
        <label>{{ t('consoleTransport') }}<select v-model="transport" :disabled="busy || data?.connected"><option value="network">{{ t('consoleNetwork') }}</option><option value="usb">USB</option></select></label>
        <template v-if="transport === 'network'"><label>{{ t('consoleHost') }}<input v-model="host" type="text" required maxlength="45" :disabled="busy || data?.connected" /></label><label>{{ t('consolePort') }}<input v-model.number="port" type="number" min="1" max="65535" required :disabled="busy || data?.connected" /></label></template>
        <button class="button" :disabled="busy || data?.connected">{{ t('consoleConnect') }}</button><button class="button" type="button" :disabled="busy || !data?.connected" @click="disconnect">{{ t('consoleDisconnect') }}</button>
        <span role="status">{{ t(data?.connected ? 'consoleConnected' : 'consoleDisconnected') }}</span>
      </div>
      <div class="console-row">
        <label>{{ t('consoleOffset') }}<input v-model="offset" type="text" pattern="[0-9a-fA-F]{1,8}" required maxlength="8" :disabled="busy || auto" /></label>
        <label>{{ t('consoleLength') }}<input v-model.number="length" type="number" min="1" max="16384" step="1" required :disabled="busy || auto" /></label>
        <button type="button" class="button" :disabled="busy || !data?.connected || auto" @click="action('consoleRead')">{{ t('consoleRead') }}</button>
        <button type="button" class="button" :disabled="busy || !data?.connected || !validHex || auto" @click="action('consoleWrite')">{{ t('consoleWrite') }}</button>
      </div>
      <label>{{ t('consoleBytes') }}<textarea v-model="raw" rows="9" maxlength="49152" :disabled="busy || auto" spellcheck="false" /></label>
      <div class="console-row">
        <label>{{ t('player') }}<select v-model="player" :disabled="busy || auto"><option v-for="p in players" :key="p.index" :value="p.index">{{ p.name }}</option></select></label>
        <label class="checkbox"><input v-model="validate" type="checkbox" :disabled="busy || auto" />{{ t('consoleValidate') }}</label>
        <label class="checkbox"><input v-model="spoof" type="checkbox" :disabled="busy || auto" />{{ t('consoleSpoof') }}</label>
        <button type="button" class="button" :disabled="busy || !data?.connected || !players.length || auto" @click="action('consolePocketRead')">{{ t('consolePocketRead') }}</button>
        <button type="button" class="button" :disabled="busy || !data?.connected || !players.length || auto" @click="action('consolePocketWrite')">{{ t('consolePocketWrite') }}</button>
      </div>
      <div class="console-row">
        <label>{{ t('consoleAuto') }}<select v-model="autoMode" :disabled="busy || auto"><option value="none">{{ t('consoleAutoOff') }}</option><option v-for="command in ['consoleRead', 'consoleWrite', 'consolePocketRead', 'consolePocketWrite']" :key="command" :value="command">{{ t(command as 'consoleRead' | 'consoleWrite' | 'consolePocketRead' | 'consolePocketWrite') }}</option></select></label>
        <label>{{ t('consoleInterval') }}<input v-model.number="interval" type="number" min="1000" max="60000" step="1" required :disabled="busy || auto" /></label>
        <button type="button" class="button" :disabled="busy || !data?.connected || auto || autoMode === 'none'" @click="startAuto">{{ t('consoleAutoStart') }}</button><button type="button" class="button" :disabled="!auto" @click="stop">{{ t('consoleAutoStop') }}</button>
      </div>
      <p>{{ t('consoleAutoHint') }}</p>
    </form>
    <dialog v-if="confirmation" ref="dialog" class="modal" :aria-label="t('consoleWrite')" @cancel.prevent="cancel"><h2>{{ t('consoleWrite') }}</h2><p>{{ t('consoleWriteConfirm') }}</p><p>{{ confirmation.command }} · {{ confirmation.payload.offset }} · {{ confirmation.payload.player }}<template v-if="confirmation.repeat"> · {{ t('consoleAuto') }} · {{ interval }} ms</template></p><div class="console-row"><button class="button" @click="cancel">{{ t('cancel') }}</button><button class="button primary" @click="confirm">{{ t('apply') }}</button></div></dialog>
  </section>
</template>
<style scoped>
.console-row { display: flex; flex-wrap: wrap; gap: .75rem; align-items: end; margin-block: 1rem; }
label { display: grid; gap: .4rem; min-width: 0; }
.checkbox { display: flex; align-items: center; }
input, select, textarea { box-sizing: border-box; max-width: 100%; min-width: 0; padding: .65rem; font: inherit; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: .5rem; }
textarea { width: 100%; overflow-wrap: anywhere; }
input[type=checkbox] { width: auto; }
</style>
