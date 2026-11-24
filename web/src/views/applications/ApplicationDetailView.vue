<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Edit, Refresh } from '@element-plus/icons-vue'
import { getApplication } from '@/api/applications'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ApplicationDetail } from '@/types/application'
import ApplicationStatusTag from './ApplicationStatusTag.vue'
import { formatDate, formatDateTime, formatMoney, formatStep } from './format'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const detail = ref<ApplicationDetail | null>(null)
const loading = ref(true)
const loadError = ref('')

let requestTicket = 0

const applicationId = computed(() => {
  const raw = route.params.id
  return Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
})

const pageTitle = computed(() =>
  detail.value
    ? `${detail.value.applicantName} · ${detail.value.applicationNo}`
    : '申请详情',
)

const pageDescription = computed(() => {
  if (!detail.value) return '查看社会救助申请档案、家庭成员与收入情况。'
  const place = [detail.value.town, detail.value.village].filter(Boolean).join(' / ')
  return place ? `社会救助申请档案 · ${place}` : '社会救助申请档案'
})

const profileText = computed(() => {
  if (!detail.value) return '—'
  return (
    [detail.value.gender, detail.value.ethnicity, detail.value.hukouType]
      .filter(Boolean)
      .join(' · ') || '—'
  )
})

const locationText = computed(() => {
  if (!detail.value) return '—'
  const parts = [detail.value.town, detail.value.village].filter(Boolean)
  if (parts.length > 0) return parts.join(' / ')
  return detail.value.address || '—'
})

async function load(): Promise<void> {
  const ticket = ++requestTicket
  const id = String(applicationId.value)

  if (!id) {
    loading.value = false
    detail.value = null
    loadError.value = '缺少申请编号'
    return
  }

  loading.value = true
  loadError.value = ''
  detail.value = null

  try {
    const response = await getApplication(id)
    if (ticket !== requestTicket) return
    detail.value = response
  } catch (error) {
    if (ticket !== requestTicket) return
    loadError.value = getErrorMessage(error, '申请详情加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function backToList(): void {
  void router.push({ name: 'applications' })
}

watch(
  () => route.params.id,
  () => {
    if (route.name === 'application-detail') void load()
  },
)

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page application-detail-page">
    <NcPageHeader
      :title="pageTitle"
      :description="pageDescription"
      kicker="社会救助 · 申请档案"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button :icon="Edit" disabled title="编辑与提交功能将在下一批上线">
          编辑（即将上线）
        </el-button>
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
      <NcCard
        title="申请概要"
        :subtitle="`最近更新 ${formatDateTime(detail.updatedAt)}`"
        class="overview-card"
      >
        <template #actions>
          <ApplicationStatusTag :status="detail.status" :status-text="detail.statusText" />
        </template>

        <div class="overview-head">
          <div class="applicant">
            <span class="field-kicker">申请人</span>
            <strong class="applicant-name">{{ detail.applicantName }}</strong>
            <span class="applicant-extra">{{ profileText }}</span>
          </div>

          <div class="status-pair">
            <span class="step-chip">当前步骤 · {{ formatStep(detail.currentStep) }}</span>
            <span class="step-chip">分类 · {{ detail.classificationResult || '—' }}</span>
          </div>
        </div>

        <div class="amount-grid">
          <NcStat label="家庭人数" :value="`${detail.familySize} 人`" tone="ink" compact />
          <NcStat
            label="年总收入"
            :value="formatMoney(detail.totalAnnualIncome)"
            hint="元"
            tone="ink"
            compact
          />
          <NcStat
            label="年人均收入"
            :value="formatMoney(detail.perCapitaAnnualIncome)"
            hint="元"
            tone="ink"
            compact
          />
          <NcStat
            label="家庭月收入"
            :value="formatMoney(detail.totalFamilyIncome)"
            hint="元"
            tone="ink"
            compact
          />
          <NcStat
            label="月人均收入"
            :value="formatMoney(detail.perCapitaIncome)"
            hint="元"
            tone="amber"
            compact
          />
          <NcStat
            label="月保障金"
            :value="formatMoney(detail.householdMonthlyGuaranteeAmount)"
            hint="元"
            tone="pine"
            compact
          />
        </div>

        <dl class="overview-list">
          <div>
            <dt>申请编号</dt>
            <dd class="mono">{{ detail.applicationNo }}</dd>
          </div>
          <div>
            <dt>身份证号</dt>
            <dd class="mono">{{ detail.applicantIdCard || '—' }}</dd>
          </div>
          <div>
            <dt>联系电话</dt>
            <dd class="mono">{{ detail.applicantPhone || '—' }}</dd>
          </div>
          <div>
            <dt>乡镇 / 村</dt>
            <dd>{{ locationText }}</dd>
          </div>
          <div>
            <dt>户籍地址</dt>
            <dd>{{ detail.hukouAddress || '—' }}</dd>
          </div>
          <div>
            <dt>现居住地址</dt>
            <dd>{{ detail.address || '—' }}</dd>
          </div>
          <div>
            <dt>申请时间</dt>
            <dd>{{ formatDateTime(detail.createdAt) }}</dd>
          </div>
          <div>
            <dt>更新时间</dt>
            <dd>{{ formatDateTime(detail.updatedAt) }}</dd>
          </div>
          <div>
            <dt>提交时间</dt>
            <dd>{{ detail.submitAt ? formatDateTime(detail.submitAt) : '尚未提交' }}</dd>
          </div>
        </dl>
      </NcCard>

      <NcCard
        title="家庭成员"
        :subtitle="`共 ${detail.members.length} 人`"
        padding="none"
        class="members-card"
      >
        <template v-if="detail.members.length > 0">
          <el-table
            v-if="!isNarrow"
            :data="detail.members"
            row-key="id"
            class="member-table"
          >
            <el-table-column label="姓名" min-width="80">
              <template #default="{ row }">
                <strong class="member-name">{{ row.name }}</strong>
              </template>
            </el-table-column>
            <el-table-column prop="relation" label="与户主关系" width="100" />
            <el-table-column label="身份证号" min-width="170">
              <template #default="{ row }">
                <span class="mono">{{ row.idCard || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="gender" label="性别" width="60" align="center" />
            <el-table-column label="出生日期" width="105">
              <template #default="{ row }">{{ formatDate(row.birthDate) }}</template>
            </el-table-column>
            <el-table-column label="年龄" width="60" align="center">
              <template #default="{ row }">{{ row.age }} 岁</template>
            </el-table-column>
            <el-table-column label="健康状况" min-width="90">
              <template #default="{ row }">{{ row.healthStatus || '—' }}</template>
            </el-table-column>
            <el-table-column label="就业状况" min-width="90">
              <template #default="{ row }">{{ row.employmentStatus || '—' }}</template>
            </el-table-column>
            <el-table-column label="月收入（元）" width="110" align="right">
              <template #default="{ row }">
                <span class="mono">{{ formatMoney(row.monthlyIncome) }}</span>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="member-cards">
            <article v-for="member in detail.members" :key="member.id" class="member-card">
              <header class="member-head">
                <strong>{{ member.name }}</strong>
                <span class="member-relation">{{ member.relation || '家庭成员' }}</span>
              </header>

              <dl class="member-list">
                <div>
                  <dt>身份证号</dt>
                  <dd class="mono">{{ member.idCard || '—' }}</dd>
                </div>
                <div>
                  <dt>性别 / 年龄</dt>
                  <dd>{{ member.gender || '—' }} · {{ member.age }} 岁</dd>
                </div>
                <div>
                  <dt>出生日期</dt>
                  <dd>{{ formatDate(member.birthDate) }}</dd>
                </div>
                <div>
                  <dt>健康状况</dt>
                  <dd>{{ member.healthStatus || '—' }}</dd>
                </div>
                <div>
                  <dt>就业状况</dt>
                  <dd>{{ member.employmentStatus || '—' }}</dd>
                </div>
                <div>
                  <dt>月收入</dt>
                  <dd>{{ formatMoney(member.monthlyIncome) }} 元</dd>
                </div>
              </dl>
            </article>
          </div>
        </template>

        <div v-else class="member-empty">
          <el-empty description="暂无家庭成员信息" :image-size="64" />
        </div>
      </NcCard>

      <NcCard title="申请原因与补充说明" class="reason-card">
        <div class="reason-grid">
          <div class="reason-block">
            <span class="reason-label">申请原因</span>
            <p class="reason-text">{{ detail.applicationReason || '—' }}</p>
          </div>
          <div class="reason-block">
            <span class="reason-label">补充说明</span>
            <p class="reason-text">{{ detail.applicationReasonDetail || '暂无补充说明' }}</p>
          </div>
        </div>
      </NcCard>
    </template>
  </div>
</template>

<style scoped>
.application-detail-page {
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

/* ── 概要 ─────────────────────────────────────────────────────── */
.overview-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 14px;
}

.applicant {
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

.applicant-name {
  margin-top: 4px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 24px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.applicant-extra {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.status-pair {
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

.overview-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 26px;
  margin: 16px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.overview-list > div {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
  padding: 7px 0;
  border-bottom: 1px dotted var(--nc-line-faint);
}

.overview-list dt {
  flex: none;
  width: 84px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.overview-list dd {
  min-width: 0;
  margin: 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
  word-break: break-all;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

/* ── 家庭成员 ─────────────────────────────────────────────────── */
.members-card {
  overflow: hidden;
}

.member-table {
  width: 100%;
}

.member-name {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.member-cards {
  display: grid;
  gap: 10px;
  padding: 12px;
}

.member-card {
  padding: 13px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.member-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.member-head strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.member-relation {
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
}

.member-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 7px 14px;
  margin: 9px 0 0;
}

.member-list > div {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.5;
}

.member-list dt {
  color: var(--nc-muted);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.member-list dd {
  margin: 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.member-empty {
  padding: 8px;
}

/* ── 申请原因 ─────────────────────────────────────────────────── */
.reason-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.reason-block {
  min-width: 0;
  padding-left: 12px;
  border-left: 2px solid var(--nc-seal);
}

.reason-block + .reason-block {
  border-left-color: var(--nc-amber);
}

.reason-label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.reason-text {
  margin-top: 6px;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.75;
  white-space: pre-wrap;
  word-break: break-word;
}

/* ── 手机 ─────────────────────────────────────────────────────── */
@media (max-width: 767px) {
  .applicant-name {
    font-size: 21px;
  }

  .amount-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }

  .overview-list {
    grid-template-columns: 1fr;
    gap: 0;
  }

  .overview-list dt {
    width: 78px;
  }

  .member-cards {
    padding: 10px;
  }

  .reason-grid {
    grid-template-columns: 1fr;
    gap: 14px;
  }
}
</style>
