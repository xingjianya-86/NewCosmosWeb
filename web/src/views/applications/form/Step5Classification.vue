<script setup lang="ts">
import { computed } from 'vue'
import { MagicStick } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import CaregiverList from './CaregiverList.vue'
import { formatDate, formatMoney } from '../format'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const emit = defineEmits<{ (e: 'classify'): void; (e: 'complete'): void }>()

const classification = computed(() => props.form.classification)
const basic = computed(() => props.form.basic)
const supporters = computed(() => props.form.supporters ?? [])

/** 分类代码 → 中文名称；只展示中文，不显示 RuralSubsistence 等代码。 */
const CLASSIFICATION_NAMES: Record<string, string> = {
  RuralSubsistence: '最低生活保障对象',
  UrbanSubsistence: '最低生活保障对象',
  RuralLowIncome: '最低生活保障边缘家庭',
  UrbanLowIncome: '最低生活保障边缘家庭',
  RuralLowIncomeSingle: '最低生活保障对象（单人）',
  UrbanLowIncomeSingle: '最低生活保障对象（单人）',
  RuralDestituteCentralized: '特困人员（集中供养）',
  UrbanDestituteCentralized: '特困人员（集中供养）',
  RuralDestituteScattered: '特困人员（分散供养）',
  UrbanDestituteScattered: '特困人员（分散供养）',
  RuralRigidExpenditure: '刚性支出困难家庭',
  UrbanRigidExpenditure: '刚性支出困难家庭',
  RuralIncomeExceeded: '收入超标',
  UrbanIncomeExceeded: '收入超标',
  IneligibleWithLabor: '不符合认定条件（有劳动力）',
  Ineligible: '不符合认定条件',
  IneligibleOther: '不符合认定条件（其他）',
  LowIncome: '最低生活保障边缘家庭',
  Destitute: '特困人员',
  RigidExpenditure: '刚性支出困难家庭',
  IncomeExceeded: '收入超标',
  AssetVerification: '资产核查',
}

function classificationName(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return ''
  const mapped = CLASSIFICATION_NAMES[text]
  if (mapped) return mapped
  return /[\u4e00-\u9fa5]/.test(text) ? text : ''
}

const hasResult = computed(() =>
  Boolean(
    classification.value.classificationResult.trim() ||
      classification.value.classificationText.trim(),
  ),
)

const classificationLabel = computed(
  () =>
    classificationName(classification.value.classificationText) ||
    classificationName(classification.value.classificationResult) ||
    '尚未分类认定',
)

const eligibilityLabel = computed(() => {
  if (!hasResult.value) return '—'
  return classification.value.isEligible ? '符合' : '不符合'
})

const graceStatus = computed(() => {
  if (!hasResult.value) return '—'
  return classification.value.isInGracePeriod ? '是' : '否'
})

const graceMonths = computed(() => {
  const months = classification.value.gracePeriodMonths
  return months == null ? '—' : `${months} 个月`
})

const graceEndDate = computed(() => formatDate(classification.value.gracePeriodEndDate))

function amountText(value: number | null | undefined): string {
  return hasResult.value ? formatMoney(value) : '—'
}
</script>

<template>
  <div class="step-classification">
    <NcCard title="分类认定">
      <dl class="detail-grid">
        <div class="detail-item">
          <dt>是否符合</dt>
          <dd
            class="detail-value"
            :class="{
              'is-eligible': hasResult && classification.isEligible,
              'is-ineligible': hasResult && !classification.isEligible,
            }"
          >
            {{ eligibilityLabel }}
          </dd>
        </div>

        <div class="detail-item">
          <dt>分类</dt>
          <dd class="detail-value detail-value--name">{{ classificationLabel }}</dd>
        </div>

        <div class="detail-item detail-item--wide">
          <dt>判定依据</dt>
          <dd :class="{ 'is-empty': !classification.determinationBasis }">
            {{ classification.determinationBasis || '—' }}
          </dd>
        </div>

        <div
          v-if="hasResult && !classification.isEligible"
          class="detail-item detail-item--wide"
        >
          <dt>不符合原因</dt>
          <dd class="is-ineligible">
            {{ classification.ineligibleReason || '—' }}
          </dd>
        </div>

        <div class="detail-item detail-item--wide">
          <dt>渐退期</dt>
          <dd class="grace-inline">
            <span>是否：<strong>{{ graceStatus }}</strong></span>
            <span>月数：<strong>{{ graceMonths }}</strong></span>
            <span>结束日期：<strong>{{ graceEndDate }}</strong></span>
          </dd>
        </div>
      </dl>

      <template #footer>
        <div class="result-actions">
          <el-button :icon="MagicStick" :loading="busy" @click="emit('classify')">
            {{ hasResult ? '重新认定' : '分类认定' }}
          </el-button>
        </div>
      </template>
    </NcCard>

    <NcCard title="保障金额">
      <div class="amount-grid">
        <NcStat
          label="月保障金"
          :value="amountText(classification.householdMonthlyGuaranteeAmount)"
          hint="元 / 月"
          tone="ink"
          compact
        />
        <NcStat
          label="分类施保"
          :value="amountText(classification.classifiedSubsidyAmount)"
          hint="元 / 月"
          tone="amber"
          compact
        />
        <NcStat
          label="护理补贴"
          :value="amountText(classification.caregiverSubsidyAmount)"
          hint="元 / 月"
          tone="pine"
          compact
        />
        <NcStat
          label="合计"
          :value="amountText(classification.totalGuaranteeAmount)"
          hint="元 / 月"
          tone="seal"
          compact
        />
      </div>
    </NcCard>

    <NcCard title="赡养 / 抚养 / 扶养人">
      <el-empty
        v-if="supporters.length === 0"
        class="supporter-empty"
        description="暂无赡养 / 抚养 / 扶养人"
        :image-size="64"
      />

      <ul v-else class="supporter-list">
        <li
          v-for="(supporter, index) in supporters"
          :key="supporter.id || index"
          class="supporter-item"
        >
          <div class="supporter-head">
            <span class="supporter-index">赡养人 {{ index + 1 }}</span>
            <strong class="supporter-name">{{ supporter.name || '未填写姓名' }}</strong>
            <span class="supporter-relation">{{ supporter.relation || '—' }}</span>
          </div>

          <dl class="supporter-meta">
            <div>
              <dt>年赡养费</dt>
              <dd>{{ formatMoney(supporter.annualSupportFee) }} 元</dd>
            </div>
            <div>
              <dt>月赡养费</dt>
              <dd>{{ formatMoney(supporter.monthlySupportFee) }} 元</dd>
            </div>
            <div>
              <dt>赡养能力</dt>
              <dd>{{ supporter.isSupportAbility ? '有' : '无' }}</dd>
            </div>
          </dl>
        </li>
      </ul>
    </NcCard>

    <CaregiverList :form="form" :busy="busy" />

    <NcCard title="银行发放信息">
      <dl class="bank-grid">
        <div class="detail-item">
          <dt>开户行</dt>
          <dd :class="{ 'is-empty': !basic.bankName }">{{ basic.bankName || '—' }}</dd>
        </div>
        <div class="detail-item">
          <dt>银行卡号</dt>
          <dd class="mono" :class="{ 'is-empty': !basic.bankAccount }">
            {{ basic.bankAccount || '—' }}
          </dd>
        </div>
      </dl>
    </NcCard>
  </div>
</template>

<style scoped>
.step-classification {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.detail-grid,
.bank-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 24px;
  margin: 0;
}

.detail-item {
  min-width: 0;
}

.detail-item--wide {
  grid-column: 1 / -1;
}

.detail-item dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.02em;
}

.detail-item dd {
  margin: 4px 0 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
  word-break: break-word;
}

.detail-item dd.is-empty {
  color: var(--nc-disabled);
}

.detail-item dd.detail-value {
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.detail-item dd.detail-value--name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.detail-item dd.detail-value.is-eligible {
  color: var(--nc-pine-dark);
}

.detail-item dd.detail-value.is-ineligible {
  color: var(--nc-seal-dark);
}

.detail-item dd.is-ineligible {
  color: var(--nc-seal-dark);
}

.grace-inline {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 22px;
}

.grace-inline strong {
  color: var(--nc-ink);
  font-weight: 600;
}

.amount-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.supporter-empty {
  padding: 4px 0;
}

.supporter-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.supporter-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr);
  align-items: center;
  gap: 10px 18px;
  padding: 11px 13px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-amber);
  background-color: var(--nc-paper-soft);
}

.supporter-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px 10px;
  min-width: 0;
}

.supporter-index {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.supporter-name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.supporter-relation {
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.supporter-meta {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 6px 14px;
  margin: 0;
}

.supporter-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.supporter-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  font-weight: 600;
  word-break: break-word;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.result-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

@media (max-width: 1099px) {
  .supporter-item {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 767px) {
  .detail-grid,
  .bank-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .detail-item--wide {
    grid-column: auto;
  }

  .amount-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .supporter-meta {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .result-actions {
    align-items: stretch;
  }

  .result-actions .el-button {
    flex: 1 1 auto;
  }
}
</style>
