<script setup lang="ts">
withDefaults(
  defineProps<{
    status: 'online' | 'offline' | 'default' | 'idle'
    label?: string
  }>(),
  {
    label: '',
  },
)
</script>

<template>
  <span class="nc-status" :class="`nc-status--${status}`">
    <i class="nc-status__dot" aria-hidden="true" />
    <span v-if="label" class="nc-status__label">{{ label }}</span>
  </span>
</template>

<style scoped>
.nc-status {
  --status-tone: var(--nc-muted);

  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--status-tone);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.4;
  white-space: nowrap;
}

.nc-status__dot {
  flex: none;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background-color: var(--status-tone);
  box-shadow: 0 0 0 3px rgba(139, 127, 108, 0.14);
}

.nc-status--online {
  --status-tone: var(--nc-pine);
}

.nc-status--online .nc-status__dot {
  background-color: var(--nc-pine-light);
  box-shadow: 0 0 0 3px rgba(74, 107, 86, 0.16);
}

.nc-status--offline {
  --status-tone: var(--nc-muted);
}

.nc-status--offline .nc-status__dot {
  background-color: transparent;
  border: 1px solid var(--nc-muted);
  box-shadow: 0 0 0 3px rgba(139, 127, 108, 0.1);
}

.nc-status--idle {
  --status-tone: var(--nc-amber);
}

.nc-status--idle .nc-status__dot {
  background-color: var(--nc-amber-light);
  box-shadow: 0 0 0 3px rgba(150, 104, 28, 0.14);
}

.nc-status--default {
  --status-tone: var(--nc-seal-dark);
}

.nc-status--default .nc-status__dot {
  width: 6px;
  height: 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
  box-shadow: none;
}

.nc-status__label {
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
