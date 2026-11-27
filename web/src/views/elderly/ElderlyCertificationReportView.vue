<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Download, Edit, Printer, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  downloadCertificationReport,
  downloadCertificationReportPdf,
  listCertificationCampaigns,
} from '@/api/elderlyCertification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { CertificationCampaign } from '@/types/elderlyCertification'
import { CERTIFICATION_REPORT_FORMS } from '@/types/elderlyCertificationReport'
import type { CertificationReportFormKey } from '@/types/elderlyCertificationReport'
import ElderlyChip from './ElderlyChip.vue'
import { campaignOptionLabel, campaignStatusTone } from './certificationDisplay'
import { formatDate } from './display'

const route = useRoute()
const router = useRouter()

/* ── 批次 ───────────────────────────────────────────────────────── */

const campaigns = ref<CertificationCampaign[]>([])
const campaignsLoading = ref(false)
const campaignsError = ref('')
const selectedCampaignId = ref<number | null>(null)

const selectedCampaign = computed(
  () => campaigns.value.find((item) => item.id === selectedCampaignId.value) ?? null,
)

/** 文件名前缀：优先合同约定的 campaignNo；缺失时用 id 兜底。 */
const campaignNo = computed(() => {
  const campaign = selectedCampaign.value
  if (!campaign) return ''
  return campaign.campaignNo.trim() || `campaign-${campaign.id}`
})

/* ── 报表勾选 / 导出 ────────────────────────────────────────────── */

const selectedKeys = ref<CertificationReportFormKey[]>(
  CERTIFICATION_REPORT_FORMS.map((form) => form.key),
)
const hasSelection = computed(() => selectedKeys.value.length > 0)
const exporting = ref(false)
const printing = ref(false)
const exportProgress = ref('')

const canPrint = computed(
  () => selectedCampaign.value !== null && selectedKeys.value.length === 1,
)

const exportNote = computed(() => {
  if (exportProgress.value) return exportProgress.value
  if (!selectedCampaign.value) return '请先选择认证批次'
  return `已选 ${selectedKeys.value.length} 个报表；打印请只勾选 1 个`
})

/* ── 批次加载 ───────────────────────────────────────────────────── */

async function loadCampaigns(): Promise<void> {
  campaignsLoading.value = true
  campaignsError.value = ''
  try {
    const list = await listCertificationCampaigns()
    campaigns.value = list
    const requested = Number(route.query.campaignId)
    const requestedExists =
      Number.isFinite(requested) && list.some((item) => item.id === requested)
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

function selectCampaign(value: number | string): void {
  const id = Number(value)
  selectedCampaignId.value = id
  void router.replace({ query: { ...route.query, campaignId: String(id) } })
}

function goInfoChanges(): void {
  void router.push({
    path: '/elderly/certifications/info-changes',
    query:
      selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

/* ── 勾选辅助 ───────────────────────────────────────────────────── */

function selectAllForms(): void {
  selectedKeys.value = CERTIFICATION_REPORT_FORMS.map((form) => form.key)
}

function clearForms(): void {
  selectedKeys.value = []
}

/* ── 下载 ───────────────────────────────────────────────────────── */

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

/** 合同 5：yyyyMMddHHmmss（本地时间）。 */
function timestamp(date = new Date()): string {
  return (
    `${date.getFullYear()}${pad2(date.getMonth() + 1)}${pad2(date.getDate())}` +
    `${pad2(date.getHours())}${pad2(date.getMinutes())}${pad2(date.getSeconds())}`
  )
}

/** 合同 5：{campaignNo}_{formKey}_{yyyyMMddHHmmss}.{xlsx|pdf} */
function reportFileName(
  batchNo: string,
  formKey: CertificationReportFormKey,
  stamp: string,
  extension: 'xlsx' | 'pdf',
): string {
  const prefix = batchNo.replace(/[\\/:*?"<>|]/g, '_')
  return `${prefix}_${formKey}_${stamp}.${extension}`
}

function saveBlob(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 2000)
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

async function exportReports(format: 'xlsx' | 'pdf'): Promise<void> {
  const campaign = selectedCampaign.value
  if (!campaign) {
    ElMessage.warning('请先选择认证批次')
    return
  }

  const keys = CERTIFICATION_REPORT_FORMS.map((form) => form.key).filter((key) =>
    selectedKeys.value.includes(key),
  )
  if (keys.length === 0) {
    ElMessage.warning('请至少勾选一个报表')
    return
  }

  exporting.value = true
  const stamp = timestamp()
  const batchNo = campaignNo.value
  const failed: string[] = []
  let succeeded = 0
  const formatName = format === 'pdf' ? 'PDF' : 'Excel'

  try {
    for (const [index, key] of keys.entries()) {
      const form = CERTIFICATION_REPORT_FORMS.find((item) => item.key === key)
      const label = form?.name ?? key
      exportProgress.value = `正在导出${formatName}「${label}」（${index + 1}/${keys.length}）…`
      try {
        const blob =
          format === 'pdf'
            ? await downloadCertificationReportPdf(campaign.id, key)
            : await downloadCertificationReport(campaign.id, key)
        saveBlob(blob, reportFileName(batchNo, key, stamp, format))
        succeeded += 1
        // 连续触发多个文件下载时给浏览器留出间隔。
        if (index < keys.length - 1) await delay(250)
      } catch (error) {
        failed.push(`${label}：${getErrorMessage(error, '下载失败')}`)
      }
    }
  } finally {
    exporting.value = false
    exportProgress.value = ''
  }

  if (failed.length === 0) {
    ElMessage.success(`已导出 ${succeeded} 个 ${formatName} 报表`)
  } else if (succeeded > 0) {
    ElMessage.warning(`已导出 ${succeeded} 个，${failed.length} 个失败（${failed.join('；')}）`)
  } else {
    ElMessage.error(`导出失败（${failed.join('；')}）`)
  }
}

function handleExport(): void {
  void exportReports('xlsx')
}

function handleExportPdf(): void {
  void exportReports('pdf')
}

/**
 * 打印：先同步打开新窗口（避免异步后 window.open 被拦截），拿到 PDF Blob 后
 * 把新窗口指向 blob URL，由浏览器内置 PDF 查看器执行打印。
 */
async function handlePrintPdf(): Promise<void> {
  const campaign = selectedCampaign.value
  if (!campaign) {
    ElMessage.warning('请先选择认证批次')
    return
  }
  if (selectedKeys.value.length !== 1) {
    ElMessage.warning('打印请只勾选 1 个报表；多份请先导出 PDF')
    return
  }

  const key = selectedKeys.value[0]
  const printWindow = window.open('', '_blank')
  if (!printWindow) {
    ElMessage.warning('浏览器阻止了新窗口，请允许弹出窗口后重试')
    return
  }

  printing.value = true
  try {
    const blob = await downloadCertificationReportPdf(campaign.id, key)
    const url = URL.createObjectURL(
      new Blob([blob], { type: 'application/pdf' }),
    )
    printWindow.location.href = url
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    ElMessage.success('PDF 已在新窗口打开，请使用浏览器打印')
  } catch (error) {
    printWindow.close()
    ElMessage.error(getErrorMessage(error, '打印 PDF 失败'))
  } finally {
    printing.value = false
  }
}

onMounted(() => {
  void loadCampaigns()
})
</script>

<template>
  <div class="nc-page certification-report-page">
    <NcPageHeader
      title="年度认证报表"
      description="按认证批次导出 Excel/PDF 花名册、汇总表与未认证名单；数据范围由服务端按当前用户权限过滤。"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/elderly/certifications')">
          返回认证
        </el-button>
        <el-button :icon="Refresh" :loading="campaignsLoading" @click="loadCampaigns()">
          刷新批次
        </el-button>
        <el-button :icon="Edit" @click="goInfoChanges">变更审批</el-button>
      </template>
    </NcPageHeader>

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
      description="暂无认证批次，请先在年度身份认证页新建批次并生成应认证名单"
    >
      <el-button type="primary" @click="router.push('/elderly/certifications')">
        前往年度身份认证
      </el-button>
    </el-empty>

    <template v-else>
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

          <div v-if="selectedCampaign" class="campaign-badge">
            <span class="bar-label">批次编号</span>
            <span class="mono">{{ selectedCampaign.campaignNo || '—' }}</span>
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

      <NcCard
        title="选择报表"
        subtitle="勾选后逐个下载 Excel 或 PDF；文件名格式 {批次编号}_{报表key}_{yyyyMMddHHmmss}.{xlsx|pdf}"
      >
        <div class="form-toolbar">
          <el-button size="small" plain @click="selectAllForms">全选</el-button>
          <el-button size="small" plain @click="clearForms">清空</el-button>
          <span class="form-selected">
            已选 {{ selectedKeys.length }} / {{ CERTIFICATION_REPORT_FORMS.length }}
          </span>
        </div>

        <el-checkbox-group v-model="selectedKeys" class="report-list">
          <el-checkbox
            v-for="form in CERTIFICATION_REPORT_FORMS"
            :key="form.key"
            :value="form.key"
            class="report-item"
          >
            <span class="report-item__body">
              <span class="report-item__head">
                <span class="report-item__name">{{ form.name }}</span>
                <span class="report-item__key mono">{{ form.key }}</span>
              </span>
              <span class="report-item__desc">{{ form.description }}</span>
            </span>
          </el-checkbox>
        </el-checkbox-group>

        <el-alert
          class="export-hint"
          type="info"
          :closable="false"
          show-icon
          title="身份证号按导出权限返回；PDF 由服务端直出（内置中文字体），打印请在浏览器 PDF 查看器中完成。"
        />

        <template #footer>
          <div class="export-bar">
            <span class="export-note" :class="{ 'is-active': exporting }">
              {{ exportNote }}
            </span>
            <el-button
              type="primary"
              :icon="Download"
              :loading="exporting"
              :disabled="!selectedCampaign || !hasSelection"
              @click="handleExport"
            >
              导出 Excel
            </el-button>
            <el-button
              :icon="Download"
              :loading="exporting"
              :disabled="!selectedCampaign || !hasSelection"
              @click="handleExportPdf"
            >
              导出 PDF
            </el-button>
            <el-button
              :icon="Printer"
              :loading="printing"
              :disabled="!canPrint || exporting"
              @click="handlePrintPdf"
            >
              打印 PDF
            </el-button>
          </div>
        </template>
      </NcCard>
    </template>
  </div>
</template>

<style scoped>
.certification-report-page {
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

.campaign-badge {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-bottom: 2px;
}

.campaign-badge .mono {
  font-size: var(--nc-text-sm);
  color: var(--nc-ink-2);
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

.form-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.form-selected {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.report-list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 10px;
}

.report-list :deep(.el-checkbox) {
  display: flex;
  align-items: flex-start;
  width: 100%;
  height: auto;
  margin: 0;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  white-space: normal;
}

.report-list :deep(.el-checkbox.is-checked) {
  border-color: var(--nc-seal);
  background: var(--nc-paper-2);
}

.report-list :deep(.el-checkbox__label) {
  flex: 1 1 auto;
  min-width: 0;
  padding-left: 10px;
}

.report-item__body {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.report-item__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.report-item__name {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 600;
}

.report-item__key {
  padding: 1px 6px;
  border-radius: var(--nc-radius-xs);
  background: var(--nc-paper-3);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.report-item__desc {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.export-hint {
  margin-top: 14px;
}

.export-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.export-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.export-note.is-active {
  color: var(--nc-ink-2);
}

.mono {
  font-family: var(--nc-font-mono);
}

.load-error {
  margin: 0;
}

@media (max-width: 767px) {
  .campaign-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .report-list {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-selected {
    margin-left: 0;
  }

  .export-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .export-bar :deep(.el-button) {
    width: 100%;
  }
}
</style>
