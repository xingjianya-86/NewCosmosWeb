<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    value: string | number
    hint?: string
    tone?: 'ink' | 'seal' | 'pine' | 'amber'
    compact?: boolean
  }>(),
  {
    hint: '',
    tone: 'ink',
    compact: false,
  },
)
</script>

<template>
  <article class="nc-stat" :class="[`nc-stat--${tone}`, { 'is-compact': compact }]">
    <span class="nc-stat__label">{{ label }}</span>
    <strong class="nc-stat__value" :title="String(value)">{{ value }}</strong>
    <span v-if="hint" class="nc-stat__hint">{{ hint }}</span>
  </article>
</template>

<style scoped>
.nc-stat {
  --stat-tone: var(--nc-ink-2);
  --stat-tone-soft: var(--nc-paper-3);

  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
  padding: 13px 16px 14px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--stat-tone);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.nc-stat--seal {
  --stat-tone: var(--nc-seal);
  --stat-tone-soft: var(--nc-seal-soft);
}

.nc-stat--pine {
  --stat-tone: var(--nc-pine);
  --stat-tone-soft: var(--nc-pine-soft);
}

.nc-stat--amber {
  --stat-tone: var(--nc-amber);
  --stat-tone-soft: var(--nc-amber-soft);
}

.nc-stat--ink {
  --stat-tone: var(--nc-ink-2);
  --stat-tone-soft: var(--nc-paper-3);
}

.nc-stat__label {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
  color: var(--nc-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nc-stat__value {
  overflow: hidden;
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.2;
  letter-spacing: 0.03em;
  color: var(--stat-tone);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.nc-stat__hint {
  overflow: hidden;
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.nc-stat.is-compact {
  justify-content: center;
}

.nc-stat.is-compact .nc-stat__value {
  font-family: var(--nc-font-sans);
  font-size: var(--nc-text-md);
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--nc-ink);
}
</style>
