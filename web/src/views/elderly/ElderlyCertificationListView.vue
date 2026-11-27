<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Document, Edit, Plus, Refresh, Search, Upload, View } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  createCertificationCampaign,
  generateCertificationRoster,
  getCertificationEvidenceContent,
  getCertificationStats,
  getCertificationTask,
  listCertificationCampaigns,
  listCertificationEvidence,
  listCertificationTasks,
  updateCertificationTaskResult,
} from '@/api/elderlyCertification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  CertificationCampaign,
  CertificationEvidence,
  CertificationStats,
  CertificationTaskDetail,
  CertificationTaskListItem,
} from '@/types/elderlyCertification'
import ElderlyChip from './ElderlyChip.vue'
import {
  CERT_STATUS_OPTIONS,
  EVIDENCE_STATUS_OPTIONS,
  VERIFY_METHOD_OPTIONS,
  campaignOptionLabel,
  campaignStatusTone,
  certStatusTone,
  evidenceStatusTone,
  stopDecisionTone,
} from './certificationDisplay'
import { formatAge, formatDate, formatMoney } from './display'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

/* ── 批次 ───────────────────────────────────────────────────────── */

const campaigns = ref<CertificationCampaign[]>([])
const campaignsLoading = ref(false)
const campaignsError = ref('')
const selectedCampaignId = ref<number | null>(null)

const selectedCampaign = computed(
  () => campaigns.value.find((item) => item.id === selectedCampaignId.value) ?? null,
)

const newCampaignVisible = ref(false)
const submittingCampaign = ref(false)
const newCampaign = reactive({
  year: new Date().getFullYear(),
  round: 1,
  name: '',
  periodStart: '',
  periodEnd: '',
  requireEvidence: true,
})

/* ── 统计 / 名单 ────────────────────────────────────────────────── */

const stats = ref<CertificationStats | null>(null)
const statsLoading = ref(false)

const keyword = ref('')
const certStatusFilter = ref('')
const evidenceStatusFilter = ref('')
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const totalPages = ref(0)
const tasks = ref<CertificationTaskListItem[]>([])
const listLoading = ref(false)
const listError = ref('')
const rosterLoading = ref(false)

let listTicket = 0

const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const hasCampaign = computed(() => selectedCampaignId.value !== null)

function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : value.toLocaleString('zh-CN')
}

/* ── 详情抽屉 ───────────────────────────────────────────────────── */

const detailVisible = ref(false)
const detailLoading = ref(false)
const detailError = ref('')
const detailTask = ref<CertificationTaskDetail | null>(null)
const evidence = ref<CertificationEvidence[]>([])
const evidenceUrls = ref<Record<number, string>>({})
const savingResult = ref(false)
const resultForm = reactive({
  certStatus: 'Pending',
  verifyMethod: 'Face',
  verifiedAt: '',
  verifiedBy: '',
  resultNote: '',
  deathDate: '',
})

/* ── 批次加载 ───────────────────────────────────────────────────── */

async function loadCampaigns(): Promise<void> {
  campaignsLoading.value = true
  campaignsError.value = ''
  try {
    const list = await listCertificationCampaigns()
    campaigns.value = list
    const requested = Number(route.query.campaignId)
    const requestedExists = Number.isFinite(requested) && list.some((item) => item.id === requested)
    const currentExists = list.some((item) => item.id === selectedCampaignId.value)

    if (requestedExists) {
      selectedCampaignId.value = requested
    } else if (!currentExists) {
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

/* ── 统计 / 名单加载 ────────────────────────────────────────────── */

async function loadStats(): Promise<void> {
  const campaignId = selectedCampaignId.value
  if (campaignId === null) {
    stats.value = null
    return
  }
  statsLoading.value = true
  try {
    stats.value = await getCertificationStats(campaignId)
  } catch {
    stats.value = null
  } finally {
    statsLoading.value = false
  }
}

async function loadTasks(): Promise<void> {
  const campaignId = selectedCampaignId.value
  const ticket = ++listTicket
  if (campaignId === null) {
    tasks.value = []
    totalCount.value = 0
    totalPages.value = 0
    return
  }

  listLoading.value = true
  listError.value = ''
  try {
    const response = await listCertificationTasks(campaignId, {
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      certStatus: certStatusFilter.value || undefined,
      evidenceStatus: evidenceStatusFilter.value || undefined,
    })
    if (ticket !== listTicket) return
    tasks.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== listTicket) return
    tasks.value = []
    totalCount.value = 0
    totalPages.value = 0
    listError.value = getErrorMessage(error, '认证名单加载失败')
  } finally {
    if (ticket === listTicket) listLoading.value = false
  }
}

async function loadAll(): Promise<void> {
  await Promise.all([loadStats(), loadTasks()])
}

function selectCampaign(value: number | string): void {
  const id = Number(value)
  selectedCampaignId.value = id
  page.value = 1
  keyword.value = ''
  certStatusFilter.value = ''
  evidenceStatusFilter.value = ''
  void router.replace({ query: { ...route.query, campaignId: String(id) } })
  void loadAll()
}

function handleSearch(): void {
  page.value = 1
  void loadTasks()
}

function handleReset(): void {
  keyword.value = ''
  certStatusFilter.value = ''
  evidenceStatusFilter.value = ''
  page.value = 1
  void loadTasks()
}

function handlePageChange(value: number): void {
  page.value = value
  void loadTasks()
}

/* ── 新建批次 / 生成名单 ────────────────────────────────────────── */

function openCreateCampaign(): void {
  const year = new Date().getFullYear()
  Object.assign(newCampaign, {
    year,
    round: 1,
    name: `${year}年度高龄津贴领取资格认证`,
    periodStart: `${year}-01-01`,
    periodEnd: `${year}-12-31`,
    requireEvidence: true,
  })
  newCampaignVisible.value = true
}

async function submitCampaign(): Promise<void> {
  if (!newCampaign.name.trim()) {
    ElMessage.warning('请填写批次名称')
    return
  }
  if (!newCampaign.periodStart || !newCampaign.periodEnd) {
    ElMessage.warning('请选择认证起止日期')
    return
  }
  if (newCampaign.periodStart > newCampaign.periodEnd) {
    ElMessage.warning('认证开始日期不能晚于结束日期')
    return
  }

  submittingCampaign.value = true
  try {
    const created = await createCertificationCampaign({
      year: newCampaign.year,
      round: newCampaign.round,
      name: newCampaign.name.trim(),
      periodStart: newCampaign.periodStart,
      periodEnd: newCampaign.periodEnd,
      requireEvidence: newCampaign.requireEvidence,
    })
    newCampaignVisible.value = false
    ElMessage.success('认证批次已创建')
    await loadCampaigns()
    selectedCampaignId.value = created.id
    page.value = 1
    void router.replace({ query: { ...route.query, campaignId: String(created.id) } })
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '新建认证批次失败'))
  } finally {
    submittingCampaign.value = false
  }
}

async function handleGenerateRoster(): Promise<void> {
  const campaign = selectedCampaign.value
  if (!campaign) {
    ElMessage.warning('请先选择认证批次')
    return
  }

  try {
    await ElMessageBox.confirm(
      `将按当前数据范围补充 ${campaign.displayName || campaign.name} 的新增应认证人员，已存在人员不会重复生成。`,
      '生成应认证名单',
      { type: 'warning', confirmButtonText: '生成', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  rosterLoading.value = true
  try {
    const result = await generateCertificationRoster(campaign.id)
    ElMessage.success(`名单已生成，新增 ${result.added} 人`)
    await loadCampaigns()
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '生成名单失败'))
  } finally {
    rosterLoading.value = false
  }
}

function goImport(): void {
  void router.push({
    path: '/elderly/certifications/import',
    query: selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

function goStopPreview(): void {
  void router.push({
    path: '/elderly/certifications/stop-preview',
    query:
      selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

function goReports(): void {
  void router.push({
    path: '/elderly/certifications/reports',
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

/** 合同第 6 节：任务行/详情进入信息变更登记。 */
function goInfoChangeForm(task: CertificationTaskListItem): void {
  void router.push({
    path: '/elderly/certifications/info-changes/new',
    query: { taskId: String(task.id) },
  })
}

/* ── 详情 / 凭证 / 结果 ─────────────────────────────────────────── */

function clearEvidenceUrls(): void {
  for (const url of Object.values(evidenceUrls.value)) URL.revokeObjectURL(url)
  evidenceUrls.value = {}
}

function openTask(task: CertificationTaskListItem): void {
  detailVisible.value = true
  void loadDetail(task.id)
}

async function loadDetail(taskId: number): Promise<void> {
  detailLoading.value = true
  detailError.value = ''
  clearEvidenceUrls()
  detailTask.value = null
  evidence.value = []

  try {
    const [task, evidenceList] = await Promise.all([
      getCertificationTask(taskId),
      listCertificationEvidence(taskId).catch(() => []),
    ])
    detailTask.value = task
    evidence.value = evidenceList
    Object.assign(resultForm, {
      certStatus: task.certStatus,
      verifyMethod: task.verifyMethod || 'Face',
      verifiedAt: task.verifiedAt ?? '',
      verifiedBy: task.verifiedBy ?? '',
      resultNote: task.resultNote ?? '',
      deathDate: task.deathDate ?? '',
    })

    const contents = await Promise.allSettled(
      evidenceList.map((item) => getCertificationEvidenceContent(item.id)),
    )
    const urls: Record<number, string> = {}
    contents.forEach((result, index) => {
      const item = evidenceList[index]
      if (result.status === 'fulfilled' && item) {
        urls[item.id] = URL.createObjectURL(result.value)
      }
    })
    evidenceUrls.value = urls
  } catch (error) {
    detailError.value = getErrorMessage(error, '认证详情加载失败')
  } finally {
    detailLoading.value = false
  }
}

async function saveResult(): Promise<void> {
  const task = detailTask.value
  if (!task) return
  if (resultForm.certStatus === 'Deceased' && !resultForm.deathDate) {
    ElMessage.warning('认证结果为「已去世」时请填写死亡日期')
    return
  }

  savingResult.value = true
  try {
    await updateCertificationTaskResult(task.id, {
      certStatus: resultForm.certStatus,
      verifyMethod: resultForm.verifyMethod,
      verifiedAt: resultForm.verifiedAt || null,
      verifiedBy: resultForm.verifiedBy.trim(),
      resultNote: resultForm.resultNote.trim(),
      deathDate:
        resultForm.certStatus === 'Deceased' ? resultForm.deathDate || null : null,
    })
    ElMessage.success('认证结果已保存')
    await loadAll()
    await loadDetail(task.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存认证结果失败'))
  } finally {
    savingResult.value = false
  }
}

onMounted(async () => {
  await loadCampaigns()
  await loadAll()
})

onBeforeUnmount(() => {
  clearEvidenceUrls()
})
</script>

<template>
  <div class="nc-page certification-list-page">
    <NcPageHeader
      title="年度身份认证"
      description="高龄津贴领取资格年度认证 · 生成应认证名单 · 导入核查结果 · 停发预览"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="listLoading || statsLoading" @click="loadAll()">
          刷新
        </el-button>
      </template>
    </NcPageHeader>

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
            >
              <div class="campaign-option">
                <span>{{ item.displayName || item.name }}</span>
                <span class="campaign-option__meta">
                  {{ item.year }} 年 · 第 {{ item.round }} 轮
                  <ElderlyChip :text="item.statusText" :tone="campaignStatusTone(item.status)" />
                </span>
              </div>
            </el-option>
          </el-select>
        </div>

        <div class="campaign-actions">
          <el-button :icon="Plus" @click="openCreateCampaign">新建批次</el-button>
          <el-button
            type="primary"
            :loading="rosterLoading"
            :disabled="!hasCampaign"
            @click="handleGenerateRoster"
          >
            生成名单
          </el-button>
          <el-button :icon="Upload" :disabled="!hasCampaign" @click="goImport">
            批量导入
          </el-button>
          <el-button :disabled="!hasCampaign" @click="goStopPreview">停发预览</el-button>
          <el-button :icon="Document" :disabled="!hasCampaign" @click="goReports">
            报表
          </el-button>
          <el-button :icon="Edit" @click="goInfoChanges">变更审批</el-button>
        </div>
      </div>

      <div v-if="selectedCampaign" class="campaign-meta">
        <span>
          认证期间：{{ formatDate(selectedCampaign.periodStart) }} 至
          {{ formatDate(selectedCampaign.periodEnd) }}
        </span>
        <span>有效期至：{{ formatDate(selectedCampaign.validUntil) }}</span>
        <span>名单生成：{{ formatDate(selectedCampaign.rosterGeneratedAt) }}</span>
        <span>凭证要求：{{ selectedCampaign.requireEvidence ? '必须上传' : '非必须' }}</span>
      </div>
    </NcCard>

    <el-alert
      v-if="campaignsError"
      class="load-error"
      type="error"
      :title="campaignsError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadCampaigns()">重新加载</el-button>
    </el-alert>

    <el-empty
      v-if="!campaignsLoading && campaigns.length === 0"
      description="暂无认证批次，请先新建批次并生成应认证名单"
    >
      <el-button type="primary" :icon="Plus" @click="openCreateCampaign">新建批次</el-button>
    </el-empty>

    <template v-if="hasCampaign">
      <div class="stat-grid">
        <NcStat label="应认证（人）" :value="formatCount(stats?.total)" tone="ink" />
        <NcStat label="待核查（人）" :value="formatCount(stats?.pending)" tone="amber" />
        <NcStat label="已通过（人）" :value="formatCount(stats?.verified)" tone="pine" />
        <NcStat label="凭证异常（人）" :value="formatCount(stats?.evidenceIssue)" tone="seal" />
        <NcStat label="停发候选（人）" :value="formatCount(stats?.stopCandidate)" tone="seal" />
        <NcStat
          label="完成率"
          :value="stats ? `${stats.completionRate}%` : '—'"
          tone="pine"
        />
      </div>

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
              v-for="item in CERT_STATUS_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <el-select
            v-model="evidenceStatusFilter"
            class="filter-select"
            clearable
            placeholder="凭证状态"
          >
            <el-option
              v-for="item in EVIDENCE_STATUS_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
          <div class="filter-actions">
            <el-button type="primary" :icon="Search" native-type="submit" :loading="listLoading">
              查询
            </el-button>
            <el-button text native-type="button" @click="handleReset">重置</el-button>
          </div>
        </form>
      </NcCard>

      <el-alert
        v-if="listError"
        class="load-error"
        type="error"
        :title="listError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadTasks()">重新加载</el-button>
      </el-alert>

      <NcCard padding="none" class="list-card">
        <div v-loading="listLoading" class="list-area" :class="{ 'is-cards': isNarrow }">
          <el-skeleton v-if="listLoading && tasks.length === 0" :rows="5" animated />

          <template v-else-if="tasks.length > 0">
            <el-table v-if="!isNarrow" :data="tasks" row-key="id" class="task-table">
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
              <el-table-column label="类别 / 月金额" min-width="170">
                <template #default="{ row }">
                  <div class="primary-cell">
                    <span>{{ row.categoryText || '—' }}</span>
                    <small>￥{{ formatMoney(row.issueAmount) }}</small>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="认证状态" min-width="140">
                <template #default="{ row }">
                  <div class="chip-cell">
                    <ElderlyChip
                      :text="row.certStatusText || row.certStatus"
                      :tone="certStatusTone(row.certStatus)"
                    />
                    <small>{{ row.verifyMethodText || '—' }}</small>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="凭证" min-width="130">
                <template #default="{ row }">
                  <div class="chip-cell">
                    <ElderlyChip
                      :text="row.evidenceStatusText || row.evidenceStatus"
                      :tone="evidenceStatusTone(row.evidenceStatus)"
                    />
                    <small>{{ row.evidenceCount }} / {{ row.expectedEvidenceCount }}</small>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="停发决定" min-width="110">
                <template #default="{ row }">
                  <ElderlyChip
                    :text="row.stopDecisionText || row.stopDecision"
                    :tone="stopDecisionTone(row.stopDecision)"
                  />
                </template>
              </el-table-column>
              <el-table-column label="操作" width="214" align="right">
                <template #default="{ row }">
                  <div class="row-actions">
                    <el-button
                      size="small"
                      :icon="View"
                      @click="openTask(row as CertificationTaskListItem)"
                    >
                      查看
                    </el-button>
                    <el-button
                      size="small"
                      :icon="Edit"
                      @click="goInfoChangeForm(row as CertificationTaskListItem)"
                    >
                      信息变更
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="task-cards">
              <article v-for="(row, index) in tasks" :key="row.id" class="task-card">
                <header class="card-head">
                  <div>
                    <strong>{{ row.name }}</strong>
                    <span class="mono">{{ row.idCard || '—' }}</span>
                  </div>
                  <span class="card-index">#{{ (page - 1) * pageSize + index + 1 }}</span>
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
                    <dt>年龄 / 性别</dt>
                    <dd>{{ formatAge(row.ageSnapshot) }} · {{ row.gender || '—' }}</dd>
                  </div>
                  <div>
                    <dt>类别 / 月金额</dt>
                    <dd>{{ row.categoryText || '—' }} · ￥{{ formatMoney(row.issueAmount) }}</dd>
                  </div>
                  <div>
                    <dt>认证方式</dt>
                    <dd>{{ row.verifyMethodText || '—' }}</dd>
                  </div>
                  <div>
                    <dt>凭证</dt>
                    <dd>{{ row.evidenceCount }} / {{ row.expectedEvidenceCount }}</dd>
                  </div>
                </dl>
                <div class="card-actions">
                  <el-button size="small" :icon="View" @click="openTask(row)">查看详情</el-button>
                  <el-button size="small" :icon="Edit" @click="goInfoChangeForm(row)">
                    信息变更
                  </el-button>
                </div>
              </article>
            </div>
          </template>

          <div v-else-if="!listLoading" class="empty-wrap">
            <el-empty description="暂无认证名单，可点击「生成名单」补充应认证人员" />
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

    <!-- 新建批次 -->
    <el-dialog v-model="newCampaignVisible" title="新建认证批次" width="min(520px, 92vw)">
      <el-form label-position="top" class="campaign-form">
        <div class="form-row">
          <el-form-item label="年度">
            <el-input-number v-model="newCampaign.year" :min="2000" :max="2100" controls-position="right" />
          </el-form-item>
          <el-form-item label="轮次">
            <el-input-number v-model="newCampaign.round" :min="1" :max="99" controls-position="right" />
          </el-form-item>
        </div>
        <el-form-item label="批次名称">
          <el-input v-model="newCampaign.name" maxlength="100" show-word-limit />
        </el-form-item>
        <div class="form-row">
          <el-form-item label="认证开始">
            <el-date-picker
              v-model="newCampaign.periodStart"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="开始日期"
            />
          </el-form-item>
          <el-form-item label="认证结束">
            <el-date-picker
              v-model="newCampaign.periodEnd"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="结束日期"
            />
          </el-form-item>
        </div>
        <el-form-item label="凭证要求">
          <el-switch v-model="newCampaign.requireEvidence" active-text="必须上传凭证" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="newCampaignVisible = false">取消</el-button>
        <el-button type="primary" :loading="submittingCampaign" @click="submitCampaign">
          创建批次
        </el-button>
      </template>
    </el-dialog>

    <!-- 任务详情 / 凭证 / 结果 -->
    <el-drawer
      v-model="detailVisible"
      title="认证详情"
      :size="isNarrow ? '100%' : '600px'"
      @closed="clearEvidenceUrls"
    >
      <div v-loading="detailLoading" class="detail-body">
        <el-alert
          v-if="detailError"
          type="error"
          :title="detailError"
          :closable="false"
          show-icon
        />

        <template v-if="detailTask">
          <section class="detail-section">
            <h3>{{ detailTask.name }}</h3>
            <dl class="detail-grid">
              <div><dt>身份证号</dt><dd class="mono">{{ detailTask.idCard || '—' }}</dd></div>
              <div><dt>性别 / 年龄</dt><dd>{{ detailTask.gender || '—' }} · {{ formatAge(detailTask.ageSnapshot) }}</dd></div>
              <div><dt>认证状态</dt><dd>{{ detailTask.certStatusText }}</dd></div>
              <div><dt>认证方式</dt><dd>{{ detailTask.verifyMethodText || '—' }}</dd></div>
              <div><dt>认证时间</dt><dd>{{ formatDate(detailTask.verifiedAt) }}</dd></div>
              <div><dt>认证人</dt><dd>{{ detailTask.verifiedBy || '—' }}</dd></div>
              <div><dt>类别</dt><dd>{{ detailTask.categoryText || '—' }}</dd></div>
              <div><dt>月金额</dt><dd>￥{{ formatMoney(detailTask.issueAmount) }}</dd></div>
              <div class="detail-wide"><dt>户籍地址</dt><dd>{{ detailTask.hukouAddress || '—' }}</dd></div>
              <div class="detail-wide"><dt>家庭地址</dt><dd>{{ detailTask.familyAddress || '—' }}</dd></div>
              <div class="detail-wide"><dt>联系电话</dt><dd>{{ detailTask.phone || '—' }}</dd></div>
              <div class="detail-wide"><dt>结果备注</dt><dd>{{ detailTask.resultNote || '—' }}</dd></div>
            </dl>
          </section>

          <section class="detail-section">
            <h3>凭证（{{ evidence.length }}）</h3>
            <div v-if="evidence.length > 0" class="evidence-grid">
              <figure v-for="item in evidence" :key="item.id" class="evidence-item">
                <img
                  v-if="evidenceUrls[item.id]"
                  :src="evidenceUrls[item.id]"
                  :alt="item.displayName || item.originalName"
                  loading="lazy"
                />
                <div v-else class="evidence-placeholder">无预览</div>
                <figcaption>
                  <span>{{ item.evidenceTypeText || item.evidenceType }}</span>
                  <small class="mono">{{ item.displayName || item.originalName }}</small>
                </figcaption>
              </figure>
            </div>
            <el-empty v-else :image-size="64" description="暂无凭证" />
          </section>

          <section class="detail-section">
            <h3>核查结果</h3>
            <el-form label-position="top" class="result-form">
              <div class="form-row">
                <el-form-item label="认证状态">
                  <el-select v-model="resultForm.certStatus">
                    <el-option
                      v-for="item in CERT_STATUS_OPTIONS"
                      :key="item.value"
                      :label="item.label"
                      :value="item.value"
                    />
                  </el-select>
                </el-form-item>
                <el-form-item label="认证方式">
                  <el-select v-model="resultForm.verifyMethod">
                    <el-option
                      v-for="item in VERIFY_METHOD_OPTIONS"
                      :key="item.value"
                      :label="item.label"
                      :value="item.value"
                    />
                  </el-select>
                </el-form-item>
              </div>
              <div class="form-row">
                <el-form-item label="认证日期">
                  <el-date-picker
                    v-model="resultForm.verifiedAt"
                    type="date"
                    value-format="YYYY-MM-DD"
                    placeholder="选择日期"
                  />
                </el-form-item>
                <el-form-item v-if="resultForm.certStatus === 'Deceased'" label="死亡日期">
                  <el-date-picker
                    v-model="resultForm.deathDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    placeholder="选择日期"
                  />
                </el-form-item>
              </div>
              <el-form-item label="认证人">
                <el-input v-model="resultForm.verifiedBy" maxlength="50" />
              </el-form-item>
              <el-form-item label="结果备注">
                <el-input
                  v-model="resultForm.resultNote"
                  type="textarea"
                  :rows="2"
                  maxlength="500"
                  show-word-limit
                />
              </el-form-item>
            </el-form>
            <div class="detail-actions">
              <el-button type="primary" :loading="savingResult" @click="saveResult">
                保存结果
              </el-button>
            </div>
          </section>
        </template>
      </div>

      <template #footer>
        <div class="detail-footer">
          <el-button @click="detailVisible = false">关闭</el-button>
          <el-button
            v-if="detailTask"
            type="primary"
            :icon="Edit"
            @click="goInfoChangeForm(detailTask)"
          >
            信息变更
          </el-button>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.certification-list-page {
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
  flex: 1 1 340px;
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

.campaign-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.campaign-option__meta {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.campaign-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.campaign-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 22px;
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 10px;
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
  flex: 0 0 140px;
}

.filter-actions {
  display: flex;
  gap: 8px;
}

.list-area {
  padding: 0 16px 16px;
}

.primary-cell,
.chip-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.primary-cell small,
.chip-cell small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.task-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.task-card {
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
  justify-content: flex-end;
  gap: 8px;
}

.row-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}

.detail-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
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

.mono {
  font-family: var(--nc-font-mono);
}

.load-error {
  margin: 0;
}

.form-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.form-row :deep(.el-date-editor),
.form-row :deep(.el-select) {
  width: 100%;
}

.detail-body {
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding-bottom: 24px;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.detail-section h3 {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin: 0;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-2);
}

.detail-wide {
  grid-column: 1 / -1;
}

.detail-grid dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.detail-grid dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  word-break: break-all;
}

.evidence-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 10px;
}

.evidence-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin: 0;
  padding: 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.evidence-item img,
.evidence-placeholder {
  width: 100%;
  aspect-ratio: 4 / 3;
  border-radius: var(--nc-radius-xs);
  background: var(--nc-paper-3);
  object-fit: cover;
}

.evidence-placeholder {
  display: grid;
  place-items: center;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.evidence-item figcaption {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.evidence-item figcaption span {
  font-size: var(--nc-text-sm);
}

.evidence-item figcaption small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.result-form :deep(.el-select),
.result-form :deep(.el-date-editor) {
  width: 100%;
}

.detail-actions {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 1279px) {
  .stat-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .campaign-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .campaign-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .campaign-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .stat-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
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

  .form-row {
    grid-template-columns: minmax(0, 1fr);
  }

  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 420px) {
  .stat-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
