<script setup lang="ts">
withDefaults(
  defineProps<{
    title: string
    description?: string
    kicker?: string
  }>(),
  {
    description: '',
    kicker: '',
  },
)
</script>

<template>
  <header class="nc-page-header">
    <div class="nc-page-header__main">
      <p v-if="kicker" class="nc-page-header__kicker">{{ kicker }}</p>
      <h1 class="nc-page-header__title">{{ title }}</h1>
      <p v-if="description" class="nc-page-header__desc">{{ description }}</p>
      <slot />
    </div>

    <div v-if="$slots.actions" class="nc-page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.nc-page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 20px;
}

.nc-page-header__main {
  min-width: 0;
}

.nc-page-header__kicker {
  margin-bottom: 6px;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: var(--nc-muted);
}

.nc-page-header__title {
  display: flex;
  align-items: center;
  gap: 12px;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-2xl);
  font-weight: 700;
  line-height: 1.35;
  letter-spacing: 0.04em;
  color: var(--nc-ink);
}

.nc-page-header__title::before {
  content: '';
  flex: none;
  width: 3px;
  height: 1.05em;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.nc-page-header__desc {
  margin-top: 7px;
  max-width: 760px;
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  color: var(--nc-muted-2);
}

.nc-page-header__actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

@media (max-width: 767px) {
  .nc-page-header {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .nc-page-header__title {
    font-size: var(--nc-text-xl);
  }

  .nc-page-header__actions {
    width: 100%;
  }

  .nc-page-header__actions :deep(.el-button) {
    flex: 1;
  }
}
</style>
