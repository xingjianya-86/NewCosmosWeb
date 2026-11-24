<script setup lang="ts">
import type { ChipTone } from './display'

withDefaults(
  defineProps<{
    text: string
    tone?: ChipTone
  }>(),
  {
    tone: 'ink',
  },
)
</script>

<template>
  <span class="elderly-chip" :class="`elderly-chip--${tone}`">{{ text || '—' }}</span>
</template>

<style scoped>
.elderly-chip {
  --chip-tone: var(--nc-ink-2);
  --chip-soft: var(--nc-paper-3);

  display: inline-flex;
  align-items: center;
  padding: 1px 8px;
  border: 1px solid var(--nc-line);
  border-left: 2px solid var(--chip-tone);
  border-radius: var(--nc-radius-xs);
  background-color: var(--chip-soft);
  color: var(--chip-tone);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.elderly-chip--seal {
  --chip-tone: var(--nc-seal);
  --chip-soft: var(--nc-seal-soft);
}

.elderly-chip--pine {
  --chip-tone: var(--nc-pine);
  --chip-soft: var(--nc-pine-soft);
}

.elderly-chip--amber {
  --chip-tone: var(--nc-amber);
  --chip-soft: var(--nc-amber-soft);
}
</style>
