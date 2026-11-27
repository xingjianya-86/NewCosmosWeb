<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Edit, Refresh, Upload } from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  applyCertificationImport,
  listCertificationCampaigns,
  previewCertificationImport,
} from '@/api/elderlyCertification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  CertificationCampaign,
  CertificationImportApplyResponse,
  CertificationImportPreview,
  CertificationImportRow,
} from '@/types/elderlyCertification'
import ElderlyChip from './ElderlyChip.vue'
import {
  campaignOptionLabel,
  certStatusTone,
  evidenceStatusTone,
  formatFileSize,
} from './certificationDisplay'
import { formatDate } from './display'
import type { ChipTone } from './display'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const campaigns = ref<CertificationCampaign[]>([])
const campaignsLoading = ref(false)
const campaignsError = ref('')
const selectedCampaignId = ref<number | null>(null)

const excelFile = ref<File | null>(null)
const zipFile = ref<File | null>(null)
const excelInput = ref<HTMLInputElement | null>(null)
const zipInput = ref<HTMLInputElement | null>(null)

const previewing = ref(false)
const preview = ref<CertificationImportPreview | null>(null)
const previewError = ref('')

const allowAdverseOverwrite = ref(false)
const applying = ref(false)
const applyResult = ref<CertificationImportApplyResponse | null>(null)

const selectedCampaign = computed(
  () => campaigns.value.find((item) => item.id === selectedCampaignId.value) ?? null,
)

const anomalyRows = computed<CertificationImportRow[]>(() => {
  const rows = preview.value?.rows ?? []
  return rows.filter(
    (row) => row.matchStatus !== 'Matched' || Boolean(row.errorMessage?.trim()),
  )
})

const visibleAnomalies = computed(() => anomalyRows.value.slice(0, 200))

const canPreview = computed(
  () =>
    selectedCampaignId.value !== null &&
    Boolean(excelFile.value) &&
    Boolean(zipFile.value) &&
    !previewing.value,
)

const canApply = computed(() => Boolean(preview.value) && !applying.value)

function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : value.toLocaleString('zh-CN')
}

function matchStatusTone(status: string): ChipTone {
  if (status === 'Matched') return 'pine'
  if (status === 'Unknown' || status === 'NameMismatch') return 'amber'
  if (status === 'Duplicate' || status === 'Invalid' || status === 'Conflict') return 'seal'
  return 'ink'
}

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

function handleCampaignChange(): void {
  resetPreview()
}

/* ── 文件选择 ───────────────────────────────────────────────────── */

function pickExcel(): void {
  excelInput.value?.click()
}

function pickZip(): void {
  zipInput.value?.click()
}

function onExcelPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (file && !/\.xlsx?$/i.test(file.name)) {
    ElMessage.warning('请选择 .xlsx 或 .xls 文件')
    return
  }
  if (file && file.size > 20 * 1024 * 1024) {
    ElMessage.warning('Excel 文件不能超过 20MB')
    return
  }
  excelFile.value = file
  resetPreview()
}

function onZipPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (file && !/\.zip$/i.test(file.name)) {
    ElMessage.warning('请选择 .zip 文件')
    return
  }
  if (file && file.size > 200 * 1024 * 1024) {
    ElMessage.warning('ZIP 文件建议不超过 200MB，大批次请按村拆分')
    return
  }
  zipFile.value = file
  resetPreview()
}

function clearExcel(): void {
  excelFile.value = null
  resetPreview()
}

function clearZip(): void {
  zipFile.value = null
  resetPreview()
}

function resetPreview(): void {
  preview.value = null
  previewError.value = ''
  applyResult.value = null
}

function resetAll(): void {
  excelFile.value = null
  zipFile.value = null
  allowAdverseOverwrite.value = false
  resetPreview()
}

/* ── 预览 / 应用 ────────────────────────────────────────────────── */

async function handlePreview(): Promise<void> {
  const campaignId = selectedCampaignId.value
  const excel = excelFile.value
  const zip = zipFile.value

  if (campaignId === null) {
    ElMessage.warning('请先选择认证批次')
    return
  }
  if (!excel || !zip) {
    ElMessage.warning('请同时选择 Excel 名单和 ZIP 凭证包')
    return
  }

  previewing.value = true
  previewError.value = ''
  applyResult.value = null
  try {
    preview.value = await previewCertificationImport(campaignId, excel, zip)
    ElMessage.success('预览完成，请核对异常行后再应用')
  } catch (error) {
    preview.value = null
    previewError.value = getErrorMessage(error, '导入预览失败')
  } finally {
    previewing.value = false
  }
}

async function handleApply(): Promise<void> {
  const current = preview.value
  if (!current) return

  try {
    await ElMessageBox.confirm(
      allowAdverseOverwrite.value
        ? `将应用预览批次 ${current.batchNo}，允许不利结果覆盖现有认证结果。`
        : `将应用预览批次 ${current.batchNo}，现有认证结果不会被不利结果覆盖。`,
      '确认应用导入',
      { type: 'warning', confirmButtonText: '应用', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  applying.value = true
  try {
    applyResult.value = await applyCertificationImport({
      previewId: current.previewId,
      allowAdverseOverwrite: allowAdverseOverwrite.value,
      rowNumbers: null,
    })
    ElMessage.success('导入已应用')
  } catch (error) {
    if (error instanceof ApiError) {
      if (error.code === 'PREVIEW_EXPIRED') {
        resetPreview()
        ElMessage.error('预览已过期，请重新选择文件并上传预览')
        return
      }
      if (error.code === 'IMPORT_FILE_CHANGED') {
        resetPreview()
        ElMessage.error('文件在预览后发生变化，请重新上传预览')
        return
      }
      if (error.code === 'CONFLICT') {
        ElMessage.error('名单状态已变化，请重新预览并确认后再应用')
        return
      }
    }
    ElMessage.error(getErrorMessage(error, '应用导入失败'))
  } finally {
    applying.value = false
  }
}

function goBack(): void {
  void router.push({
    path: '/elderly/certifications',
    query: selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

function goInfoChanges(): void {
  void router.push({
    path: '/elderly/certifications/info-changes',
    query: selectedCampaignId.value === null ? {} : { campaignId: String(selectedCampaignId.value) },
  })
}

onMounted(() => {
  void loadCampaigns()
})
</script>

<template>
  <div class="nc-page certification-import-page">
    <NcPageHeader
      title="年度认证批量导入"
      description="Excel 名单 + ZIP 凭证包两段式导入 · 先预览再应用，避免整批误写"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回认证名单</el-button>
        <el-button :icon="Refresh" :loading="campaignsLoading" @click="loadCampaigns()">
          刷新批次
        </el-button>
        <el-button :icon="Edit" @click="goInfoChanges">变更审批</el-button>
      </template>
    </NcPageHeader>

    <NcCard class="campaign-card">
      <div class="campaign-bar">
        <div class="campaign-picker">
          <span class="bar-label">目标批次</span>
          <el-select
            v-model="selectedCampaignId"
            class="campaign-select"
            placeholder="请选择认证批次"
            :loading="campaignsLoading"
            :disabled="campaigns.length === 0"
            @change="handleCampaignChange"
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
          <span>
            认证期间：{{ formatDate(selectedCampaign.periodStart) }} 至
            {{ formatDate(selectedCampaign.periodEnd) }}
          </span>
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

    <NcCard title="文件选择" subtitle="Excel 不超过 20MB；ZIP 单包建议不超过 200MB">
      <div class="file-grid">
        <div class="file-slot">
          <div class="file-slot__head">
            <strong>Excel 名单</strong>
            <span>.xlsx / .xls</span>
          </div>
          <div class="file-actions">
            <el-button :icon="Upload" @click="pickExcel">选择文件</el-button>
            <el-button v-if="excelFile" link type="danger" @click="clearExcel">清除</el-button>
          </div>
          <p v-if="excelFile" class="file-info">
            <span class="file-name">{{ excelFile.name }}</span>
            <span class="muted">{{ formatFileSize(excelFile.size) }}</span>
          </p>
          <p v-else class="muted">未选择</p>
          <input
            ref="excelInput"
            type="file"
            accept=".xlsx,.xls"
            class="hidden-input"
            @change="onExcelPicked"
          />
        </div>

        <div class="file-slot">
          <div class="file-slot__head">
            <strong>ZIP 凭证包</strong>
            <span>.zip</span>
          </div>
          <div class="file-actions">
            <el-button :icon="Upload" @click="pickZip">选择文件</el-button>
            <el-button v-if="zipFile" link type="danger" @click="clearZip">清除</el-button>
          </div>
          <p v-if="zipFile" class="file-info">
            <span class="file-name">{{ zipFile.name }}</span>
            <span class="muted">{{ formatFileSize(zipFile.size) }}</span>
          </p>
          <p v-else class="muted">未选择</p>
          <input
            ref="zipInput"
            type="file"
            accept=".zip"
            class="hidden-input"
            @change="onZipPicked"
          />
        </div>
      </div>

      <el-alert
        class="session-hint"
        type="info"
        :closable="false"
        show-icon
        title="大批次可按村拆分为多个 ZIP，但当前一次预览只能选择一个 ZIP；多包追加会话需等待后端补充接口。"
      />

      <div class="preview-actions">
        <el-button
          type="primary"
          :icon="Upload"
          :loading="previewing"
          :disabled="!canPreview"
          @click="handlePreview"
        >
          上传并预览
        </el-button>
        <el-button v-if="preview || excelFile || zipFile" @click="resetAll">重新开始</el-button>
      </div>
    </NcCard>

    <el-alert
      v-if="previewError"
      class="load-error"
      type="error"
      :title="previewError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :loading="previewing" @click="handlePreview">
        重新预览
      </el-button>
    </el-alert>

    <template v-if="preview">
      <NcCard title="预览统计" :subtitle="preview.summary || `预览批次 ${preview.batchNo}`">
        <div class="preview-stats">
          <NcStat label="总行数" :value="formatCount(preview.totalRows)" tone="ink" compact />
          <NcStat label="已匹配" :value="formatCount(preview.matchedRows)" tone="pine" compact />
          <NcStat label="未匹配" :value="formatCount(preview.unknownRows)" tone="amber" compact />
          <NcStat label="重复行" :value="formatCount(preview.duplicateRows)" tone="amber" compact />
          <NcStat label="姓名不一致" :value="formatCount(preview.nameMismatchRows)" tone="amber" compact />
          <NcStat label="无效行" :value="formatCount(preview.invalidRows)" tone="seal" compact />
          <NcStat label="状态冲突" :value="formatCount(preview.conflictRows)" tone="seal" compact />
          <NcStat label="凭证匹配" :value="`${preview.evidenceMatched} / ${preview.evidenceTotal}`" tone="pine" compact />
          <NcStat label="凭证缺失" :value="formatCount(preview.evidenceMissing)" tone="seal" compact />
          <NcStat label="凭证未匹配" :value="formatCount(preview.evidenceUnmatched)" tone="amber" compact />
        </div>

        <el-alert
          v-if="preview.errors.length > 0"
          class="preview-alert"
          type="error"
          :closable="false"
          show-icon
          title="预览错误"
        >
          <ul class="message-list">
            <li v-for="(item, index) in preview.errors" :key="`error-${index}`">{{ item }}</li>
          </ul>
        </el-alert>

        <el-alert
          v-if="preview.warnings.length > 0"
          class="preview-alert"
          type="warning"
          :closable="false"
          show-icon
          title="预览提醒"
        >
          <ul class="message-list">
            <li v-for="(item, index) in preview.warnings" :key="`warning-${index}`">{{ item }}</li>
          </ul>
        </el-alert>
      </NcCard>

      <NcCard
        title="异常行"
        :subtitle="`共 ${anomalyRows.length} 行未匹配或存在错误，最多显示前 200 行`"
      >
        <template v-if="visibleAnomalies.length > 0">
          <el-table v-if="!isNarrow" :data="visibleAnomalies" row-key="rowNo" class="anomaly-table">
            <el-table-column prop="rowNo" label="行号" width="72" />
            <el-table-column label="姓名 / 身份证" min-width="180">
              <template #default="{ row }">
                <div class="primary-cell">
                  <strong>{{ row.name || '—' }}</strong>
                  <small class="mono">{{ row.idCard || '—' }}</small>
                </div>
              </template>
            </el-table-column>
            <el-table-column prop="village" label="村居" min-width="110" />
            <el-table-column label="匹配状态" min-width="120">
              <template #default="{ row }">
                <ElderlyChip
                  :text="row.matchStatusText || row.matchStatus"
                  :tone="matchStatusTone(row.matchStatus)"
                />
              </template>
            </el-table-column>
            <el-table-column label="认证状态" min-width="110">
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
                  <small>{{ row.matchedEvidenceCount }} / {{ row.expectedEvidenceCount }}</small>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="错误信息" min-width="220">
              <template #default="{ row }">
                <span class="error-text">{{ row.errorMessage || '—' }}</span>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="anomaly-cards">
            <article v-for="row in visibleAnomalies" :key="row.rowNo" class="anomaly-card">
              <header class="card-head">
                <div>
                  <strong>{{ row.name || '—' }}</strong>
                  <small class="mono">{{ row.idCard || '—' }}</small>
                </div>
                <span class="card-index">第 {{ row.rowNo }} 行</span>
              </header>
              <div class="card-chips">
                <ElderlyChip
                  :text="row.matchStatusText || row.matchStatus"
                  :tone="matchStatusTone(row.matchStatus)"
                />
                <ElderlyChip
                  :text="row.certStatusText || row.certStatus"
                  :tone="certStatusTone(row.certStatus)"
                />
                <ElderlyChip
                  :text="row.evidenceStatusText || row.evidenceStatus"
                  :tone="evidenceStatusTone(row.evidenceStatus)"
                />
              </div>
              <dl class="card-meta">
                <div><dt>村居</dt><dd>{{ row.village || '—' }}</dd></div>
                <div><dt>凭证匹配</dt><dd>{{ row.matchedEvidenceCount }} / {{ row.expectedEvidenceCount }}</dd></div>
              </dl>
              <p v-if="row.errorMessage" class="error-text">{{ row.errorMessage }}</p>
            </article>
          </div>
        </template>
        <el-empty v-else :image-size="64" description="没有异常行" />
      </NcCard>

      <NcCard
        v-if="preview.unmatchedEntries.length > 0"
        title="未匹配的 ZIP 条目"
        :subtitle="`共 ${preview.unmatchedEntries.length} 个文件未对应到名单`"
      >
        <el-table :data="preview.unmatchedEntries" row-key="entryName" class="entry-table">
          <el-table-column prop="entryName" label="ZIP 内路径" min-width="220" />
          <el-table-column prop="fileName" label="文件名" min-width="160" />
          <el-table-column prop="extension" label="扩展名" width="90" />
          <el-table-column label="大小" width="110" align="right">
            <template #default="{ row }">{{ formatFileSize(row.length) }}</template>
          </el-table-column>
        </el-table>
      </NcCard>

      <NcCard title="应用导入">
        <div class="apply-bar">
          <el-checkbox v-model="allowAdverseOverwrite">
            允许覆盖不利结果（用本次导入的拒绝/不通过/死亡等结果覆盖现有结果）
          </el-checkbox>
          <span class="muted">
            默认只补充与升级结果；预览过期或文件变化需重新上传。
          </span>
        </div>
        <div class="apply-actions">
          <el-button type="primary" :loading="applying" :disabled="!canApply" @click="handleApply">
            应用全部可应用行
          </el-button>
        </div>

        <div v-if="applyResult" class="apply-result">
          <el-alert
            type="success"
            :closable="false"
            show-icon
            :title="applyResult.summary || `批次 ${applyResult.batchNo} 已应用`"
          />
          <div class="result-stats">
            <NcStat label="应用行数" :value="formatCount(applyResult.appliedRows)" tone="pine" compact />
            <NcStat label="跳过行数" :value="formatCount(applyResult.skippedRows)" tone="amber" compact />
            <NcStat label="失败行数" :value="formatCount(applyResult.errorRows)" tone="seal" compact />
            <NcStat label="新增凭证" :value="formatCount(applyResult.evidenceInserted)" tone="pine" compact />
            <NcStat label="跳过凭证" :value="formatCount(applyResult.evidenceSkipped)" tone="amber" compact />
            <NcStat label="未匹配凭证" :value="formatCount(applyResult.evidenceUnmatched)" tone="seal" compact />
            <NcStat label="生成变更单" :value="formatCount(applyResult.infoChangesCreated)" tone="pine" compact />
            <NcStat label="变更跳过" :value="formatCount(applyResult.infoChangesSkipped)" tone="amber" compact />
            <NcStat label="变更失败" :value="formatCount(applyResult.infoChangeErrors)" tone="seal" compact />
          </div>
          <div class="apply-actions">
            <el-button type="primary" @click="goBack">查看认证名单</el-button>
            <el-button @click="resetAll">继续导入下一批</el-button>
          </div>
        </div>
      </NcCard>
    </template>

    <el-empty
      v-else-if="!previewing"
      :image-size="80"
      description="选择 Excel 名单与 ZIP 凭证包后上传预览"
    />
  </div>
</template>

<style scoped>
.certification-import-page {
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

.file-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.file-slot {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.file-slot__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
}

.file-slot__head span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.file-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.file-info {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  margin: 0;
  font-size: var(--nc-text-sm);
}

.file-name {
  overflow-wrap: anywhere;
}

.muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.hidden-input {
  display: none;
}

.session-hint {
  margin-top: 12px;
}

.preview-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.load-error {
  margin: 0;
}

.preview-stats,
.result-stats {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 8px;
}

.preview-alert {
  margin-top: 12px;
}

.message-list {
  margin: 0;
  padding-left: 18px;
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

.error-text {
  color: var(--nc-seal);
  font-size: var(--nc-text-sm);
  overflow-wrap: anywhere;
}

.anomaly-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.anomaly-card {
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

.apply-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
}

.apply-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.apply-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 1279px) {
  .preview-stats,
  .result-stats {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .campaign-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .file-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .preview-stats,
  .result-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .preview-actions :deep(.el-button) {
    flex: 1;
  }
}

@media (max-width: 420px) {
  .preview-stats,
  .result-stats {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
