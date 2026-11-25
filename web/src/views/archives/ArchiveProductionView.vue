<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Document,
  InfoFilled,
  Monitor,
  Printer,
  Refresh,
  View,
} from '@element-plus/icons-vue'
import {
  ARCHIVE_PRINT_BUSINESS_TYPE,
  createPrintJob,
  deletePrintJob,
  listDocumentTemplates,
  listPrintJobs,
  retryPrintJob,
} from '@/api/archivePrint'
import type {
  DocumentTemplate,
  PrintJobListItem,
  PrintJobMode,
} from '@/api/archivePrint'
import { getApplication } from '@/api/applications'
import { getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import ArchiveApplicationPanel from './ArchiveApplicationPanel.vue'
import PrintJobPanel from './PrintJobPanel.vue'
import { maskIdCard } from '@/views/applications/format'
import type { ApplicationDetail, ApplicationListItem } from '@/types/application'
import type { PrinterBinding } from '@/types/api'

const route = useRoute()
const router = useRouter()

const POLL_INTERVAL = 4_000
const JOB_PAGE_SIZE = 10

const applicationPanelRef = ref<InstanceType<typeof ArchiveApplicationPanel> | null>(null)

/* ── 申请 ─────────────────────────────────────────────────────── */
const selectedApplication = ref<ApplicationListItem | null>(null)

/* ── 文书模板 ─────────────────────────────────────────────────── */
const templates = ref<DocumentTemplate[]>([])
const selectedTemplateIds = ref<number[]>([])
const templatesLoading = ref(false)
const templatesError = ref('')
let templatesInitialized = false

/* ── 打印机 ───────────────────────────────────────────────────── */
const printers = ref<PrinterBinding[]>([])
const selectedPrinterId = ref<number | null>(null)
const printersLoading = ref(false)
const printersError = ref('')

/* ── 推送参数 ─────────────────────────────────────────────────── */
const copies = ref<number | undefined>(1)
const isDuplex = ref(false)
const pushing = ref(false)
const pushMode = ref<PrintJobMode | null>(null)

/* ── 任务列表 ─────────────────────────────────────────────────── */
const jobs = ref<PrintJobListItem[]>([])
const jobsLoading = ref(false)
const jobsError = ref('')
const jobStatusFilter = ref('')
const jobPage = ref(1)
const jobPageSize = ref(JOB_PAGE_SIZE)
const jobTotalCount = ref(0)
const jobTotalPages = ref(1)
const actionJobId = ref<number | null>(null)
let jobsTicket = 0
let pollTimer: number | undefined

const selectedTemplates = computed(() =>
  templates.value.filter((template) => selectedTemplateIds.value.includes(template.id)),
)
const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === selectedPrinterId.value) ?? null,
)
const onlinePrinterCount = computed(
  () => printers.value.filter((printer) => printer.online).length,
)
const copiesCount = computed(() => {
  const value = Math.round(copies.value ?? 1)
  return Math.min(99, Math.max(1, value))
})
const allTemplatesSelected = computed(
  () =>
    templates.value.length > 0 && selectedTemplateIds.value.length === templates.value.length,
)
const canPreview = computed(
  () =>
    !pushing.value &&
    Boolean(selectedApplication.value) &&
    selectedTemplates.value.length > 0,
)
const canPrint = computed(() => canPreview.value && Boolean(selectedPrinter.value))

const jobStatusOptions = [
  { label: '全部状态', value: '' },
  { label: '待处理', value: 'Pending' },
  { label: '处理中', value: 'Processing' },
  { label: '已完成', value: 'Completed' },
  { label: '失败', value: 'Failed' },
]

/* ── 加载 ─────────────────────────────────────────────────────── */

async function loadTemplates(): Promise<void> {
  templatesLoading.value = true
  templatesError.value = ''
  try {
    const list = await listDocumentTemplates(ARCHIVE_PRINT_BUSINESS_TYPE)
    templates.value = list
    const available = new Set(list.map((template) => template.id))

    if (!templatesInitialized) {
      selectedTemplateIds.value = list.map((template) => template.id)
      templatesInitialized = true
    } else {
      // 刷新后保留用户已选项，移除后端已下架的模板。
      selectedTemplateIds.value = selectedTemplateIds.value.filter((id) => available.has(id))
    }
  } catch (error) {
    templates.value = []
    selectedTemplateIds.value = []
    templatesError.value = getErrorMessage(error, '文书模板加载失败')
  } finally {
    templatesLoading.value = false
  }
}

async function loadPrinters(): Promise<void> {
  printersLoading.value = true
  printersError.value = ''
  try {
    const list = await listPrinters()
    printers.value = list

    const current = list.find((printer) => printer.id === selectedPrinterId.value)
    if (current) return

    const preferred = list.find((printer) => printer.isDefault && printer.online)
      ?? list.find((printer) => printer.isDefault)
      ?? list.find((printer) => printer.online)
      ?? list[0]
    selectedPrinterId.value = preferred?.id ?? null
  } catch (error) {
    printers.value = []
    selectedPrinterId.value = null
    printersError.value = getErrorMessage(error, '打印机列表加载失败')
  } finally {
    printersLoading.value = false
  }
}

async function loadJobs(options: { silent?: boolean } = {}): Promise<void> {
  const ticket = ++jobsTicket
  if (!options.silent) {
    jobsLoading.value = true
    jobsError.value = ''
  }

  try {
    const response = await listPrintJobs({
      status: jobStatusFilter.value || undefined,
      pageIndex: jobPage.value,
      pageSize: jobPageSize.value,
    })
    if (ticket !== jobsTicket) return

    jobs.value = response.items
    jobPage.value = response.pageIndex || jobPage.value
    jobPageSize.value = response.pageSize || jobPageSize.value
    jobTotalCount.value = response.totalCount
    jobTotalPages.value = Math.max(response.totalPages, 1)
    jobsError.value = ''
  } catch (error) {
    if (ticket !== jobsTicket) return
    if (!options.silent) {
      jobsError.value = getErrorMessage(error, '打印任务加载失败')
    }
  } finally {
    if (ticket === jobsTicket) jobsLoading.value = false
  }
}

function refreshAll(): void {
  void applicationPanelRef.value?.reload()
  void loadTemplates()
  void loadPrinters()
  void loadJobs()
}

/* ── 申请选择 ─────────────────────────────────────────────────── */

function handleSelectApplication(item: ApplicationListItem): void {
  selectedApplication.value = item
}

function detailToListItem(detail: ApplicationDetail): ApplicationListItem {
  return {
    id: detail.id,
    applicationNo: detail.applicationNo,
    applicantName: detail.applicantName,
    applicantIdCardMasked: maskIdCard(detail.applicantIdCard),
    applicantPhone: detail.applicantPhone,
    status: detail.status,
    statusText: detail.statusText,
    currentStep: detail.currentStep,
    familySize: detail.familySize,
    classificationResult: detail.classificationResult,
    totalAnnualIncome: detail.totalAnnualIncome,
    perCapitaAnnualIncome: detail.perCapitaAnnualIncome,
    town: detail.town,
    village: detail.village,
    address: detail.address,
    createdAt: detail.createdAt,
    updatedAt: detail.updatedAt,
  }
}

async function loadPreselectedApplication(): Promise<void> {
  const raw = Number(route.query.applicationId)
  if (!Number.isFinite(raw) || raw <= 0) return

  try {
    const detail = await getApplication(raw)
    selectedApplication.value = detailToListItem(detail)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '指定申请加载失败'))
  }
}

/* ── 模板选择 ─────────────────────────────────────────────────── */

function selectAllTemplates(): void {
  selectedTemplateIds.value = templates.value.map((template) => template.id)
}

function clearTemplates(): void {
  selectedTemplateIds.value = []
}

function goTemplateManagement(): void {
  void router.push('/coming-soon/templates')
}

function goPrinters(): void {
  void router.push('/printers')
}

/* ── 推送 ─────────────────────────────────────────────────────── */

function validationMessage(mode: PrintJobMode): string | null {
  if (!selectedApplication.value) return '请先在左侧选择一条申请'
  if (selectedTemplates.value.length === 0) return '请至少选择一份文书'
  if (mode === 'print' && !selectedPrinter.value) return '请先选择打印机'
  return null
}

async function pushJobs(mode: PrintJobMode): Promise<void> {
  if (pushing.value) return

  const invalid = validationMessage(mode)
  if (invalid) {
    ElMessage.warning(invalid)
    return
  }

  const application = selectedApplication.value
  if (!application) return

  const count = selectedTemplates.value.length
  const printerName = selectedPrinter.value?.printerName

  if (mode === 'print') {
    const printer = selectedPrinter.value
    if (!printer) return
    const target = `${printer.deviceName} / ${printer.printerName}`
    try {
      await ElMessageBox.confirm(
        `将向「${target}」推送 ${count} 个打印任务，每份 ${copiesCount.value} 份、${
          isDuplex.value ? '双面' : '单面'
        }打印。确认开始打印？`,
        '确认打印',
        { confirmButtonText: '开始打印', cancelButtonText: '取消', type: 'warning' },
      )
    } catch {
      return
    }
  }

  pushing.value = true
  pushMode.value = mode
  let succeeded = 0
  let firstError = ''

  try {
    for (const template of selectedTemplates.value) {
      try {
        await createPrintJob({
          businessType: ARCHIVE_PRINT_BUSINESS_TYPE,
          businessId: application.id,
          templateId: template.id,
          templateName: template.name,
          printerName,
          copies: copiesCount.value,
          isDuplex: isDuplex.value,
          mode,
        })
        succeeded += 1
      } catch (error) {
        if (!firstError) firstError = getErrorMessage(error, '打印任务推送失败')
      }
    }
  } finally {
    pushing.value = false
    pushMode.value = null
  }

  if (succeeded > 0) {
    if (mode === 'preview') {
      ElMessage.success('已推送到 PC 端预览')
    } else {
      ElMessage.success(`已推送 ${succeeded} 个打印任务，请在 PC 端查看打印机`)
    }
    jobPage.value = 1
    void loadJobs({ silent: true })
  }

  if (firstError) {
    ElMessage.error(
      succeeded > 0
        ? `${succeeded} 个任务已推送，其余失败：${firstError}`
        : firstError,
    )
  }
}

/* ── 任务操作 ─────────────────────────────────────────────────── */

function handleStatusFilterChange(): void {
  jobPage.value = 1
  void loadJobs()
}

function handleJobPageChange(value: number): void {
  jobPage.value = value
  void loadJobs()
}

async function handleRetryJob(job: PrintJobListItem): Promise<void> {
  if (actionJobId.value !== null) return
  actionJobId.value = job.id
  try {
    await retryPrintJob(job.id)
    ElMessage.success('已重新加入打印队列')
    await loadJobs({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '重试失败'))
  } finally {
    actionJobId.value = null
  }
}

async function handleDeleteJob(job: PrintJobListItem): Promise<void> {
  if (actionJobId.value !== null) return

  try {
    await ElMessageBox.confirm(
      `确定删除任务「${job.templateName}」（${job.jobNo}）吗？删除后不可恢复。`,
      '删除打印任务',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  actionJobId.value = job.id
  try {
    await deletePrintJob(job.id)
    ElMessage.success('打印任务已删除')
    if (jobs.value.length === 1 && jobPage.value > 1) {
      jobPage.value -= 1
    }
    await loadJobs({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  } finally {
    actionJobId.value = null
  }
}

/* ── 生命周期 ─────────────────────────────────────────────────── */

onMounted(async () => {
  void loadTemplates()
  void loadPrinters()
  void loadJobs()
  void loadPreselectedApplication()

  pollTimer = window.setInterval(() => {
    if (document.visibilityState !== 'visible') return
    if (pushing.value || actionJobId.value !== null || jobsLoading.value) return
    void loadJobs({ silent: true })
  }, POLL_INTERVAL)
})

onBeforeUnmount(() => {
  if (pollTimer !== undefined) window.clearInterval(pollTimer)
})
</script>

<template>
  <div class="nc-page archive-production">
    <NcPageHeader
      title="档案制作"
      description="选择申请与文书，推送到 PC 端预览或打印"
      kicker="救助档案管理 / 档案制作"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="jobsLoading" @click="refreshAll">刷新</el-button>
        <el-button :icon="Printer" @click="goPrinters">打印机绑定</el-button>
      </template>
    </NcPageHeader>

    <div class="hint-bar">
      <el-icon><InfoFilled /></el-icon>
      <span>Web 端不生成 PDF，预览/打印由已绑定的 PC 打印代理执行。</span>
    </div>

    <div class="summary">
      <NcStat
        label="已选文书"
        :value="`${selectedTemplateIds.length} / ${templates.length}`"
        tone="seal"
        compact
      />
      <NcStat
        label="在线打印机"
        :value="`${onlinePrinterCount} / ${printers.length}`"
        tone="pine"
        compact
      />
      <NcStat label="打印任务" :value="jobTotalCount" tone="ink" compact />
    </div>

    <div class="production-grid">
      <!-- ── 左：申请选择 ── -->
      <NcCard title="1 · 选择申请" subtitle="按申请编号、姓名或身份证检索">
        <ArchiveApplicationPanel
          ref="applicationPanelRef"
          :selected="selectedApplication"
          @select="handleSelectApplication"
        />
      </NcCard>

      <!-- ── 中：文书模板 ── -->
      <NcCard title="2 · 选择文书" subtitle="默认全选，可取消本次不需要的文书">
        <div class="template-toolbar">
          <span class="template-total">
            共 {{ templates.length }} 份 · 已选 {{ selectedTemplateIds.length }} 份
          </span>
          <div class="template-toolbar__actions">
            <el-button
              link
              type="primary"
              size="small"
              :disabled="templates.length === 0 || allTemplatesSelected"
              @click="selectAllTemplates"
            >
              全选
            </el-button>
            <el-button
              link
              size="small"
              :disabled="selectedTemplateIds.length === 0"
              @click="clearTemplates"
            >
              清空
            </el-button>
          </div>
        </div>

        <el-alert
          v-if="templatesError"
          class="panel-error"
          type="error"
          :title="templatesError"
          :closable="false"
          show-icon
        >
          <el-button link type="primary" @click="loadTemplates">重新加载</el-button>
        </el-alert>

        <el-skeleton
          v-else-if="templatesLoading && templates.length === 0"
          :rows="5"
          animated
        />

        <el-checkbox-group
          v-else-if="templates.length > 0"
          v-model="selectedTemplateIds"
          class="template-list"
        >
          <el-checkbox
            v-for="template in templates"
            :key="template.id"
            :value="template.id"
            class="template-item"
          >
            <span class="template-content">
              <span class="template-name">{{ template.name }}</span>
              <span class="template-tags">
                <span v-if="template.docType" class="template-tag">
                  {{ template.docType }}
                </span>
                <span v-if="template.fileType" class="template-tag template-tag--file">
                  {{ template.fileType }}
                </span>
              </span>
            </span>
          </el-checkbox>
        </el-checkbox-group>

        <el-empty
          v-else-if="!templatesLoading && !templatesError"
          :image-size="70"
          description="暂无可用文书模板"
        >
          <el-button size="small" :icon="Document" @click="goTemplateManagement">
            前往模板管理
          </el-button>
          <p class="empty-tip">
            请先在文书模板管理中上传并标注 FamilyApplication 业务类型的模板。
          </p>
        </el-empty>
      </NcCard>

      <!-- ── 右：打印设置 ── -->
      <NcCard class="printer-panel" title="3 · 推送打印" subtitle="推送到已绑定的 PC 打印代理">
        <div class="printer-block">
          <el-skeleton v-if="printersLoading" :rows="2" animated />

          <el-alert
            v-else-if="printersError"
            type="error"
            :title="printersError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadPrinters">重新加载</el-button>
          </el-alert>

          <template v-else-if="printers.length > 0">
            <el-select
              v-model="selectedPrinterId"
              placeholder="选择打印机"
              filterable
              style="width: 100%"
            >
              <el-option
                v-for="printer in printers"
                :key="printer.id"
                :value="printer.id"
                :label="`${printer.deviceName} / ${printer.printerName}`"
              >
                <div class="printer-option">
                  <span class="printer-option__name">{{ printer.printerName }}</span>
                  <span class="printer-option__device">{{ printer.deviceName }}</span>
                  <span class="printer-option__state" :class="{ 'is-online': printer.online }">
                    {{ printer.online ? '在线' : '离线' }}
                  </span>
                </div>
              </el-option>
            </el-select>

            <div v-if="selectedPrinter" class="printer-detail">
              <span class="printer-detail__device">
                <el-icon><Monitor /></el-icon>
                {{ selectedPrinter.deviceName }}
              </span>
              <NcStatusDot
                :status="selectedPrinter.online ? 'online' : 'offline'"
                :label="selectedPrinter.online ? '在线' : '离线'"
              />
            </div>

            <p v-if="selectedPrinter && !selectedPrinter.online" class="printer-warn">
              打印机离线：任务会进入队列，等待该电脑的 Agent 上线后执行。
            </p>
          </template>

          <el-empty
            v-else-if="!printersLoading"
            :image-size="70"
            description="尚未绑定打印机"
          >
            <el-button size="small" :icon="Printer" @click="goPrinters">去绑定打印机</el-button>
            <p class="empty-tip">预览可不选打印机；实际打印需要先绑定 PC 端打印机。</p>
          </el-empty>
        </div>

        <div class="print-options">
          <div class="option-row">
            <span class="option-label">份数</span>
            <el-input-number v-model="copies" :min="1" :max="99" size="small" />
          </div>
          <div class="option-row">
            <span class="option-label">单双面</span>
            <el-radio-group v-model="isDuplex" size="small">
              <el-radio-button :value="false">单面</el-radio-button>
              <el-radio-button :value="true">双面</el-radio-button>
            </el-radio-group>
          </div>
        </div>

        <div class="print-actions">
          <el-button
            class="print-actions__button"
            :icon="View"
            :loading="pushing && pushMode === 'preview'"
            :disabled="!canPreview"
            @click="pushJobs('preview')"
          >
            预览
          </el-button>
          <el-button
            class="print-actions__button"
            type="primary"
            :icon="Printer"
            :loading="pushing && pushMode === 'print'"
            :disabled="!canPrint"
            @click="pushJobs('print')"
          >
            打印
          </el-button>
        </div>

        <p class="proxy-hint">
          <el-icon><InfoFilled /></el-icon>
          <span>Web 端不生成 PDF，预览/打印由已绑定的 PC 打印代理执行。</span>
        </p>
      </NcCard>
    </div>

    <!-- ── 下：打印任务 ── -->
    <NcCard
      title="打印任务"
      subtitle="每 4 秒自动刷新；预览任务同样会出现在任务列表中"
      class="jobs-card"
    >
      <template #actions>
        <el-select
          v-model="jobStatusFilter"
          size="small"
          placeholder="全部状态"
          style="width: 118px"
          @change="handleStatusFilterChange"
        >
          <el-option
            v-for="option in jobStatusOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
        <el-button size="small" :icon="Refresh" :loading="jobsLoading" @click="loadJobs()">
          刷新
        </el-button>
      </template>

      <PrintJobPanel
        :jobs="jobs"
        :loading="jobsLoading"
        :error="jobsError"
        :page="jobPage"
        :page-size="jobPageSize"
        :total-count="jobTotalCount"
        :total-pages="jobTotalPages"
        :busy-id="actionJobId"
        @refresh="loadJobs()"
        @retry="handleRetryJob"
        @remove="handleDeleteJob"
        @page-change="handleJobPageChange"
      />
    </NcCard>
  </div>
</template>

<style scoped>
.archive-production {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

/* ── 提示条 ───────────────────────────────────────────────────── */
.hint-bar {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 10px 14px;
  border: 1px solid rgba(150, 104, 28, 0.28);
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.hint-bar .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

/* ── 概要 ─────────────────────────────────────────────────────── */
.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

/* ── 三栏工作区 ───────────────────────────────────────────────── */
.production-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.02fr) minmax(0, 1.08fr) minmax(290px, 0.82fr);
  gap: 12px;
  align-items: start;
}

.production-grid > :deep(.nc-card) {
  min-width: 0;
}

/* ── 文书模板 ─────────────────────────────────────────────────── */
.template-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 9px;
}

.template-total {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.template-toolbar__actions {
  display: flex;
  flex: none;
  gap: 4px;
}

.template-toolbar__actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.template-list {
  display: flex;
  flex-direction: column;
  gap: 5px;
  max-height: 372px;
  padding-right: 2px;
  overflow-y: auto;
}

.template-item {
  display: flex;
  align-items: flex-start;
  width: 100%;
  height: auto;
  margin: 0;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-left: 2px solid transparent;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.template-item:hover {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-3);
}

.template-item.is-checked {
  border-color: var(--nc-seal);
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.template-item :deep(.el-checkbox__input) {
  margin-top: 1px;
}

.template-item :deep(.el-checkbox__label) {
  width: 100%;
  min-width: 0;
  padding-left: 8px;
  overflow: hidden;
}

.template-content {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.template-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.45;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.template-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.template-tag {
  display: inline-flex;
  align-items: center;
  padding: 0 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: 10px;
  line-height: 1.7;
  white-space: nowrap;
}

.template-tag--file {
  color: #31576e;
  border-color: rgba(49, 87, 110, 0.2);
  background-color: #e8f0f7;
}

.panel-error {
  margin: 0;
}

.empty-tip {
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.6;
}

/* ── 打印机 / 推送 ────────────────────────────────────────────── */
.printer-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.printer-option {
  display: flex;
  align-items: center;
  gap: 8px;
}

.printer-option__name {
  overflow: hidden;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.printer-option__device {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.printer-option__state {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: 11px;
}

.printer-option__state.is-online {
  color: var(--nc-pine);
}

.printer-detail {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 7px 9px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
}

.printer-detail__device {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.printer-detail__device .el-icon {
  flex: none;
  color: var(--nc-seal);
}

.printer-warn {
  padding: 6px 9px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: 11px;
  line-height: 1.6;
}

.print-options {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
  padding-top: 11px;
  border-top: 1px solid var(--nc-line-faint);
}

.option-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.option-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.print-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.print-actions__button {
  width: 100%;
  margin-left: 0;
}

.print-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.proxy-hint {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.65;
}

.proxy-hint .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

/* ── 任务 ─────────────────────────────────────────────────────── */
.jobs-card {
  margin-top: 2px;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1180px) {
  .production-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .printer-panel {
    grid-column: 1 / -1;
  }

  .template-list {
    max-height: 330px;
  }
}

@media (max-width: 1023px) {
  .production-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .printer-panel {
    grid-column: auto;
  }

  .template-list {
    max-height: none;
  }
}

@media (max-width: 767px) {
  .archive-production {
    gap: 10px;
  }

  .summary {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 7px;
  }

  .print-actions {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
