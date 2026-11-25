<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft } from '@element-plus/icons-vue'
import { getChangeManagementDetail, submitGraceConfirm } from '@/api/changeManagement'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { ChangeManagementDetail, GraceConfirmResult } from '@/types/changeManagement'
import { formatAmount, formatDate, formatMoney, formatPeriod, graceDaysText } from './format'

const LIST_PATH = '/social-assistance/change-management'

const route = useRoute()
const router = useRouter()

const applicationId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0
})

const detail = ref<ChangeManagementDetail | null>(null)
const loading = ref(false)
const loadError = ref('')
const months = ref(6)
const submitting = ref(false)
const savedResult = ref<GraceConfirmResult | null>(null)

const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

const grace = computed(() => detail.value?.grace ?? null)

const periodText = computed(() => {
  const current = grace.value
  return current ? formatPeriod(current.startDate, months.value) : '—'
})

const originalPeriodText = computed(() => {
  const current = grace.value
  return current ? formatPeriod(current.startDate, current.months) : '—'
})

const canSubmit = computed(
  () =>
    Boolean(detail.value) &&
    Boolean(grace.value) &&
    Boolean(detail.value?.canManage) &&
    Boolean(detail.value?.canGraceConfirm) &&
    !submitting.value,
)

const hintText = computed(() => {
  const saved = savedResult.value
  if (saved) {
    return `渐退期已确认：${saved.months} 个月（${formatDate(saved.startDate)} 至 ${formatDate(saved.endDate)}）。`
  }
  return '确认后将更新当前在效渐退期的月数与结束日期；不会重算分类与保障金。渐退资格须先经经济状况复核 Step5 分类判定产生。'
})

async function load(): Promise<void> {
  if (applicationId.value <= 0) {
    loadError.value = '申请 ID 无效'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    detail.value = await getChangeManagementDetail(applicationId.value)
    const current = detail.value.grace
    if (current?.months && current.months >= 1 && current.months <= 12) {
      months.value = current.months
    }
  } catch (error) {
    detail.value = null
    loadError.value = getErrorMessage(error, '渐退期确认数据加载失败')
  } finally {
    loading.value = false
  }
}

async function confirm(): Promise<void> {
  const current = detail.value
  if (!current || !grace.value) {
    ElMessage.warning('当前无进行中的渐退期，不能确认')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认将渐退期调整为 ${months.value} 个月（${periodText.value}）？`,
      '确认渐退期',
      { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  submitting.value = true
  try {
    savedResult.value = await submitGraceConfirm(current.applicationId, { months: months.value })
    ElMessage.success('渐退期已确认')
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '渐退期确认失败'))
  } finally {
    submitting.value = false
  }
}

function goBack(): void {
  void router.push(LIST_PATH)
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page grace-confirm-page">
    <NcPageHeader
      title="渐退确认"
      description="确认当前在效渐退期的月数与区间。分类与保障金由 Core 判定，本页不重算。"
      kicker="保障对象动态管理 · 渐退期"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回列表</el-button>
      </template>
    </NcPageHeader>

    <el-alert v-if="loadError" type="error" :title="loadError" :closable="false" show-icon>
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <el-skeleton v-if="loading" :rows="8" animated />

    <template v-else-if="detail">
      <el-alert
        v-if="!grace"
        type="warning"
        :closable="false"
        show-icon
        title="当前户无进行中的渐退期"
        description="渐退资格须在经济状况复核 Step5 分类判定后确认。本页不会凭空创建渐退期。"
      />

      <el-alert
        v-else-if="!detail.canManage"
        type="warning"
        :closable="false"
        show-icon
        title="当前账号无「办理信息变更」权限（CHANGE_MANAGE），不能确认/调整渐退期。"
      />

      <template v-if="grace">
        <NcCard title="分类变化" padding="md">
          <div class="classification-change">
            <div>
              <span>渐退前原分类</span>
              <strong>{{ grace?.originalClassificationName || grace?.originalClassification || '—' }}</strong>
            </div>
            <span class="arrow" aria-hidden="true">→</span>
            <div>
              <span>本次判定分类</span>
              <strong class="is-current">{{ detail.classificationName || '—' }}</strong>
            </div>
          </div>
        </NcCard>

        <NcCard title="收入与标准" padding="md">
          <div class="info-grid">
            <div>
              <span>低保标准（元/人·月）</span>
              <strong>{{ formatMoney(detail.subsistenceStandard) }}</strong>
            </div>
            <div>
              <span>月人均收入（元）</span>
              <strong>{{ formatMoney(detail.perCapitaIncome) }}</strong>
            </div>
            <div>
              <span>年家庭总收入（元）</span>
              <strong>{{ formatMoney(detail.totalAnnualIncome) }}</strong>
            </div>
            <div>
              <span>家庭人数（人）</span>
              <strong>{{ detail.familySize }}</strong>
            </div>
          </div>
        </NcCard>

        <NcCard title="保障金与渐退期" padding="md">
          <div class="info-grid">
            <div>
              <span>保障金额（元/月）</span>
              <strong>{{ formatMoney(detail.totalGuaranteeAmount) }}</strong>
            </div>
            <div>
              <span>原保障金额（渐退前）</span>
              <strong>{{ formatAmount(grace?.originalGuaranteeAmount) }}</strong>
            </div>
            <div>
              <span>渐退期应发（元/月）</span>
              <strong>{{ formatAmount(grace?.graceGrantAmount) }}</strong>
            </div>
            <div>
              <span>剩余天数</span>
              <strong class="grace-days">{{ graceDaysText(grace?.daysLeft) }}</strong>
            </div>
          </div>

          <div class="period-row">
            <div class="field">
              <span>渐退期月数（1–12）</span>
              <el-select v-model="months" class="month-select">
                <el-option
                  v-for="option in monthOptions"
                  :key="option"
                  :label="`${option} 个月`"
                  :value="option"
                />
              </el-select>
            </div>
            <div class="period-preview">
              <span>调整后区间</span>
              <strong>{{ periodText }}</strong>
              <small v-if="grace?.months !== months">原区间：{{ originalPeriodText }}</small>
            </div>
          </div>

          <p class="grace-hint">{{ hintText }}</p>
        </NcCard>
      </template>

      <div class="footer-bar">
        <span class="footer-note">
          {{ grace ? `当前渐退期：${originalPeriodText}` : '当前无在效渐退期' }}
        </span>
        <div class="footer-actions">
          <el-button @click="goBack">取消</el-button>
          <el-button
            type="primary"
            :disabled="!canSubmit"
            :loading="submitting"
            @click="confirm"
          >
            确认渐退期
          </el-button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.grace-confirm-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.classification-change {
  display: flex;
  align-items: center;
  gap: 18px;
}

.classification-change > div {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.classification-change span {
  color: var(--nc-muted);
  font-size: 10px;
}

.classification-change strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.classification-change strong.is-current {
  color: var(--nc-seal);
}

.arrow {
  flex: none;
  color: var(--nc-muted);
  font-size: 20px;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.info-grid > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.info-grid span {
  color: var(--nc-muted);
  font-size: 10px;
}

.info-grid strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.info-grid strong.grace-days {
  color: var(--nc-amber);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.period-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 18px;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field > span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.month-select {
  width: 150px;
}

.period-preview {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.period-preview > span {
  color: var(--nc-muted);
  font-size: 10px;
}

.period-preview strong {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.period-preview small {
  color: var(--nc-muted);
  font-size: 10px;
}

.grace-hint {
  margin-top: 12px;
  padding: 9px 11px;
  border: 1px solid rgba(166, 106, 0, 0.28);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.footer-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 13px 16px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
}

.footer-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.footer-actions {
  display: flex;
  gap: 8px;
}

@media (max-width: 760px) {
  .classification-change {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }

  .arrow {
    display: none;
  }

  .info-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .period-row {
    flex-direction: column;
    align-items: stretch;
  }

  .month-select {
    width: 100%;
  }

  .footer-actions {
    width: 100%;
  }

  .footer-actions .el-button {
    flex: 1;
  }
}
</style>
