<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Download, Refresh, Upload } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import {
  downloadImportLog,
  downloadImportTemplate,
  getImportJobSummary,
  listImportTypes,
  previewImport,
  startImport,
} from '@/api/dataImport'
import { getErrorMessage } from '@/api/http'
import { saveBlob } from '@/views/photos/photoMeta'
import type {
  ImportJobSummary,
  ImportPreviewResult,
  ImportTypeOption,
} from '@/types/dataImport'
import type { DataCenterJob } from '@/types/dataCenter'
import JobProgressDialog from './components/JobProgressDialog.vue'

const types = ref<ImportTypeOption[]>([])
const typesLoading = ref(false)
const typesError = ref('')

const selectedType = ref('')
const familyFile = ref<File | null>(null)
const personFile = ref<File | null>(null)
const singleFiles = ref<File[]>([])

const year = ref<number | null>(null)
const rangeMode = ref(false)
const rangeStart = ref<number | null>(null)
const rangeEnd = ref<number | null>(null)
const clearBeforeImport = ref(false)

const previewing = ref(false)
const preview = ref<ImportPreviewResult | null>(null)
const previewError = ref('')

const starting = ref(false)
const jobVisible = ref(false)
const jobId = ref('')
const result = ref<ImportJobSummary | null>(null)
const logDownloading = ref(false)

const familyInput = ref<HTMLInputElement | null>(null)
const personInput = ref<HTMLInputElement | null>(null)
const singleInput = ref<HTMLInputElement | null>(null)

const currentType = computed(() => types.value.find((item) => item.code === selectedType.value) ?? null)
const isCombined = computed(() => currentType.value?.combined ?? false)
const hasFiles = computed(() =>
  isCombined.value ? Boolean(familyFile.value || personFile.value) : singleFiles.value.length > 0,
)
const canPreview = computed(() => Boolean(selectedType.value) && hasFiles.value && !previewing.value)
const canStart = computed(() => canPreview.value && !starting.value)

const clearHint = computed(() => {
  if (!clearBeforeImport.value) return ''
  if (currentType.value?.supportsScope) {
    return '将只清空本次导入涉及的年份段，其它年份数据保留。'
  }
  return '将清空该导入类型的整张表数据，请确认已备份。'
})

const yearHint = computed(() => {
  if (!currentType.value?.supportsScope) return ''
  if (rangeMode.value) {
    return '只允许年份落在区间内的数据；越界的整份文件会被拒绝，不写库。'
  }
  return '留空＝按文件内「数据年份」列或文件名解析；填写则强制使用该年份。'
})

function resetFiles(): void {
  familyFile.value = null
  personFile.value = null
  singleFiles.value = []
  preview.value = null
  previewError.value = ''
  result.value = null
  year.value = null
  rangeMode.value = false
  rangeStart.value = null
  rangeEnd.value = null
  clearBeforeImport.value = false
}

watch(selectedType, () => resetFiles())

async function loadTypes(): Promise<void> {
  typesLoading.value = true
  typesError.value = ''
  try {
    types.value = await listImportTypes()
    if (!selectedType.value && types.value.length > 0) {
      selectedType.value = types.value[0]?.code ?? ''
    }
  } catch (error) {
    typesError.value = getErrorMessage(error, '导入类型加载失败')
  } finally {
    typesLoading.value = false
  }
}

function pickFamily(): void {
  familyInput.value?.click()
}

function pickPerson(): void {
  personInput.value?.click()
}

function pickSingle(): void {
  singleInput.value?.click()
}

function onFamilyPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  familyFile.value = input.files?.[0] ?? null
  input.value = ''
  preview.value = null
}

function onPersonPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  personFile.value = input.files?.[0] ?? null
  input.value = ''
  preview.value = null
}

function onSinglePicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const picked = Array.from(input.files ?? [])
  input.value = ''
  const known = new Set(singleFiles.value.map((file) => file.name))
  singleFiles.value = [...singleFiles.value, ...picked.filter((file) => !known.has(file.name))]
  preview.value = null
}

function removeSingle(index: number): void {
  singleFiles.value = singleFiles.value.filter((_, i) => i !== index)
  preview.value = null
}

function fileSizeText(file: File): string {
  if (file.size < 1024) return `${file.size} B`
  if (file.size < 1024 * 1024) return `${(file.size / 1024).toFixed(1)} KB`
  return `${(file.size / 1024 / 1024).toFixed(1)} MB`
}

function validateYearScope(): boolean {
  if (!currentType.value?.supportsScope) return true
  if (rangeMode.value) {
    if (!rangeStart.value || !rangeEnd.value) {
      ElMessage.warning('按区间导入时请填写起始年与结束年')
      return false
    }
    if (rangeStart.value > rangeEnd.value) {
      ElMessage.warning('起始年不能大于结束年')
      return false
    }
  }
  return true
}

function buildInput() {
  return {
    importType: selectedType.value,
    familyFile: isCombined.value ? familyFile.value : null,
    personFile: isCombined.value ? personFile.value : null,
    files: isCombined.value ? [] : singleFiles.value,
    year: rangeMode.value ? null : year.value,
    rangeStart: rangeMode.value ? rangeStart.value : null,
    rangeEnd: rangeMode.value ? rangeEnd.value : null,
    clearBeforeImport: clearBeforeImport.value,
  }
}

async function handlePreview(): Promise<void> {
  if (!validateYearScope()) return
  if (isCombined.value && (!familyFile.value || !personFile.value)) {
    ElMessage.warning('组合导入请同时选择家庭文件和人员文件')
    return
  }

  previewing.value = true
  previewError.value = ''
  try {
    const data = await previewImport(buildInput())
    preview.value = data
    if (!data.success) {
      previewError.value = data.errorMessage || '预览失败'
    }
  } catch (error) {
    previewError.value = getErrorMessage(error, '预览失败')
    preview.value = null
  } finally {
    previewing.value = false
  }
}

async function handleStart(): Promise<void> {
  if (!validateYearScope()) return
  if (isCombined.value && (!familyFile.value || !personFile.value)) {
    ElMessage.warning('组合导入请同时选择家庭文件和人员文件')
    return
  }

  try {
    await ElMessageBox.confirm(
      clearBeforeImport.value
        ? '导入前将先清空数据，此操作不可撤销。确定开始导入吗？'
        : '确定开始导入吗？导入过程中请勿关闭页面。',
      '开始导入',
      { confirmButtonText: '开始导入', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  starting.value = true
  try {
    const started = await startImport(buildInput())
    jobId.value = started.jobId
    jobVisible.value = true
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '启动导入失败'))
  } finally {
    starting.value = false
  }
}

async function handleJobFinished(job: DataCenterJob): Promise<void> {
  if (job.kind !== 'import') return
  try {
    result.value = await getImportJobSummary(job.jobId)
  } catch {
    // 结果读取失败时仍保留任务日志。
  }
}

async function handleTemplate(): Promise<void> {
  if (!selectedType.value) return
  try {
    const blob = await downloadImportTemplate(selectedType.value)
    saveBlob(blob, `${currentType.value?.name ?? '导入'}模板.xlsx`)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '下载模板失败'))
  }
}

async function handleLog(): Promise<void> {
  if (!jobId.value) return
  logDownloading.value = true
  try {
    const blob = await downloadImportLog(jobId.value)
    saveBlob(blob, `导入日志_${jobId.value}.txt`)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '下载日志失败'))
  } finally {
    logDownloading.value = false
  }
}

function clearResult(): void {
  result.value = null
  preview.value = null
  previewError.value = ''
}

onMounted(loadTypes)
</script>

<template>
  <div class="nc-page import-page">
    <NcPageHeader
      title="数据导入"
      description="上传 Excel 家庭/人员文件，自动匹配后预览并导入。支持模板下载与错误日志导出。"
      kicker="数据中心 · 数据导入"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="typesLoading" @click="loadTypes">刷新类型</el-button>
      </template>
    </NcPageHeader>

    <NcCard title="导入类型选择">
      <p v-if="typesError" class="error-note">{{ typesError }}</p>
      <el-skeleton v-else-if="typesLoading" :rows="2" animated />
      <template v-else>
        <el-select v-model="selectedType" placeholder="选择导入类型" class="type-select">
          <el-option
            v-for="item in types"
            :key="item.code"
            :label="item.name"
            :value="item.code"
          />
        </el-select>

        <p v-if="currentType" class="hint">
          {{ isCombined
            ? '组合导入：家庭文件 + 人员文件，按文件名自动匹配。'
            : '单文件导入：可一次选择多个 Excel 文件。' }}
        </p>

        <div v-if="currentType?.supportsScope" class="year-scope">
          <div class="year-row">
            <span>导入年份</span>
            <el-input-number
              v-model="year"
              :min="2000"
              :max="2100"
              :disabled="rangeMode"
              controls-position="right"
              placeholder="留空＝按文件解析"
            />
            <el-checkbox v-model="rangeMode">按区间</el-checkbox>
          </div>
          <div v-if="rangeMode" class="year-row">
            <span>起始年</span>
            <el-input-number v-model="rangeStart" :min="2000" :max="2100" controls-position="right" />
            <span>结束年</span>
            <el-input-number v-model="rangeEnd" :min="2000" :max="2100" controls-position="right" />
          </div>
          <p class="hint">{{ yearHint }}</p>
        </div>
      </template>
    </NcCard>

    <NcCard title="文件选择">
      <div v-if="isCombined" class="file-grid">
        <div class="file-slot">
          <strong>家庭文件</strong>
          <small>{{ currentType?.familyPattern || '按文件名匹配' }}</small>
          <div class="file-actions">
            <el-button :icon="Upload" @click="pickFamily">选择文件</el-button>
            <span v-if="familyFile" class="file-name">{{ familyFile.name }}</span>
            <span v-else class="muted">未选择</span>
          </div>
          <input ref="familyInput" type="file" accept=".xlsx,.xls" class="hidden-input" @change="onFamilyPicked" />
        </div>

        <div class="file-slot">
          <strong>人员文件</strong>
          <small>{{ currentType?.personPattern || '按文件名匹配' }}</small>
          <div class="file-actions">
            <el-button :icon="Upload" @click="pickPerson">选择文件</el-button>
            <span v-if="personFile" class="file-name">{{ personFile.name }}</span>
            <span v-else class="muted">未选择</span>
          </div>
          <input ref="personInput" type="file" accept=".xlsx,.xls" class="hidden-input" @change="onPersonPicked" />
        </div>
      </div>

      <div v-else class="single-files">
        <div class="file-actions">
          <el-button :icon="Upload" @click="pickSingle">添加文件</el-button>
          <span class="muted">支持多个 .xlsx / .xls，单文件不超过 128MB</span>
        </div>
        <input ref="singleInput" type="file" accept=".xlsx,.xls" multiple class="hidden-input" @change="onSinglePicked" />
        <ul v-if="singleFiles.length > 0" class="file-list">
          <li v-for="(file, index) in singleFiles" :key="file.name">
            <span class="file-name">{{ file.name }}</span>
            <span class="muted">{{ fileSizeText(file) }}</span>
            <el-button link type="danger" @click="removeSingle(index)">移除</el-button>
          </li>
        </ul>
      </div>
    </NcCard>

    <NcCard title="导入操作">
      <div class="action-row">
        <el-checkbox v-model="clearBeforeImport">导入前清空表数据</el-checkbox>
        <el-button
          v-if="currentType?.hasTemplate"
          :icon="Download"
          @click="handleTemplate"
        >
          导出模板
        </el-button>
      </div>
      <p v-if="clearHint" class="danger-hint">{{ clearHint }}</p>

      <div class="action-buttons">
        <el-button :loading="previewing" :disabled="!canPreview" @click="handlePreview">
          预览
        </el-button>
        <el-button type="primary" :loading="starting" :disabled="!canStart" @click="handleStart">
          开始导入
        </el-button>
      </div>
    </NcCard>

    <NcCard v-if="preview || previewError" title="预览结果">
      <p v-if="previewError" class="error-note">{{ previewError }}</p>
      <template v-if="preview">
        <div class="preview-head">
          <span>家庭 {{ preview.familyTotalRows.toLocaleString() }} 行</span>
          <span>人员 {{ preview.personTotalRows.toLocaleString() }} 行</span>
          <span v-if="preview.matched?.familyFileName">
            匹配：{{ preview.matched.familyFileName }} / {{ preview.matched.personFileName }}
          </span>
        </div>

        <el-tabs>
          <el-tab-pane label="家庭预览">
            <el-table :data="preview.familyRows" size="small" max-height="280">
              <el-table-column
                v-for="column in preview.familyColumns.filter((item) => item.isMapped)"
                :key="column.sourceColumn"
                :prop="column.sourceColumn"
                :label="column.sourceColumn"
                min-width="120"
                show-overflow-tooltip
              />
            </el-table>
          </el-tab-pane>
          <el-tab-pane label="人员预览">
            <el-table :data="preview.personRows" size="small" max-height="280">
              <el-table-column
                v-for="column in preview.personColumns.filter((item) => item.isMapped)"
                :key="column.sourceColumn"
                :prop="column.sourceColumn"
                :label="column.sourceColumn"
                min-width="120"
                show-overflow-tooltip
              />
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </template>
    </NcCard>

    <NcCard v-if="result || jobId" title="导入结果">
      <template #actions>
        <el-button link :icon="Download" :loading="logDownloading" @click="handleLog">
          下载日志
        </el-button>
        <el-button link @click="clearResult">清空结果</el-button>
      </template>

      <div v-if="result" class="result-grid">
        <div class="result-tile"><span>家庭</span><strong>{{ result.familyCount }}</strong></div>
        <div class="result-tile"><span>成员</span><strong>{{ result.personCount }}</strong></div>
        <div class="result-tile"><span>关联</span><strong>{{ result.linkedCount }}</strong></div>
        <div class="result-tile"><span>未关联</span><strong>{{ result.unlinkedCount }}</strong></div>
        <div class="result-tile"><span>错误</span><strong>{{ result.errorCount }}</strong></div>
        <div class="result-tile"><span>警告</span><strong>{{ result.warningCount }}</strong></div>
      </div>
      <p v-if="result?.error" class="error-note">{{ result.error }}</p>

      <div v-if="result && result.unlinkedPersons.length > 0" class="unlinked">
        <strong>未关联人员（{{ result.unlinkedCount }}）</strong>
        <el-table :data="result.unlinkedPersons" size="small" max-height="240">
          <el-table-column prop="rowNumber" label="行号" width="80" />
          <el-table-column prop="name" label="姓名" width="120" />
          <el-table-column prop="idCard" label="身份证号" min-width="180" />
          <el-table-column prop="headIdCard" label="户主身份证" min-width="180" />
        </el-table>
      </div>
    </NcCard>

    <JobProgressDialog
      v-model="jobVisible"
      :job-id="jobId || null"
      title="数据导入"
      @finished="handleJobFinished"
    />
  </div>
</template>

<style scoped>
.import-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.type-select {
  max-width: 420px;
  width: 100%;
}

.hint {
  margin: 10px 0 0;
  color: var(--nc-muted);
  line-height: 1.7;
}

.year-scope {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.year-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}

.file-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.file-slot {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 14px 16px;
  border: 1px dashed var(--nc-line);
  border-radius: var(--nc-radius-sm);
}

.file-slot strong { color: var(--nc-ink); }
.file-slot small { color: var(--nc-muted); }

.file-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-top: 6px;
}

.file-name {
  color: var(--nc-ink);
  word-break: break-all;
}

.single-files {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.file-list {
  margin: 0;
  padding: 0;
  list-style: none;
  border-top: 1px solid var(--nc-line-faint);
}

.file-list li {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.action-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.action-buttons {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}

.danger-hint {
  margin: 10px 0 0;
  color: var(--nc-danger, #b42318);
  font-weight: 600;
  line-height: 1.7;
}

.preview-head {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
  margin-bottom: 10px;
  color: var(--nc-ink);
}

.result-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 10px;
}

.result-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.result-tile span { color: var(--nc-muted); font-size: var(--nc-text-xs); }
.result-tile strong { font-size: 20px; color: var(--nc-ink); }

.unlinked {
  margin-top: 14px;
}

.log-list {
  margin: 8px 0 0;
  padding-left: 18px;
  max-height: 180px;
  overflow: auto;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 1.8;
}

.hidden-input {
  display: none;
}

.muted { color: var(--nc-muted); font-size: var(--nc-text-xs); }

.error-note {
  margin: 0;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
}

@media (max-width: 1100px) {
  .result-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}

@media (max-width: 767px) {
  .file-grid { grid-template-columns: 1fr; }
  .result-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .action-buttons { flex-direction: column; }
}
</style>
