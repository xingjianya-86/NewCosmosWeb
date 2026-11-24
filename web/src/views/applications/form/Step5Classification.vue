<script setup lang="ts">
import { computed } from 'vue'
import { DocumentChecked, MagicStick } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import CaregiverList from './CaregiverList.vue'
import { formatDate, formatMoney } from '../format'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const emit = defineEmits<{ (e: 'classify'): void; (e: 'complete'): void }>()

const classification = computed(() => props.form.classification)

const hasResult = computed(() => {
  const result = props.form.classification
  return Boolean(result.classificationResult.trim() || result.classificationText.trim())
})
</script>

<template>
  <div class="step-classification">
    <CaregiverList :form="form" :busy="busy" />

    <NcCard
      title="分类认定"
      subtitle="分类与保障金由服务端 Core 判定，前端仅展示结果，不参与计算。"
    >
      <template #actions>
        <span class="section-meta">{{ form.applicationNo || '未编号' }}</span>
      </template>

      <div class="result-banner" :class="{ 'is-unclassified': !hasResult }">
        <div class="result-identity">
          <span class="result-kicker">CLASSIFICATION / 分类认定</span>
          <strong class="result-name">
            {{
              hasResult
                ? classification.classificationText || classification.classificationResult
                : '尚未分类认定'
            }}
          </strong>
        </div>

        <span
          v-if="hasResult"
          class="eligibility-tag"
          :class="classification.isEligible ? 'is-eligible' : 'is-ineligible'"
        >
          {{ classification.isEligible ? '符合' : '不符合' }}
        </span>
      </div>

      <div v-if="!hasResult" class="unclassified">
        <strong>尚未完成分类认定</strong>
        <p>先完成前四步并保存，再执行分类认定。</p>
      </div>

      <template v-else>
        <dl class="detail-grid">
          <div class="detail-item">
            <dt>分类代码</dt>
            <dd class="mono">{{ classification.classificationResult || '—' }}</dd>
          </div>
          <div class="detail-item">
            <dt>中文名称</dt>
            <dd>{{ classification.classificationText || '—' }}</dd>
          </div>
          <div class="detail-item">
            <dt>是否符合</dt>
            <dd>{{ classification.isEligible ? '符合认定条件' : '不符合认定条件' }}</dd>
          </div>
          <div class="detail-item">
            <dt>渐退期</dt>
            <dd>{{ classification.isInGracePeriod ? '进入渐退期' : '未进入渐退期' }}</dd>
          </div>
          <div class="detail-item detail-item--wide">
            <dt>不符合原因</dt>
            <dd :class="{ 'is-empty': !classification.ineligibleReason }">
              {{ classification.ineligibleReason || '—' }}
            </dd>
          </div>
          <div class="detail-item detail-item--wide">
            <dt>判定依据</dt>
            <dd :class="{ 'is-empty': !classification.determinationBasis }">
              {{ classification.determinationBasis || '—' }}
            </dd>
          </div>
        </dl>

        <div class="amount-grid">
          <NcStat
            label="月保障金"
            :value="formatMoney(classification.householdMonthlyGuaranteeAmount)"
            hint="元 / 月"
            tone="ink"
            compact
          />
          <NcStat
            label="分类施保"
            :value="formatMoney(classification.classifiedSubsidyAmount)"
            hint="元 / 月"
            tone="amber"
            compact
          />
          <NcStat
            label="护理补贴"
            :value="formatMoney(classification.caregiverSubsidyAmount)"
            hint="元 / 月"
            tone="pine"
            compact
          />
          <NcStat
            label="合计"
            :value="formatMoney(classification.totalGuaranteeAmount)"
            hint="元 / 月"
            tone="seal"
            compact
          />
        </div>

        <div class="grace-row">
          <div class="grace-item">
            <span>渐退期</span>
            <strong :class="{ 'is-on': classification.isInGracePeriod }">
              {{ classification.isInGracePeriod ? '是' : '否' }}
            </strong>
          </div>
          <div class="grace-item">
            <span>渐退月份</span>
            <strong>
              {{
                classification.gracePeriodMonths != null
                  ? `${classification.gracePeriodMonths} 个月`
                  : '—'
              }}
            </strong>
          </div>
          <div class="grace-item">
            <span>结束日期</span>
            <strong>{{ formatDate(classification.gracePeriodEndDate) }}</strong>
          </div>
        </div>
      </template>

      <template #footer>
        <div class="result-actions">
          <el-button type="primary" :icon="MagicStick" :loading="busy" @click="emit('classify')">
            一键分类认定
          </el-button>
          <el-button :icon="DocumentChecked" :loading="busy" @click="emit('complete')">
            完成认定 / 生成档案
          </el-button>
          <span class="action-note">先保存前四步数据，再执行分类认定与完成建档。</span>
        </div>
      </template>
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

.section-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
  white-space: nowrap;
}

.result-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 16px 18px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-seal);
  background-color: var(--nc-paper-soft);
}

.result-banner.is-unclassified {
  border-left-style: dashed;
  border-left-color: var(--nc-line-strong);
}

.result-identity {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.result-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.16em;
}

.result-name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.3;
  letter-spacing: 0.04em;
  word-break: break-word;
}

.eligibility-tag {
  flex: none;
  padding: 3px 10px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.eligibility-tag.is-eligible {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.eligibility-tag.is-ineligible {
  border-color: rgba(171, 50, 39, 0.35);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.unclassified {
  margin-top: 14px;
  padding: 24px 18px;
  border: 1px dashed var(--nc-line-strong);
  background-color: var(--nc-paper-soft);
  text-align: center;
}

.unclassified strong {
  display: block;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.unclassified p {
  margin: 6px 0 0;
  color: var(--nc-muted-2);
  font-size: 13px;
  line-height: 1.7;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 24px;
  margin: 16px 0 0;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.detail-item {
  min-width: 0;
}

.detail-item--wide {
  grid-column: 1 / -1;
}

.detail-item dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.1em;
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

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.amount-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  margin-top: 16px;
}

.grace-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px 28px;
  margin-top: 14px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.grace-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 110px;
  line-height: 1.5;
}

.grace-item span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.grace-item strong {
  color: var(--nc-ink-2);
  font-size: 13px;
  font-weight: 600;
}

.grace-item strong.is-on {
  color: var(--nc-amber-dark);
}

.result-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.action-note {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (max-width: 1099px) {
  .amount-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .result-banner {
    padding: 14px 13px;
  }

  .result-name {
    font-size: 22px;
  }

  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .detail-item--wide {
    grid-column: auto;
  }

  .amount-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .result-actions {
    align-items: stretch;
  }

  .result-actions .el-button {
    flex: 1 1 100%;
  }

  .action-note {
    flex-basis: 100%;
    margin-left: 0;
    text-align: center;
  }
}

@media (max-width: 479px) {
  .amount-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .grace-row {
    gap: 10px 18px;
  }
}
</style>
