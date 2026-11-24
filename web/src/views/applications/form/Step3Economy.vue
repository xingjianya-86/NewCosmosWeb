<script setup lang="ts">
import { computed } from 'vue'
import { InfoFilled } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import { formatMoney } from '../format'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

/** 经济状况中的可编辑数值字段（合计与人均由服务端回写，不在此列）。 */
type EconomyInputKey =
  | 'workIncome'
  | 'businessIncome'
  | 'propertyIncome'
  | 'transferIncome'
  | 'otherIncome'
  | 'alimonyIncome'
  | 'landIncome'
  | 'subsidyTotal'
  | 'rigidExpenditure'
  | 'familyLandArea'
  | 'selfFarmedLandArea'
  | 'subleasedLandArea'
  | 'contractedLandArea'

interface EconomyField {
  key: EconomyInputKey
  label: string
  hint?: string
  step?: number
}

const economy = computed(() => props.form.economy)

const incomeFields: EconomyField[] = [
  { key: 'workIncome', label: '工资性收入' },
  { key: 'businessIncome', label: '经营净收入' },
  { key: 'propertyIncome', label: '财产性收入' },
  { key: 'transferIncome', label: '转移性收入' },
  { key: 'otherIncome', label: '其他收入' },
]

const annualFields: EconomyField[] = [
  { key: 'alimonyIncome', label: '赡养费（年值）' },
  { key: 'landIncome', label: '土地收入（年值）' },
  { key: 'subsidyTotal', label: '农业补贴（年值）', hint: '按折算后年值填报' },
]

const landFields: EconomyField[] = [
  { key: 'familyLandArea', label: '家庭承包土地面积', step: 0.1 },
  { key: 'selfFarmedLandArea', label: '自种面积', step: 0.1 },
  { key: 'subleasedLandArea', label: '转包面积', step: 0.1 },
  { key: 'contractedLandArea', label: '托管面积', step: 0.1 },
]

function setEconomy(key: EconomyInputKey, value: number | null | undefined): void {
  props.form.economy[key] = value ?? null
}
</script>

<template>
  <div class="step-economy">
    <NcCard title="收入分项" subtitle="按家庭月均口径填报，允许填 0。">
      <template #actions>
        <span class="section-meta">元 / 月</span>
      </template>

      <div class="field-grid">
        <div v-for="field in incomeFields" :key="field.key" class="field">
          <label class="field-label" :for="`economy-${field.key}`">{{ field.label }}</label>
          <el-input-number
            :id="`economy-${field.key}`"
            class="nc-number"
            :model-value="economy[field.key]"
            :min="0"
            :step="100"
            :precision="2"
            :value-on-clear="0"
            :disabled="busy"
            controls-position="right"
            :aria-label="field.label"
            @update:model-value="setEconomy(field.key, $event)"
          />
          <p v-if="field.hint" class="field-hint">{{ field.hint }}</p>
        </div>
      </div>
    </NcCard>

    <NcCard title="年度收入" subtitle="赡养费、土地收入与农业补贴按年值填报。">
      <template #actions>
        <span class="section-meta">元 / 年</span>
      </template>

      <div class="field-grid">
        <div v-for="field in annualFields" :key="field.key" class="field">
          <label class="field-label" :for="`economy-${field.key}`">{{ field.label }}</label>
          <el-input-number
            :id="`economy-${field.key}`"
            class="nc-number"
            :model-value="economy[field.key]"
            :min="0"
            :step="100"
            :precision="2"
            :value-on-clear="0"
            :disabled="busy"
            controls-position="right"
            :aria-label="field.label"
            @update:model-value="setEconomy(field.key, $event)"
          />
          <p v-if="field.hint" class="field-hint">{{ field.hint }}</p>
        </div>
      </div>
    </NcCard>

    <NcCard title="刚性支出" subtitle="医疗、教育、残疾康复、居住等分项合并填报。">
      <template #actions>
        <span class="section-meta">元 / 月</span>
      </template>

      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="economy-rigidExpenditure">刚性支出合计</label>
          <el-input-number
            id="economy-rigidExpenditure"
            class="nc-number"
            :model-value="economy.rigidExpenditure"
            :min="0"
            :step="100"
            :precision="2"
            :value-on-clear="0"
            :disabled="busy"
            controls-position="right"
            aria-label="刚性支出合计"
            @update:model-value="setEconomy('rigidExpenditure', $event)"
          />
          <p class="field-hint">按家庭月均口径合并填报。</p>
        </div>

        <aside class="aside-note">
          含医疗、教育、残疾康复、居住等刚性支出；分项不再单列，统一计入合计。
        </aside>
      </div>
    </NcCard>

    <NcCard title="土地情况" subtitle="按家庭实际土地经营情况填报。">
      <template #actions>
        <span class="section-meta">亩</span>
      </template>

      <div class="field-grid">
        <div v-for="field in landFields" :key="field.key" class="field">
          <label class="field-label" :for="`economy-${field.key}`">{{ field.label }}</label>
          <el-input-number
            :id="`economy-${field.key}`"
            class="nc-number"
            :model-value="economy[field.key]"
            :min="0"
            :step="field.step ?? 0.1"
            :precision="2"
            :value-on-clear="0"
            :disabled="busy"
            controls-position="right"
            :aria-label="field.label"
            @update:model-value="setEconomy(field.key, $event)"
          />
        </div>
      </div>
    </NcCard>

    <NcCard title="收入核算" subtitle="合计与人均为只读值，保存后由服务端按 Core 口径重新计算。">
      <template #actions>
        <span class="section-meta">只读 · 服务端回写</span>
      </template>

      <div class="summary-grid">
        <NcStat
          label="年收入合计"
          :value="formatMoney(economy.totalAnnualIncome)"
          hint="元 / 年"
          tone="ink"
          compact
        />
        <NcStat
          label="月均收入"
          :value="formatMoney(economy.totalFamilyIncome)"
          hint="元 / 月"
          tone="ink"
          compact
        />
        <NcStat
          label="年人均收入"
          :value="formatMoney(economy.perCapitaAnnualIncome)"
          hint="元 / 年"
          tone="amber"
          compact
        />
        <NcStat
          label="月人均收入"
          :value="formatMoney(economy.perCapitaIncome)"
          hint="元 / 月"
          tone="seal"
          compact
        />
      </div>

      <p class="recalc-note">
        <el-icon><InfoFilled /></el-icon>
        <span>保存后由服务端按 Core 口径重算，前端不重写收入公式。</span>
      </p>
    </NcCard>
  </div>
</template>

<style scoped>
.step-economy {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.section-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
  white-space: nowrap;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 18px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
  line-height: 1.45;
}

.field-hint {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.aside-note {
  align-self: center;
  min-width: 0;
  padding: 10px 12px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.nc-number {
  width: 100%;
}

.nc-number :deep(.el-input__wrapper) {
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
}

.nc-number :deep(.el-input__wrapper.is-focus) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
}

.nc-number :deep(.el-input__inner) {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: 13px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.recalc-note {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.recalc-note .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

@media (max-width: 1099px) {
  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }

  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .section-meta {
    font-size: 10px;
    letter-spacing: 0.06em;
  }
}

@media (max-width: 479px) {
  .summary-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
