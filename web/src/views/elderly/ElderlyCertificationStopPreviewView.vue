<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Edit, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  getStopPreview,
  listCertificationCampaigns,
  setCertificationStopDecision,
  stopCertificationTask,
} from '@/api/elderlyCertification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  CertificationCampaign,
  CertificationStopDecision,
  CertificationStopPreviewItem,
  CertificationStopRequest,
} from '@/types/elderlyCertification'
import ElderlyChip from './ElderlyChip.vue'
import {
  campaignOptionLabel,
  certStatusTone,
  evidenceStatusTone,
  stopDecisionTone,
} from './certificationDisplay'
import { formatAge, formatDate } from './display'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const STOP_CERT_STATUS_OPTIONS = [
  { value: 'Pending', label: '待核查' },
  { value: 'Refused', label: '拒绝核查' },
  { value: 'Failed', label: '核查不通过' },
  { value: 'Deceased', label: '已去世' },
  { value: 'MovedOut', label: '已迁出' },
  { value: 'NotLocated', label: '无法联系' },
] as const

const campaigns = ref<CertificationCampaign[]>([])
const campaignsLoading = ref(false)
const campaignsError = ref('')
const selectedCampaignId = ref<number | null>(null)

const keyword = ref('')
const certStatusFilter = ref('')
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const totalPages = ref(0)
const rows = ref<CertificationStopPreviewItem[]>([])
const loading = ref(false)
const loadError = ref('')

const decisionVisible = ref(false)
const decisionRow = ref<CertificationStopPreviewItem | null>(null)
const savingDecision = ref(false)
const decisionForm = reactive({
  decision: 'Deferred' as CertificationStopDecision,
  note: '',
  followUpDate: '',
})

const STOP_REASON_OPTIONS = [
  { value: 'DECEASED', label: '去世' },
  { value: 'MOVED_OUT', label: '户籍迁出本县' },
  { value: 'QUALIFICATION_LOST', label: '身份资格取消' },
  { value: 'CERTIFICATION_FAIL', label: '不符合认证要求' },
  { value: 'OTHER', label: '其他' },
] as const

const stopVisible = ref(false)
const stopRow = ref<CertificationStopPreviewItem | null>(null)
const savingStop = ref(false)
const stopForm = reactive({
  stopReason: 'CERTIFICATION_FAIL',
  dueStopDate: '',
  deathDate: '',
  isRecover: false,
  recoverStartMonth: '',
  recoverEndMonth: '',
  recoverAmount: 0,
  remark: '',
  otherReasonText: '',
})

let listTicket = 0

const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const hasCampaign = computed(() => selectedCampaignId.value !== null)
const selectedCampaign = computed(
  () => campaigns.value.find((item) => item.id === selectedCampaignId.value) ?? null,
)

const decisionTitle = computed(() => {
  switch (decisionForm.decision) {
    case 'Excluded':
      return '排除停发候选'
    case 'Pending':
      return '重新进入待停发'
    default:
      return '暂缓停发'
  }
})

/* ── 批次 ───────────────────────────────────────────────────────── */

async function loadCampaigns(): Promise<void> {
  campaignsLoading.value = true
  campaignsError.value = ''
  try {
    const list = await listCertificationCampaigns()
    campaigns.value = list
    const requested = Number(route.query.campaignId)
    if (Number.isFinite(requested) && list.some((item) => item.id === requested)) {
      selectedCampaignId.value = requested
    } else if (!list.some((item) => item.id === selectedCampaignId.value)) {
      selectedCampaignId.value = list[0]?.id ?? null
    }
  } catch (error) {
    campaigns.value = []
    selectedCampaignId.value = null
    campaignsError.value = getErrorMessage(error, '认证批次加载失败')
  } finally {
    campaignsLoading.value = false
  }
}

function selectCampaign(value: number | string): void {
  const id = Number(value)
  selectedCampaignId.value = id
  page.value = 1
  void router.replace({ query: { ...route.query, campaignId: String(id) } })
  void load()
}

/* ── 列表 ───────────────────────────────────────────────────────── */

async function load(): Promise<void> {
  const campaignId = selectedCampaignId.value
  const ticket = ++listTicket
  if (campaignId === null) {
    rows.value = []
    totalCount.value = 0
    totalPages.value = 0
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    const response = await getStopPreview(campaignId, {
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      certStatus: certStatusFilter.value || undefined,
    })
    if (ticket !== listTicket) return
    rows.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== listTicket) return
    rows.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '停发预览加载失败')
  } finally {
    if (ticket === listTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  certStatusFilter.value = ''
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

/* ── 停发决定 ───────────────────────────────────────────────────── */

function openDecision(
  row: CertificationStopPreviewItem,
  decision: CertificationStopDecision,
): void {
  decisionRow.value = row
  decisionForm.decision = decision
  decisionForm.note = ''
  decisionForm.followUpDate = ''
  decisionVisible.value = true
}

function closeDecision(): void {
  decisionVisible.value = false
  decisionRow.value = null
}

async function submitDecision(): Promise<void> {
  const row = decisionRow.value
  if (!row) return
  if (decisionForm.decision === 'Deferred' && !decisionForm.note.trim()) {
    ElMessage.warning('暂缓停发时请填写原因')
    return
  }

  savingDecision.value = true
  try {
    await setCertificationStopDecision(row.id, {
      decision: decisionForm.decision,
      note: decisionForm.note.trim(),
      followUpDate:
        decisionForm.decision === 'Deferred' && decisionForm.followUpDate
          ? decisionForm.followUpDate
          : null,
    })
    ElMessage.success(
      decisionForm.decision === 'Deferred'
        ? '已暂缓停发'
        : decisionForm.decision === 'Excluded'
          ? '已排除停发'
          : '已重新进入待停发',
    )
    closeDecision()
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存停发决定失败'))
  } finally {
    savingDecision.value = false
  }
}

function todayIso(): string {
  const date = new Date()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

function goStop(row: CertificationStopPreviewItem): void {
  if (!row.applicationId) {
    ElMessage.warning('该人员名册尚未建档，无法办理停发')
    return
  }

  stopRow.value = row
  stopForm.stopReason = row.suggestedStopReason || 'CERTIFICATION_FAIL'
  stopForm.dueStopDate = row.dueStopDate || todayIso()
  stopForm.deathDate = row.deathDate || ''
  stopForm.isRecover = false
  stopForm.recoverStartMonth = ''
  stopForm.recoverEndMonth = ''
  stopForm.recoverAmount = 0
  stopForm.remark = row.suggestedStopNote || ''
  stopForm.otherReasonText = ''
  stopVisible.value = true
}

function closeStop(): void {
  stopVisible.value = false
  stopRow.value = null
}

async function submitStop(): Promise<void> {
  const row = stopRow.value
  if (!row) return
  if (!stopForm.stopReason) {
    ElMessage.warning('请选择停发原因')
    return
  }
  if (stopForm.stopReason === 'OTHER' && !stopForm.otherReasonText.trim()) {
    ElMessage.warning('停发原因为“其他”时请填写说明')
    return
  }
  if (stopForm.stopReason === 'DECEASED' && !stopForm.deathDate) {
    ElMessage.warning('停发原因为“去世”时请选择死亡日期')
    return
  }
  if (stopForm.isRecover) {
    if (!stopForm.recoverStartMonth || !stopForm.recoverEndMonth) {
      ElMessage.warning('勾选追缴时请填写追缴起止月')
      return
    }
    if (stopForm.recoverStartMonth > stopForm.recoverEndMonth) {
      ElMessage.warning('追缴止月不能早于追缴起月')
      return
    }
  }

  const request: CertificationStopRequest = {
    stopReason: stopForm.stopReason,
    dueStopDate: stopForm.dueStopDate || null,
    deathDate: stopForm.stopReason === 'DECEASED' ? stopForm.deathDate || null : null,
    isRecover: stopForm.isRecover,
    recoverStartMonth: stopForm.isRecover ? stopForm.recoverStartMonth : '',
    recoverEndMonth: stopForm.isRecover ? stopForm.recoverEndMonth : '',
    recoverAmount: stopForm.isRecover ? Number(stopForm.recoverAmount) || 0 : 0,
    remark: stopForm.remark.trim(),
    otherReasonText: stopForm.stopReason === 'OTHER' ? stopForm.otherReasonText.trim() : undefined,
  }

  savingStop.value = true
  try {
    await stopCertificationTask(row.id, request)
    ElMessage.success('已办理停发并回写认证任务')
    closeStop()
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '停发失败'))
  } finally {
    savingStop.value = false
  }
}

function goBack(): void {
  void router.push({
    path: '/elderly/certifications',
    query:
      selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

function goInfoChanges(): void {
  void router.push({
    path: '/elderly/certifications/info-changes',
    query:
      selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

onMounted(async () => {
  await loadCampaigns()
  await load()
})
</script>

<template>
  <div class="nc-page stop-preview-page">
    <NcPageHeader
      title="年度认证停发预览"
      description="只读筛选建议停发人员 · 标记暂缓/排除 · 逐人停发仍走现有停发办理页"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回认证名单</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
        <el-button :icon="Edit" @click="goInfoChanges">变更审批</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      type="info"
      :closable="false"
      show-icon
      title="本页不执行批量停发。点击「办理停发」逐人确认，停发成功后才回写为「已停发」。"
    />

    <NcCard class="campaign-card">
      <div class="campaign-bar">
        <div class="campaign-picker">
          <span class="bar-label">认证批次</span>
          <el-select
            v-model="selectedCampaignId"
            class="campaign-select"
            placeholder="请选择认证批次"
            :loading="campaignsLoading"
            :disabled="campaigns.length === 0"
            @change="selectCampaign"
          >
            <el-option
              v-for="item in campaigns"
              :key="item.id"
              :value="item.id"
              :label="campaignOptionLabel(item.displayName || item.name, item.year, item.round)"
            />
          </el-select>
        </div>
        <div v-if="selectedCampaign" class="campaign-meta">
          <span>{{ selectedCampaign.year }} 年 · 第 {{ selectedCampaign.round }} 轮</span>
          <span>{{ selectedCampaign.statusText }}</span>
          <span>当前共 {{ totalCount }} 人进入停发预览</span>
        </div>
      </div>
    </NcCard>

    <el-alert
      v-if="campaignsError"
      class="load-error"
      type="error"
      :title="campaignsError"
      :closable="false"
      show-icon
    />

    <el-empty
      v-if="!campaignsLoading && campaigns.length === 0"
      description="暂无认证批次，请先在年度认证页新建批次并生成名单"
    >
      <el-button type="primary" @click="goBack">返回年度认证</el-button>
    </el-empty>

    <template v-if="hasCampaign">
      <NcCard padding="none" class="filter-card">
        <form class="filter-bar" @submit.prevent="handleSearch">
          <el-input
            v-model="keyword"
            class="filter-search"
            clearable
            :prefix-icon="Search"
            placeholder="搜索姓名、身份证号"
            @clear="handleSearch"
          />
          <el-select
            v-model="certStatusFilter"
            class="filter-select"
            clearable
            placeholder="认证状态"
          >
            <el-option
              v-for="item in STOP_CERT_STATUS_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <div class="filter-actions">
            <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
              查询
            </el-button>
            <el-button text native-type="button" @click="handleReset">重置</el-button>
          </div>
        </form>
      </NcCard>

      <el-alert
        v-if="loadError"
        class="load-error"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="load()">重新加载</el-button>
      </el-alert>

      <NcCard padding="none" class="list-card">
        <div v-loading="loading" class="list-area">
          <el-skeleton v-if="loading && rows.length === 0" :rows="5" animated />

          <template v-else-if="rows.length > 0">
            <el-table v-if="!isNarrow" :data="rows" row-key="id" class="preview-table">
              <el-table-column label="姓名 / 年龄" min-width="130">
                <template #default="{ row }">
                  <div class="primary-cell">
                    <strong>{{ row.name }}</strong>
                    <small>{{ row.gender || '—' }} · {{ formatAge(row.ageSnapshot) }}</small>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="身份证号" min-width="170">
                <template #default="{ row }">
                  <span class="mono">{{ row.idCard || '—' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="认证状态" min-width="120">
                <template #default="{ row }">
                  <ElderlyChip
                    :text="row.certStatusText || row.certStatus"
                    :tone="certStatusTone(row.certStatus)"
                  />
                </template>
              </el-table-column>
              <el-table-column label="凭证" min-width="120">
                <template #default="{ row }">
                  <div class="primary-cell">
                    <ElderlyChip
                      :text="row.evidenceStatusText || row.evidenceStatus"
                      :tone="evidenceStatusTone(row.evidenceStatus)"
                    />
                    <small>{{ row.evidenceCount }} / {{ row.expectedEvidenceCount }}</small>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="建议停发原因" min-width="150">
                <template #default="{ row }">
                  {{ row.suggestedStopReasonText || row.suggestedStopReason || '—' }}
                </template>
              </el-table-column>
              <el-table-column label="建议停发日期" min-width="120">
                <template #default="{ row }">{{ formatDate(row.dueStopDate) }}</template>
              </el-table-column>
              <el-table-column label="停发决定" min-width="110">
                <template #default="{ row }">
                  <ElderlyChip
                    :text="row.stopDecisionText || row.stopDecision"
                    :tone="stopDecisionTone(row.stopDecision)"
                  />
                </template>
              </el-table-column>
              <el-table-column label="操作" width="300" align="right">
                <template #default="{ row }">
                  <div class="row-actions">
                    <el-button
                      size="small"
                      type="warning"
                      :disabled="!row.applicationId || row.stopDecision === 'Approved'"
                      :title="!row.applicationId ? '名册未建档，无法停发' : ''"
                      @click="goStop(row as CertificationStopPreviewItem)"
                    >
                      办理停发
                    </el-button>
                    <el-button
                      size="small"
                      :disabled="row.stopDecision === 'Approved'"
                      @click="openDecision(row as CertificationStopPreviewItem, 'Deferred')"
                    >
                      暂缓
                    </el-button>
                    <el-button
                      size="small"
                      :disabled="row.stopDecision === 'Approved'"
                      @click="openDecision(row as CertificationStopPreviewItem, 'Excluded')"
                    >
                      排除
                    </el-button>
                    <el-button
                      v-if="row.stopDecision === 'Deferred' || row.stopDecision === 'Excluded'"
                      size="small"
                      @click="openDecision(row as CertificationStopPreviewItem, 'Pending')"
                    >
                      恢复待停发
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="preview-cards">
              <article v-for="row in rows" :key="row.id" class="preview-card">
                <header class="card-head">
                  <div>
                    <strong>{{ row.name }}</strong>
                    <small class="mono">{{ row.idCard || '—' }}</small>
                  </div>
                  <span class="card-index">{{ formatAge(row.ageSnapshot) }}</span>
                </header>
                <div class="card-chips">
                  <ElderlyChip
                    :text="row.certStatusText || row.certStatus"
                    :tone="certStatusTone(row.certStatus)"
                  />
                  <ElderlyChip
                    :text="row.evidenceStatusText || row.evidenceStatus"
                    :tone="evidenceStatusTone(row.evidenceStatus)"
                  />
                  <ElderlyChip
                    :text="row.stopDecisionText || row.stopDecision"
                    :tone="stopDecisionTone(row.stopDecision)"
                  />
                </div>
                <dl class="card-meta">
                  <div>
                    <dt>建议停发原因</dt>
                    <dd>{{ row.suggestedStopReasonText || row.suggestedStopReason || '—' }}</dd>
                  </div>
                  <div>
                    <dt>建议停发日期</dt>
                    <dd>{{ formatDate(row.dueStopDate) }}</dd>
                  </div>
                  <div>
                    <dt>凭证</dt>
                    <dd>{{ row.evidenceCount }} / {{ row.expectedEvidenceCount }}</dd>
                  </div>
                  <div>
                    <dt>名册建档</dt>
                    <dd>{{ row.applicationId ? `#${row.applicationId}` : '未建档' }}</dd>
                  </div>
                </dl>
                <div class="card-actions">
                  <el-button
                    size="small"
                    type="warning"
                    :disabled="!row.applicationId || row.stopDecision === 'Approved'"
                    @click="goStop(row)"
                  >
                    办理停发
                  </el-button>
                  <el-button
                    size="small"
                    :disabled="row.stopDecision === 'Approved'"
                    @click="openDecision(row, 'Deferred')"
                  >
                    暂缓
                  </el-button>
                  <el-button
                    size="small"
                    :disabled="row.stopDecision === 'Approved'"
                    @click="openDecision(row, 'Excluded')"
                  >
                    排除
                  </el-button>
                  <el-button
                    v-if="row.stopDecision === 'Deferred' || row.stopDecision === 'Excluded'"
                    size="small"
                    @click="openDecision(row, 'Pending')"
                  >
                    恢复待停发
                  </el-button>
                </div>
              </article>
            </div>
          </template>

          <div v-else-if="!loading" class="empty-wrap">
            <el-empty description="当前筛选条件下没有需要停发预览的人员" />
          </div>
        </div>

        <template v-if="totalCount > 0" #footer>
          <div class="pagination">
            <span class="pagination-note">
              第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
            </span>
            <el-pagination
              :current-page="page"
              :page-size="pageSize"
              :total="totalCount"
              :layout="isNarrow ? 'prev, pager, next' : 'prev, pager, next, jumper'"
              :pager-count="isNarrow ? 5 : 7"
              :small="isNarrow"
              background
              @current-change="handlePageChange"
            />
          </div>
        </template>
      </NcCard>
    </template>

    <el-dialog v-model="decisionVisible" :title="decisionTitle" width="min(460px, 92vw)">
      <p v-if="decisionRow" class="dialog-subject">
        {{ decisionRow.name }} · <span class="mono">{{ decisionRow.idCard }}</span>
      </p>
      <el-form label-position="top">
        <el-form-item label="原因备注" :required="decisionForm.decision === 'Deferred'">
          <el-input
            v-model="decisionForm.note"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="例如：已联系家属，12 月补认证"
          />
        </el-form-item>
        <el-form-item v-if="decisionForm.decision === 'Deferred'" label="跟进日期">
          <el-date-picker
            v-model="decisionForm.followUpDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择跟进日期"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeDecision">取消</el-button>
        <el-button
          type="primary"
          :loading="savingDecision"
          @click="submitDecision"
        >
          确认
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="stopVisible" title="办理停发" width="min(560px, 94vw)">
      <p v-if="stopRow" class="dialog-subject">
        {{ stopRow.name }} · <span class="mono">{{ stopRow.idCard }}</span>
        <span v-if="stopRow.hukouVillage"> · {{ stopRow.hukouVillage }}</span>
      </p>
      <el-alert
        v-if="stopRow && !stopRow.applicationId"
        type="warning"
        :closable="false"
        show-icon
        title="该人员名册尚未建档，无法直接停发；请先补建档。"
      />
      <el-form label-position="top">
        <el-form-item label="停发原因" required>
          <el-select v-model="stopForm.stopReason" placeholder="请选择停发原因">
            <el-option
              v-for="option in STOP_REASON_OPTIONS"
              :key="option.value"
              :value="option.value"
              :label="option.label"
            />
          </el-select>
        </el-form-item>

        <el-form-item v-if="stopForm.stopReason === 'OTHER'" label="其他原因说明" required>
          <el-input
            v-model="stopForm.otherReasonText"
            maxlength="200"
            show-word-limit
            placeholder="请填写停发原因"
          />
        </el-form-item>

        <el-form-item label="应停发日期">
          <el-date-picker
            v-model="stopForm.dueStopDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择应停发日期"
          />
        </el-form-item>

        <el-form-item v-if="stopForm.stopReason === 'DECEASED'" label="死亡日期" required>
          <el-date-picker
            v-model="stopForm.deathDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择死亡日期"
          />
        </el-form-item>

        <el-form-item label="是否追缴">
          <el-checkbox v-model="stopForm.isRecover">需要追缴</el-checkbox>
        </el-form-item>
        <template v-if="stopForm.isRecover">
          <el-form-item label="追缴起止月">
            <div class="month-range">
              <el-date-picker
                v-model="stopForm.recoverStartMonth"
                type="month"
                value-format="YYYY-MM"
                placeholder="起月"
              />
              <span class="range-sep">至</span>
              <el-date-picker
                v-model="stopForm.recoverEndMonth"
                type="month"
                value-format="YYYY-MM"
                placeholder="止月"
              />
            </div>
          </el-form-item>
          <el-form-item label="追缴金额（元）">
            <el-input-number
              v-model="stopForm.recoverAmount"
              :min="0"
              :precision="2"
              :controls="false"
            />
          </el-form-item>
        </template>

        <el-form-item label="备注">
          <el-input
            v-model="stopForm.remark"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="可填写无法联系、材料情况等说明"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeStop">取消</el-button>
        <el-button
          type="primary"
          :loading="savingStop"
          :disabled="!stopRow?.applicationId"
          @click="submitStop"
        >
          确认停发
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.stop-preview-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.campaign-card {
  overflow: hidden;
}

.campaign-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
}

.campaign-picker {
  display: flex;
  flex: 1 1 360px;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.bar-label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.campaign-select {
  width: 100%;
}

.campaign-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 18px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.filter-card,
.list-card {
  overflow: hidden;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 240px;
}

.filter-select {
  flex: 0 0 150px;
}

.filter-actions {
  display: flex;
  gap: 8px;
}

.list-area {
  padding: 0 16px 16px;
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.primary-cell small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.row-actions {
  display: inline-flex;
  gap: 6px;
}

.preview-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.preview-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-head > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.card-head strong {
  font-size: var(--nc-text-md);
}

.card-head .mono {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-index {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
}

.card-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.card-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.empty-wrap {
  padding: 30px 0;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.dialog-subject {
  margin: 0 0 12px;
  color: var(--nc-ink-2);
}

.mono {
  font-family: var(--nc-font-mono);
}

.load-error {
  margin: 0;
}

.month-range {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.range-sep {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

@media (max-width: 767px) {
  .campaign-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .filter-bar {
    flex-direction: column;
  }

  .filter-select {
    flex: 1 1 auto;
  }

  .filter-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .card-meta {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
