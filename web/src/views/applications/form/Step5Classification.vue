<script setup lang="ts">
import { computed } from 'vue'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import CaregiverList from './CaregiverList.vue'
import { formatDate } from '../format'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const emit = defineEmits<{ (e: 'classify'): void }>()

const classification = computed(() => props.form.classification)

/**
 * 服务端 classification 契约目前只覆盖判定结果与金额；能力鉴定、判定明细、
 * 分类施保明细、渐退开始日期/原保障金额尚无字段。这里按可选字段只读读取，
 * 有值才展示，没有值统一显示“—”，不伪造数据。
 */
interface ClassificationExtras {
  originalGuaranteeAmount?: number | null
  gracePeriodStartDate?: string | null
  determinationDetails?: string[] | null
  classifiedSubsidyTypes?: string | null
  classifiedSubsidyCount?: number | null
  classifiedSubsidyPerPerson?: number | null
  capabilityEating?: boolean | null
  capabilityDressing?: boolean | null
  capabilityGettingInOutOfBed?: boolean | null
  capabilityUsingToilet?: boolean | null
  capabilityIndoorWalking?: boolean | null
  capabilityBathing?: boolean | null
  capabilityCompletedItems?: number | null
  capabilitySelfCareLevel?: string | null
}

const extras = computed(
  () => props.form.classification as unknown as ClassificationExtras,
)

/** 分类代码 → 中文名称；服务端 classificationText 优先，未命中保留原值。 */
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
  return CLASSIFICATION_NAMES[text] ?? text
}

const hasResult = computed(
  () =>
    Boolean(
      (classification.value.classificationResult ?? '').trim() ||
        (classification.value.classificationText ?? '').trim(),
    ) || classification.value.isEligible,
)

const classificationLabel = computed(() => {
  const label =
    classificationName(classification.value.classificationText) ||
    classificationName(classification.value.classificationResult)
  return label || '未识别分类'
})

const eligibilityLabel = computed(() =>
  classification.value.isEligible ? '符合' : '不符合',
)

/** 特困判定：分类代码或中文名包含“特困 / Destitute”。 */
const isDestitute = computed(() =>
  /特困|destitute/i.test(
    `${classification.value.classificationResult ?? ''} ${
      classification.value.classificationText ?? ''
    }`,
  ),
)

const amountFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
})

function amountText(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `${amountFormatter.format(value)} 元`
}

const graceMonthsText = computed(() => {
  const months = classification.value.gracePeriodMonths
  return months === null || months === undefined ? '—' : `${months} 个月`
})

const graceOriginalAmountText = computed(() =>
  amountText(extras.value.originalGuaranteeAmount),
)

const graceStartDateText = computed(() =>
  extras.value.gracePeriodStartDate
    ? formatDate(extras.value.gracePeriodStartDate)
    : '—',
)

const graceEndDateText = computed(() =>
  formatDate(classification.value.gracePeriodEndDate),
)

/** 旧版口径：剩余 N 天（至 yyyy-MM-dd）/ 已结束（yyyy-MM-dd）/ 渐退期中。 */
function dateOnly(value: string): Date | null {
  const matched = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (matched) {
    return new Date(Number(matched[1]), Number(matched[2]) - 1, Number(matched[3]))
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

const graceStatusText = computed(() => {
  if (!classification.value.isInGracePeriod) return '—'
  const endDate = dateOnly(classification.value.gracePeriodEndDate)
  if (!endDate) return '渐退期中'
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const daysLeft = Math.round((endDate.getTime() - today.getTime()) / 86_400_000)
  return daysLeft >= 0
    ? `剩余 ${daysLeft} 天（至 ${formatDate(classification.value.gracePeriodEndDate)}）`
    : `已结束（${formatDate(classification.value.gracePeriodEndDate)}）`
})

const classifiedSubsidy = computed(() => {
  const count = extras.value.classifiedSubsidyCount
  return {
    types: extras.value.classifiedSubsidyTypes?.trim() || '—',
    count: count === null || count === undefined ? '—' : `${count} 人`,
    perPerson: amountText(extras.value.classifiedSubsidyPerPerson),
  }
})

const determinationDetails = computed<string[]>(() => {
  const value = extras.value.determinationDetails
  if (!Array.isArray(value)) return []
  return value.filter(
    (item): item is string => typeof item === 'string' && item.trim() !== '',
  )
})

function capabilityText(value: boolean | null | undefined): string {
  if (value === true) return '✓ 能'
  if (value === false) return '✗ 不能'
  return '—'
}

const capabilityItems = computed(() => [
  { label: '进食', value: capabilityText(extras.value.capabilityEating) },
  { label: '穿衣', value: capabilityText(extras.value.capabilityDressing) },
  {
    label: '上下床',
    value: capabilityText(extras.value.capabilityGettingInOutOfBed),
  },
  { label: '如厕', value: capabilityText(extras.value.capabilityUsingToilet) },
  {
    label: '室内行走',
    value: capabilityText(extras.value.capabilityIndoorWalking),
  },
  { label: '洗澡', value: capabilityText(extras.value.capabilityBathing) },
])

const capabilityCompletedText = computed(() => {
  const count = extras.value.capabilityCompletedItems
  return count === null || count === undefined ? '—' : `${count}/6 项`
})

const capabilityLevelText = computed(
  () => extras.value.capabilitySelfCareLevel?.trim() || '—',
)
</script>

<template>
  <div class="step-classification">
    <!-- 卡1：分类判定 -->
    <NcCard title="🔍 分类判定">
      <template #actions>
        <el-button
          v-if="hasResult"
          :loading="busy"
          :disabled="busy"
          @click="emit('classify')"
        >
          重新判定
        </el-button>
        <el-button
          v-else
          type="primary"
          :loading="busy"
          :disabled="busy"
          @click="emit('classify')"
        >
          执行判定
        </el-button>
      </template>
    </NcCard>

    <!-- 卡2：特困供养信息（仅特困分类显示） -->
    <NcCard v-if="isDestitute" title="🏠 特困供养信息">
      <dl class="detail-grid">
        <div class="detail-item">
          <dt>供养方式</dt>
          <dd class="is-empty">—</dd>
        </div>
        <div class="detail-item">
          <dt>供养机构</dt>
          <dd class="is-empty">—</dd>
        </div>
        <div class="detail-item detail-item--wide">
          <dt>机构详情</dt>
          <dd class="is-empty">—</dd>
        </div>
      </dl>

      <div class="section-divider" />

      <CaregiverList :form="form" :busy="busy" />
    </NcCard>

    <!-- 卡3：判定结果（仅已判定显示） -->
    <NcCard v-if="hasResult" title="📊 判定结果">
      <dl class="detail-grid">
        <div class="detail-item">
          <dt>分类结果</dt>
          <dd class="detail-value detail-value--name">{{ classificationLabel }}</dd>
        </div>
        <div class="detail-item">
          <dt>是否符合</dt>
          <dd
            class="detail-value"
            :class="classification.isEligible ? 'is-eligible' : 'is-ineligible'"
          >
            {{ eligibilityLabel }}
          </dd>
        </div>
        <div
          v-if="!classification.isEligible"
          class="detail-item detail-item--wide"
        >
          <dt>不符合原因</dt>
          <dd class="is-ineligible">
            {{ classification.ineligibleReason || '—' }}
          </dd>
        </div>
      </dl>

      <!-- 保障金额子卡 -->
      <section class="amount-card">
        <h3 class="sub-title">💰 保障金额</h3>
        <div class="amount-grid">
          <NcStat
            label="月保障金额"
            :value="amountText(classification.householdMonthlyGuaranteeAmount)"
            tone="ink"
            compact
          />
          <NcStat
            label="照料护理费"
            :value="amountText(classification.caregiverSubsidyAmount)"
            tone="amber"
            compact
          />
          <NcStat
            label="分类施保"
            :value="amountText(classification.classifiedSubsidyAmount)"
            tone="pine"
            compact
          />
          <NcStat
            label="合计保障"
            :value="amountText(classification.totalGuaranteeAmount)"
            tone="seal"
            compact
          />
        </div>
      </section>

      <!-- 分类施保详情：服务端未提供时全部显示“—” -->
      <section class="subsidy-detail">
        <h3 class="sub-title">分类施保详情</h3>
        <dl class="detail-grid">
          <div class="detail-item">
            <dt>符合条件类型</dt>
            <dd :class="{ 'is-empty': classifiedSubsidy.types === '—' }">
              {{ classifiedSubsidy.types }}
            </dd>
          </div>
          <div class="detail-item">
            <dt>符合条件人数</dt>
            <dd :class="{ 'is-empty': classifiedSubsidy.count === '—' }">
              {{ classifiedSubsidy.count }}
            </dd>
          </div>
          <div class="detail-item detail-item--wide">
            <dt>每人标准</dt>
            <dd :class="{ 'is-empty': classifiedSubsidy.perPerson === '—' }">
              {{ classifiedSubsidy.perPerson }}
            </dd>
          </div>
        </dl>
      </section>
    </NcCard>

    <!-- 卡4：渐退期（仅渐退期内显示） -->
    <NcCard
      v-if="classification.isInGracePeriod"
      title="🔄 渐退期"
    >
      <dl class="detail-grid">
        <div class="detail-item">
          <dt>渐退期月数</dt>
          <dd class="detail-value">{{ graceMonthsText }}</dd>
        </div>
        <div class="detail-item">
          <dt>原保障金额</dt>
          <dd
            class="detail-value"
            :class="{ 'is-empty': graceOriginalAmountText === '—' }"
          >
            {{ graceOriginalAmountText }}
          </dd>
        </div>
        <div class="detail-item">
          <dt>渐退开始</dt>
          <dd :class="{ 'is-empty': graceStartDateText === '—' }">
            {{ graceStartDateText }}
          </dd>
        </div>
        <div class="detail-item">
          <dt>渐退结束</dt>
          <dd :class="{ 'is-empty': graceEndDateText === '—' }">
            {{ graceEndDateText }}
          </dd>
        </div>
        <div class="detail-item detail-item--wide">
          <dt>状态</dt>
          <dd class="grace-status">{{ graceStatusText }}</dd>
        </div>
      </dl>
    </NcCard>

    <!-- 卡5：判定依据（仅已判定显示） -->
    <NcCard v-if="hasResult" title="📋 判定依据">
      <dl class="detail-grid">
        <div class="detail-item detail-item--wide">
          <dt>判定依据</dt>
          <dd
            class="basis-text"
            :class="{ 'is-empty': !classification.determinationBasis }"
          >
            {{ classification.determinationBasis || '—' }}
          </dd>
        </div>
      </dl>

      <div class="section-divider" />

      <h3 class="sub-title">判定明细</h3>
      <ul v-if="determinationDetails.length > 0" class="detail-list">
        <li
          v-for="(detail, index) in determinationDetails"
          :key="`${index}-${detail}`"
        >
          {{ detail }}
        </li>
      </ul>
      <p v-else class="empty-detail">暂无判定明细</p>
    </NcCard>

    <!-- 卡6：能力鉴定（仅特困显示） -->
    <NcCard v-if="isDestitute" title="📋 能力鉴定">
      <div class="capability-grid">
        <div
          v-for="item in capabilityItems"
          :key="item.label"
          class="capability-item"
        >
          <span>{{ item.label }}</span>
          <strong :class="{ 'is-empty': item.value === '—' }">
            {{ item.value }}
          </strong>
        </div>
      </div>

      <div class="capability-summary">
        <div class="capability-summary__item">
          <span>能完成项目数</span>
          <strong :class="{ 'is-empty': capabilityCompletedText === '—' }">
            {{ capabilityCompletedText }}
          </strong>
        </div>
        <div class="capability-summary__item">
          <span>自理能力等级</span>
          <strong :class="{ 'is-empty': capabilityLevelText === '—' }">
            {{ capabilityLevelText }}
          </strong>
        </div>
      </div>
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

.detail-grid {
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

.detail-item dd.detail-value.is-ineligible,
.detail-item dd.is-ineligible {
  color: var(--nc-seal-dark);
}

.section-divider {
  height: 1px;
  margin: 16px 0;
  background-color: var(--nc-line-faint);
}

.sub-title {
  margin: 0 0 10px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.amount-card {
  margin-top: 16px;
  padding: 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.amount-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.subsidy-detail {
  margin-top: 16px;
  padding: 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.basis-text {
  white-space: pre-wrap;
}

.grace-status {
  color: var(--nc-amber-dark, var(--nc-ink-2));
  font-weight: 600;
}

.detail-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.detail-list li {
  padding: 8px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
}

.empty-detail {
  margin: 0;
  color: var(--nc-disabled);
  font-size: 13px;
}

.capability-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.capability-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
  padding: 9px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink-2);
  font-size: 13px;
}

.capability-item strong {
  color: var(--nc-ink);
  font-weight: 700;
  white-space: nowrap;
}

.capability-summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.capability-summary__item {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
}

.capability-summary__item span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.capability-summary__item strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.is-empty {
  color: var(--nc-disabled);
}

@media (max-width: 1099px) {
  .amount-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .detail-item--wide {
    grid-column: auto;
  }

  .amount-grid,
  .capability-grid,
  .capability-summary {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
