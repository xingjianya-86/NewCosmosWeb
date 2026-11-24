<script setup lang="ts">
import { computed } from 'vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { resolveApplicationStatus } from './status'

const props = withDefaults(
  defineProps<{
    status: string
    statusText?: string
  }>(),
  {
    statusText: '',
  },
)

const meta = computed(() => resolveApplicationStatus(props.status, props.statusText))
</script>

<template>
  <span class="app-status" :class="`tone-${meta.tone}`">
    <NcStatusDot :status="meta.dot" :label="meta.label" />
  </span>
</template>

<style scoped>
.app-status {
  --status-bg: var(--nc-paper-3);

  display: inline-flex;
  align-items: center;
  max-width: 100%;
  padding: 2px 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--status-bg);
  line-height: 1.5;
  vertical-align: middle;
}

.app-status :deep(.nc-status) {
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.app-status.tone-seal {
  --status-bg: var(--nc-seal-soft);
}

.app-status.tone-pine {
  --status-bg: var(--nc-pine-soft);
}

.app-status.tone-amber {
  --status-bg: var(--nc-amber-soft);
}

.app-status.tone-violet {
  --status-bg: var(--nc-violet-soft);
}
</style>
