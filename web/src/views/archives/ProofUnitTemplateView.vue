<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Edit,
  Plus,
  Refresh,
  UploadFilled,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  createArchiveProofUnit,
  deleteArchiveProofUnit,
  getArchiveProofTemplates,
  updateArchiveProofUnit,
} from '@/api/archive'
import type { ArchiveProofUnitInput, ArchiveProofUnitTemplate } from '@/types/archive'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { formatDateTimeText } from './archiveDisplay'

const router = useRouter()

const units = ref<ArchiveProofUnitTemplate[]>([])
const loading = ref(false)
const loadError = ref('')

const formVisible = ref(false)
const isCreateMode = ref(true)
const editingId = ref<number | null>(null)
const unitName = ref('')
const selectedFile = ref<File | null>(null)
const fileType = ref('Word')
const configJson = ref('')
const saving = ref(false)
const deletingId = ref<number | null>(null)
const fileInputRef = ref<HTMLInputElement | null>(null)
const formRef = ref<HTMLElement | null>(null)

const sortedUnits = computed(() =>
  [...units.value].sort((left, right) =>
    left.unitName.localeCompare(right.unitName, 'zh-CN'),
  ),
)

const formTitle = computed(() =>
  isCreateMode.value ? '新增单位模板' : '编辑单位模板',
)

const fileName = computed(() => selectedFile.value?.name ?? '')

function fileTypeLabel(value: string | null | undefined): string {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized === 'excel' || normalized === 'xlsx' || normalized === '.xlsx') {
    return 'Excel 表格'
  }
  if (normalized === 'word' || normalized === 'docx' || normalized === '.docx') {
    return 'Word 文档'
  }
  return value || '—'
}

function normalizeFileType(value: string | null | undefined): string {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized.includes('excel') || normalized.includes('xls')) return 'Excel'
  return 'Word'
}

function detectFileType(name: string): string {
  const lower = name.toLowerCase()
  if (lower.endsWith('.xlsx')) return 'Excel'
  if (lower.endsWith('.docx')) return 'Word'
  return ''
}

async function loadUnits(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const response = await getArchiveProofTemplates()
    units.value = response.units ?? []
  } catch (error) {
    units.value = []
    loadError.value = getErrorMessage(error, '单位模板加载失败')
  } finally {
    loading.value = false
  }
}

function resetForm(): void {
  unitName.value = ''
  selectedFile.value = null
  fileType.value = 'Word'
  configJson.value = ''
  if (fileInputRef.value) fileInputRef.value.value = ''
}

async function scrollToForm(): Promise<void> {
  await nextTick()
  formRef.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function startCreate(): void {
  isCreateMode.value = true
  editingId.value = null
  resetForm()
  formVisible.value = true
  void scrollToForm()
}

function startEdit(item: ArchiveProofUnitTemplate): void {
  isCreateMode.value = false
  editingId.value = item.id
  unitName.value = item.unitName
  selectedFile.value = null
  fileType.value = normalizeFileType(item.fileType)
  configJson.value = ''
  if (fileInputRef.value) fileInputRef.value.value = ''
  formVisible.value = true
  void scrollToForm()
}

function cancelEdit(): void {
  formVisible.value = false
  resetForm()
}

function openFilePicker(): void {
  fileInputRef.value?.click()
}

function clearFile(): void {
  selectedFile.value = null
  if (fileInputRef.value) fileInputRef.value.value = ''
}

function onFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  if (!file) {
    selectedFile.value = null
    return
  }

  const detected = detectFileType(file.name)
  if (!detected) {
    ElMessage.warning('仅支持 .docx / .xlsx 模板文件')
    selectedFile.value = null
    input.value = ''
    return
  }

  selectedFile.value = file
  fileType.value = detected
}

async function save(): Promise<void> {
  const name = unitName.value.trim()
  if (!name) {
    ElMessage.warning('请输入接收单位名称')
    return
  }

  const duplicate = units.value.some(
    (item) => item.id !== editingId.value && item.unitName.trim() === name,
  )
  if (duplicate) {
    ElMessage.warning('该单位已配置模板，请直接编辑')
    return
  }

  if (isCreateMode.value && !selectedFile.value) {
    ElMessage.warning('请选择证明模板文件（.docx / .xlsx）')
    return
  }

  if (selectedFile.value && !fileType.value) {
    ElMessage.warning('无法识别模板文件类型')
    return
  }

  saving.value = true
  try {
    const input: ArchiveProofUnitInput = {
      unitName: name,
      fileType: fileType.value,
      file: selectedFile.value,
      configJson: configJson.value.trim() || undefined,
    }

    if (isCreateMode.value) {
      await createArchiveProofUnit(input)
    } else if (editingId.value !== null) {
      await updateArchiveProofUnit(editingId.value, input)
    } else {
      throw new Error('缺少编辑目标')
    }

    ElMessage.success(isCreateMode.value ? '新增成功' : '保存成功')
    formVisible.value = false
    resetForm()
    await loadUnits()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存单位模板失败'))
  } finally {
    saving.value = false
  }
}

async function remove(item: ArchiveProofUnitTemplate): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除单位「${item.unitName}」的证明模板吗？仅解除单位与模板的映射，模板文件保留。`,
      '确认删除',
      {
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  deletingId.value = item.id
  try {
    await deleteArchiveProofUnit(item.id)
    ElMessage.success('删除成功')
    if (editingId.value === item.id) {
      formVisible.value = false
      resetForm()
    }
    await loadUnits()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除单位模板失败'))
  } finally {
    deletingId.value = null
  }
}

function goBack(): void {
  void router.push('/archives/query')
}

onMounted(() => {
  void loadUnits()
})
</script>

<template>
  <div class="nc-page proof-template-page">
    <NcPageHeader
      title="单位模板维护"
      description="管理证明材料模板与格式配置"
      kicker="救助档案 · 证明模板"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回档案查询</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadUnits()">
          刷新
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="page-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadUnits()">重新加载</el-button>
    </el-alert>

    <!-- 浏览模式：单位模板列表 -->
    <NcCard v-if="!formVisible" title="单位模板列表">
      <template #actions>
        <div class="list-actions">
          <span class="count-text">共 {{ units.length }} 个</span>
          <el-button type="primary" :icon="Plus" @click="startCreate">
            新增单位模板
          </el-button>
        </div>
      </template>

      <el-skeleton v-if="loading" :rows="6" animated />

      <el-empty
        v-else-if="units.length === 0"
        description="尚未配置单位模板"
        :image-size="70"
      >
        <p class="empty-hint">
          点击「新增单位模板」，为接收单位上传专属证明模板文件。
        </p>
      </el-empty>

      <div v-else class="unit-list">
        <div
          v-for="item in sortedUnits"
          :key="item.id"
          class="unit-item"
        >
          <div class="unit-main">
            <strong>{{ item.unitName }}</strong>
            <p>模板：{{ item.templateName || '—' }}</p>
          </div>

          <span class="file-chip">{{ fileTypeLabel(item.fileType) }}</span>

          <div class="unit-times">
            <span>创建：{{ formatDateTimeText(item.createdAt) }}</span>
            <span>更新：{{ formatDateTimeText(item.updatedAt) }}</span>
          </div>

          <div class="unit-actions">
            <el-button size="small" :icon="Edit" @click="startEdit(item)">
              编辑
            </el-button>
            <el-button
              size="small"
              type="danger"
              plain
              :icon="Delete"
              :loading="deletingId === item.id"
              @click="remove(item)"
            >
              删除
            </el-button>
          </div>
        </div>
      </div>
    </NcCard>

    <!-- 表单模式：新增 / 编辑 -->
    <div v-else ref="formRef">
      <NcCard
        :title="formTitle"
        subtitle="上传模板文件后自动识别文件类型；编辑时文件可留空保留原模板"
      >
        <div class="form-field">
          <label class="field-label" for="proof-unit-name">
            接收单位名称 <em>*</em>
          </label>
          <el-input
            id="proof-unit-name"
            v-model="unitName"
            maxlength="100"
            show-word-limit
            placeholder="如：XX市民政局 / XX街道办"
          />
        </div>

        <div class="form-field">
          <span class="field-label">
            证明模板文件（.docx / .xlsx，含 {字段} 占位符）
          </span>
          <input
            ref="fileInputRef"
            class="file-input"
            type="file"
            accept=".docx,.xlsx"
            @change="onFileChange"
          />
          <div class="file-row">
            <div class="file-box">
              <span v-if="fileName" class="file-name">{{ fileName }}</span>
              <span v-else class="file-placeholder">
                {{
                  isCreateMode
                    ? '未选择文件'
                    : '未选择文件（编辑时留空表示保留原模板）'
                }}
              </span>
            </div>
            <el-button :icon="UploadFilled" @click="openFilePicker">
              选择文件
            </el-button>
            <el-button v-if="fileName" plain @click="clearFile">清除</el-button>
          </div>
          <p v-if="fileName" class="field-hint">
            识别类型：{{ fileTypeLabel(fileType) }}
          </p>
        </div>

        <div class="form-field">
          <span class="field-label">
            字段映射配置 JSON（可选，留空则按 {占位符} 直接替换）
          </span>
          <el-input
            v-model="configJson"
            type="textarea"
            :rows="3"
            placeholder='字段映射 JSON 配置，可留空（例: { "fields": [...] }）'
          />
        </div>

        <div class="form-actions">
          <el-button :disabled="saving" @click="cancelEdit">取消</el-button>
          <el-button type="primary" :loading="saving" @click="save">
            {{ isCreateMode ? '保存模板' : '保存修改' }}
          </el-button>
        </div>
      </NcCard>
    </div>
  </div>
</template>

<style scoped>
.proof-template-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.page-alert {
  margin: 0;
}

.list-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.count-text {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.06em;
  color: var(--nc-muted);
}

.unit-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.unit-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto auto;
  gap: 16px;
  align-items: center;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.unit-main {
  min-width: 0;
}

.unit-main strong {
  display: block;
  overflow: hidden;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.unit-main p {
  margin-top: 2px;
  overflow: hidden;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-chip {
  padding: 1px 8px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  color: var(--nc-seal-dark);
  white-space: nowrap;
}

.unit-times {
  display: flex;
  flex-direction: column;
  gap: 1px;
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
  text-align: right;
  white-space: nowrap;
}

.unit-actions {
  display: flex;
  gap: 6px;
}

.unit-actions :deep(.el-button) {
  margin-left: 0;
}

.empty-hint {
  margin-top: 8px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
  text-align: center;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 18px;
}

.field-label {
  font-size: var(--nc-text-sm);
  font-weight: 600;
  color: var(--nc-muted-2);
}

.field-label em {
  font-style: normal;
  color: var(--nc-seal);
}

.field-hint {
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

.file-input {
  display: none;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-row :deep(.el-button) {
  flex: none;
  margin-left: 0;
}

.file-box {
  flex: 1;
  min-width: 0;
  padding: 8px 12px;
  overflow: hidden;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  font-size: var(--nc-text-sm);
  color: var(--nc-ink-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-name {
  color: var(--nc-ink);
}

.file-placeholder {
  color: var(--nc-muted);
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.form-actions :deep(.el-button) {
  margin-left: 0;
}

@media (max-width: 767px) {
  .unit-item {
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 10px;
  }

  .unit-times {
    grid-column: 1 / -1;
    flex-direction: row;
    flex-wrap: wrap;
    gap: 8px;
    text-align: left;
  }

  .unit-actions {
    grid-column: 1 / -1;
    justify-content: flex-end;
  }

  .file-row {
    flex-wrap: wrap;
  }

  .file-box {
    flex-basis: 100%;
  }
}
</style>
