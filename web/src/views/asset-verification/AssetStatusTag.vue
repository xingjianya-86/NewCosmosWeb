<script setup lang="ts">
import { computed } from 'vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { resolveAssetStatus } from './status'

const props = withDefaults(
  defineProps<{
    status: string
    statusText?: string
  }>(),
  {
    statusText: '',
  },
)

const meta = computed(() => resolveAssetStatus(props.status, props.statusText))
</script>

<template>
  <span class="asset-status" :class="`tone-${meta.tone}`">
    <NcStatusDot :status="meta.dot" :label="meta.label" />
  </span>
</template>

<style scoped>
.asset-status {
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

.asset-status :deep(.nc-status) {
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.asset-status.tone-seal {
  --status-bg: var(--nc-seal-soft);
}

.asset-status.tone-pine {
  --status-bg: var(--nc-pine-soft);
}

.asset-status.tone-amber {
  --status-bg: var(--nc-amber-soft);
}

.asset-status.tone-ink {
  --status-bg: var(--nc-paper-3);
}
</style>
