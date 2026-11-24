<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import { getElderlyApplication } from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import type { ElderlyApplicationDetail } from '@/types/elderly'
import ElderlyChip from './ElderlyChip.vue'
import { applicationStatusTone, formatAge, formatDate, formatDateTime, formatMoney, formatMonth } from './display'

const route = useRoute()
const router = useRouter()

const detail = ref<ElderlyApplicationDetail | null>(null)
const loading = ref(true)
const loadError = ref('')

let requestTicket = 0

const applicationId = computed(() => {
  const raw = route.params.id
  return Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
})

const pageTitle = computed(() =>
  detail.value ? `${detail.value.name} · ${detail.value.applicationNo}` : '高龄津贴档案',
)

const pageDescription = computed(() => {
  if (!detail.value) return '查看高龄津贴登记档案、待遇标准与停发信息。'
  return `普惠高龄津贴档案 · ${detail.value.categoryText || '未分类'}`
})

const hukouFullAddress = computed(() => {
  if (!detail.value) return '—'
  const parts = [
    detail.value.hukouAddress,
    detail.value.hukouVillage,
    detail.value.hukouDetailAddress,
  ].filter((part) => Boolean(part && part.trim()))
  return parts.length > 0 ? parts.join(' ') : '—'
})

const familyFullAddress = computed(() => {
  if (!detail.value) return '—'
  const parts = [detail.value.familyAddress, detail.value.detailAddress]
    .filter((part) => Boolean(part && part.trim()))
  return parts.length > 0 ? parts.join(' ') : '—'
})

const paybackRange = computed(() => {
  if (!detail.value) return '—'
  if (!detail.value.paybackStartMonth && !detail.value.paybackEndMonth) return '—'
  return `${formatMonth(detail.value.paybackStartMonth)} 至 ${formatMonth(detail.value.paybackEndMonth)}`
})

const recoverRange = computed(() => {
  if (!detail.value) return '—'
  if (!detail.value.recoverStartMonth && !detail.value.recoverEndMonth) return '—'
  return `${formatMonth(detail.value.recoverStartMonth)} 至 ${formatMonth(detail.value.recoverEndMonth)}`
})

function text(value: string | null | undefined): string {
  return value && value.trim() ? value : '—'
}

function yesNo(value: boolean): string {
  return value ? '是' : '否'
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  const id = String(applicationId.value)

  if (!id) {
    loading.value = false
    detail.value = null
    loadError.value = '缺少档案编号'
    return
  }

  loading.value = true
  loadError.value = ''
  detail.value = null

  try {
    const response = await getElderlyApplication(id)
    if (ticket !== requestTicket) return
    detail.value = response
  } catch (error) {
    if (ticket !== requestTicket) return
    loadError.value = getErrorMessage(error, '高龄津贴档案加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function backToList(): void {
  void router.push({ name: 'elderly-applications' })
}

watch(
  () => route.params.id,
  () => {
    if (route.name === 'elderly-application-detail') void load()
  },
)

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-detail-page">
    <NcPageHeader :title="pageTitle" :description="pageDescription" kicker="普惠高龄 · 登记档案">
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <NcCard v-if="loading" padding="lg" class="state-card">
      <el-skeleton :rows="8" animated />
    </NcCard>

    <el-alert
      v-else-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <div class="error-actions">
        <el-button size="small" :icon="Refresh" @click="load()">重新加载</el-button>
        <el-button size="small" @click="backToList">返回列表</el-button>
      </div>
    </el-alert>

    <template v-else-if="detail">
      <NcCard title="档案概要" class="overview-card">
        <template #actions>
          <ElderlyChip :text="detail.statusText || detail.status" :tone="applicationStatusTone(detail.status)" />
        </template>

        <div class="overview-head">
          <div class="person">
            <span class="field-kicker">申请人</span>
            <strong class="person-name">{{ detail.name }}</strong>
            <span class="person-extra">
              {{ detail.gender || '—' }} · {{ formatAge(detail.age) }} · {{ text(detail.categoryText) }}
            </span>
          </div>

          <div class="chip-pair">
            <span class="step-chip">申请编号 · {{ detail.applicationNo }}</span>
            <span class="step-chip">计发年月 · {{ formatMonth(detail.issueStartMonth) }}</span>
          </div>
        </div>

        <div class="amount-grid">
          <NcStat label="月标准" :value="formatMoney(detail.issueAmount)" hint="元/月" tone="seal" compact />
          <NcStat label="计发起始月" :value="formatMonth(detail.issueStartMonth)" tone="ink" compact />
          <NcStat label="补发月数" :value="`${detail.paybackMonths} 个月`" tone="amber" compact />
          <NcStat label="补发金额" :value="formatMoney(detail.paybackAmount)" hint="元" tone="amber" compact />
        </div>

        <dl class="info-list">
          <div>
            <dt>身份证号</dt>
            <dd class="mono">{{ text(detail.idCard) }}</dd>
          </div>
          <div>
            <dt>联系电话</dt>
            <dd class="mono">{{ text(detail.phone) }}</dd>
          </div>
          <div>
            <dt>出生日期</dt>
            <dd>{{ formatDate(detail.birthDate) }}</dd>
          </div>
          <div>
            <dt>年龄</dt>
            <dd>{{ formatAge(detail.age) }}</dd>
          </div>
          <div>
            <dt>受理日期</dt>
            <dd>{{ formatDate(detail.applyDate) }}</dd>
          </div>
          <div>
            <dt>确认时间</dt>
            <dd>{{ formatDateTime(detail.confirmedAt) }}</dd>
          </div>
          <div>
            <dt>确认人</dt>
            <dd>{{ text(detail.confirmedBy) }}</dd>
          </div>
          <div>
            <dt>身份比对</dt>
            <dd>{{ text(detail.identityFlag) }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="户籍与居住地址">
        <dl class="info-list info-list--single">
          <div>
            <dt>户籍地址</dt>
            <dd>{{ hukouFullAddress }}</dd>
          </div>
          <div>
            <dt>家庭住址</dt>
            <dd>{{ familyFullAddress }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="待遇发放与社保卡">
        <dl class="info-list">
          <div>
            <dt>享受类别</dt>
            <dd>{{ text(detail.categoryText) }}</dd>
          </div>
          <div>
            <dt>月标准</dt>
            <dd class="mono">{{ formatMoney(detail.issueAmount) }} 元/月</dd>
          </div>
          <div>
            <dt>计发起始月</dt>
            <dd>{{ formatMonth(detail.issueStartMonth) }}</dd>
          </div>
          <div>
            <dt>类别人工修正</dt>
            <dd>{{ yesNo(detail.isCategoryManual) }}</dd>
          </div>
          <div>
            <dt>社保卡开户行</dt>
            <dd>{{ text(detail.bankName) }}</dd>
          </div>
          <div>
            <dt>社保卡账号</dt>
            <dd class="mono">{{ text(detail.bankAccount) }}</dd>
          </div>
          <div>
            <dt>身份比对来源</dt>
            <dd>{{ text(detail.identitySource) }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="代办与代领信息">
        <dl class="info-list">
          <div>
            <dt>代办人</dt>
            <dd>{{ text(detail.agentName) }}</dd>
          </div>
          <div>
            <dt>代办关系</dt>
            <dd>{{ text(detail.agentRelation) }}</dd>
          </div>
          <div>
            <dt>代领人</dt>
            <dd>{{ text(detail.agentReceiveName) }}</dd>
          </div>
          <div>
            <dt>代领关系</dt>
            <dd>{{ text(detail.agentReceiveRelation) }}</dd>
          </div>
          <div>
            <dt>代领人开户行</dt>
            <dd>{{ text(detail.agentReceiveBankName) }}</dd>
          </div>
          <div>
            <dt>代领人账号</dt>
            <dd class="mono">{{ text(detail.agentReceiveBankAccount) }}</dd>
          </div>
          <div class="span-2">
            <dt>代领原因</dt>
            <dd>{{ text(detail.agentReceiveReason) }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="补发与特殊情况">
        <dl class="info-list">
          <div>
            <dt>补发起止</dt>
            <dd>{{ paybackRange }}</dd>
          </div>
          <div>
            <dt>补发月数</dt>
            <dd>{{ detail.paybackMonths }} 个月</dd>
          </div>
          <div>
            <dt>补发金额</dt>
            <dd class="mono">{{ formatMoney(detail.paybackAmount) }} 元</dd>
          </div>
          <div>
            <dt>补发原因</dt>
            <dd>{{ text(detail.paybackReason) }}</dd>
          </div>
          <div>
            <dt>特殊情况</dt>
            <dd>{{ yesNo(detail.isSpecialCase) }}</dd>
          </div>
          <div class="span-2">
            <dt>特殊情况说明</dt>
            <dd>{{ text(detail.specialReason) }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="停发与追缴">
        <dl class="info-list">
          <div>
            <dt>当前状态</dt>
            <dd>{{ text(detail.statusText) }}</dd>
          </div>
          <div>
            <dt>停发原因</dt>
            <dd>{{ text(detail.stopReasonText) }}</dd>
          </div>
          <div>
            <dt>应停发时间</dt>
            <dd>{{ formatDate(detail.dueStopDate) }}</dd>
          </div>
          <div>
            <dt>实际停发时间</dt>
            <dd>{{ formatDate(detail.actualStopDate) }}</dd>
          </div>
          <div>
            <dt>停发操作时间</dt>
            <dd>{{ formatDateTime(detail.stoppedAt) }}</dd>
          </div>
          <div>
            <dt>停发经办人</dt>
            <dd>{{ text(detail.stoppedBy) }}</dd>
          </div>
          <div>
            <dt>是否追缴</dt>
            <dd>{{ yesNo(detail.isRecover) }}</dd>
          </div>
          <div>
            <dt>追缴起止</dt>
            <dd>{{ recoverRange }}</dd>
          </div>
          <div>
            <dt>追缴金额</dt>
            <dd class="mono">{{ formatMoney(detail.recoverAmount) }} 元</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard title="备注与记录时间">
        <p class="remark">{{ text(detail.remark) }}</p>
        <dl class="info-list info-list--single record-list">
          <div>
            <dt>创建时间</dt>
            <dd>{{ formatDateTime(detail.createdAt) }}</dd>
          </div>
          <div>
            <dt>最近更新</dt>
            <dd>{{ formatDateTime(detail.updatedAt) }}</dd>
          </div>
        </dl>
      </NcCard>
    </template>
  </div>
</template>

<style scoped>
.elderly-detail-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.state-card {
  min-height: 300px;
}

.load-error {
  margin: 0;
}

.error-actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.overview-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 14px;
}

.person {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.4;
}

.field-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.16em;
}

.person-name {
  margin-top: 4px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 24px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.person-extra {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.chip-pair {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.step-chip {
  display: inline-flex;
  align-items: center;
  padding: 3px 9px;
  border: 1px solid var(--nc-line);
  border-left: 2px solid var(--nc-seal);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.step-chip + .step-chip {
  border-left-color: var(--nc-pine);
}

.amount-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 10px;
  margin-top: 16px;
}

.info-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 26px;
  margin: 0;
}

.info-list--single {
  grid-template-columns: minmax(0, 1fr);
}

.overview-card .info-list {
  margin-top: 16px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.info-list > div {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
  padding: 7px 0;
  border-bottom: 1px dotted var(--nc-line-faint);
}

.info-list dt {
  flex: none;
  width: 98px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.info-list dd {
  min-width: 0;
  margin: 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
  word-break: break-all;
}

.info-list .span-2 {
  grid-column: span 2;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.remark {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.8;
  white-space: pre-wrap;
  word-break: break-word;
}

.record-list {
  margin-top: 12px;
  padding-top: 4px;
  border-top: 1px solid var(--nc-line-faint);
}

@media (max-width: 767px) {
  .person-name {
    font-size: 21px;
  }

  .amount-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }

  .info-list {
    grid-template-columns: minmax(0, 1fr);
    gap: 0;
  }

  .info-list .span-2 {
    grid-column: auto;
  }

  .info-list dt {
    width: 86px;
  }
}
</style>
