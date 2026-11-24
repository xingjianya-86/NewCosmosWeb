<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string
    subtitle?: string
    padding?: 'none' | 'sm' | 'md' | 'lg'
    tone?: 'paper' | 'plain'
  }>(),
  {
    title: '',
    subtitle: '',
    padding: 'md',
    tone: 'paper',
  },
)
</script>

<template>
  <section class="nc-card" :class="[`nc-card--${padding}`, `nc-card--${tone}`]">
    <header v-if="title || subtitle || $slots.actions" class="nc-card__head">
      <div class="nc-card__heading">
        <h2 v-if="title" class="nc-card__title">{{ title }}</h2>
        <p v-if="subtitle" class="nc-card__subtitle">{{ subtitle }}</p>
      </div>
      <div v-if="$slots.actions" class="nc-card__actions">
        <slot name="actions" />
      </div>
    </header>

    <div class="nc-card__body">
      <slot />
    </div>

    <footer v-if="$slots.footer" class="nc-card__foot">
      <slot name="footer" />
    </footer>
  </section>
</template>

<style scoped>
.nc-card {
  min-width: 0;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  box-shadow: var(--nc-shadow-xs);
}

.nc-card--paper {
  background-color: var(--nc-paper-2);
}

.nc-card--plain {
  background-color: transparent;
}

.nc-card--none {
  padding: 0;
}

.nc-card--sm {
  padding: 14px 16px;
}

.nc-card--md {
  padding: 20px 22px;
}

.nc-card--lg {
  padding: 26px 28px;
}

.nc-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.nc-card--none .nc-card__head {
  padding: 18px 20px 0;
}

.nc-card--sm .nc-card__head,
.nc-card--md .nc-card__head,
.nc-card--lg .nc-card__head {
  margin-bottom: 14px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.nc-card__heading {
  min-width: 0;
}

.nc-card__title {
  display: flex;
  align-items: center;
  gap: 10px;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
  color: var(--nc-ink);
}

.nc-card__title::before {
  content: '';
  flex: none;
  width: 3px;
  height: 15px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.nc-card__subtitle {
  margin-top: 5px;
  font-size: var(--nc-text-sm);
  line-height: 1.65;
  color: var(--nc-muted);
}

.nc-card__actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.nc-card__body {
  min-width: 0;
}

.nc-card--none .nc-card__body {
  min-height: 0;
}

.nc-card__foot {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

@media (max-width: 767px) {
  .nc-card--md {
    padding: 16px 15px;
  }

  .nc-card--lg {
    padding: 20px 16px;
  }

  .nc-card--sm {
    padding: 13px 14px;
  }
}
</style>
