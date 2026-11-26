<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Document, UploadFilled } from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import { importOrganizations, previewOrganizationImport } from '@/api/organizations'
import { getMyPermissions } from '@/api/users'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { OrganizationImportResult, OrganizationPreview } from '@/types/organizations'

const ACCEPTED_EXTENSIONS = ['xlsx', 'xls', 'csv']
const MAX_FILE_BYTES = 20 * 1024 * 1024
const MAX_ERROR_ROWS = 50

const router = useRouter()

/* ───────────────────────────── 权限 ───────────────────────────── */

const permissions = ref<string[]>([])
const isSuperAdmin = ref(false)
const permissionsLoaded = ref(false)
const permissionsError = ref('')

const canImport = computed(
  () =>
    permissionsLoaded.value &&
    (isSuperAdmin.value || permissions.value.includes('ORG_CREATE')),
)

const permissionHint = computed(() => {
  if (permissionsError.value) return permissionsError.value
  if (permissionsLoaded.value && !canImport.value) {
    return '当前账号缺少 ORG_CREATE 权限，无法预览或导入组织数据；请联系管理员授权。'
  }
  return ''
})

async function loadPermissions(): Promise<void> {
  try {
    const data = await getMyPermissions()
    permissions.value = data.permissions ?? []
    isSuperAdmin.value = data.isSuperAdmin
    permissionsLoaded.value = true
    permissionsError.value = ''
  } catch (error) {
    permissionsLoaded.value = false
    permissionsError.value = getErrorMessage(error, '权限信息加载失败，导入操作已禁用')
  }
}

function forbiddenMessage(): string {
  return '当前账号缺少 ORG_CREATE 权限，无法导入组织数据；请联系管理员授权。'
}

function actionErrorText(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 403) return forbiddenMessage()
  return getErrorMessage(error, fallback)
}

/* ───────────────────────────── 文件与预览 ───────────────────────────── */

const fileInput = ref<HTMLInputElement | null>(null)
const selectedFile = ref<File | null>(null)
const previewing = ref(false)
const importing = ref(false)
const preview = ref<OrganizationPreview | null>(null)
const result = ref<OrganizationImportResult | null>(null)
const errorText = ref('')
const dragging = ref(false)

const previewRows = computed(() => (preview.value?.previewRows ?? []).slice(0, 5))
const totalRows = computed(() => preview.value?.totalRows ?? 0)
const resultTotal = computed(() =>
  result.value ? result.value.importedCount + result.value.errorCount : 0,
)
const shownErrors = computed(() => (result.value?.errors ?? []).slice(0, MAX_ERROR_ROWS))
const hiddenErrorCount = computed(() =>
  result.value ? Math.max(result.value.errorCount - MAX_ERROR_ROWS, 0) : 0,
)
const shownWarnings = computed(() => (result.value?.warnings ?? []).slice(0, 20))

function fileSizeText(file: File): string {
  if (file.size < 1024) return `${file.size} B`
  if (file.size < 1024 * 1024) return `${(file.size / 1024).toFixed(1)} KB`
  return `${(file.size / 1024 / 1024).toFixed(1)} MB`
}

function validateFile(file: File): boolean {
  const extension = file.name.split('.').pop()?.toLowerCase() ?? ''
  if (!ACCEPTED_EXTENSIONS.includes(extension)) {
    ElMessage.warning('仅支持 .xlsx / .xls / .csv 文件')
    return false
  }
  if (file.size > MAX_FILE_BYTES) {
    ElMessage.warning('文件大小不能超过 20 MB')
    return false
  }
  return true
}

function pickFile(): void {
  if (importing.value) return
  if (permissionsLoaded.value && !canImport.value) {
    ElMessage.warning(forbiddenMessage())
    return
  }
  fileInput.value?.click()
}

async function acceptFile(file: File | null | undefined): Promise<void> {
  if (!file || previewing.value) return
  if (!validateFile(file)) return
  selectedFile.value = file
  preview.value = null
  result.value = null
  errorText.value = ''
  await runPreview()
}

async function onFilePicked(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  await acceptFile(file)
}

async function onDrop(event: DragEvent): Promise<void> {
  dragging.value = false
  if (importing.value) return
  if (permissionsLoaded.value && !canImport.value) {
    ElMessage.warning(forbiddenMessage())
    return
  }
  await acceptFile(event.dataTransfer?.files?.[0] ?? null)
}

async function runPreview(): Promise<void> {
  const file = selectedFile.value
  if (!file || previewing.value) return

  previewing.value = true
  preview.value = null
  result.value = null
  errorText.value = ''
  try {
    const data = await previewOrganizationImport(file)
    preview.value = {
      totalRows: data.totalRows ?? 0,
      previewRows: data.previewRows ?? [],
      error: data.error ?? null,
    }
    if (data.error) errorText.value = data.error
  } catch (error) {
    preview.value = null
    errorText.value = actionErrorText(error, '导入预览失败，请检查文件后重试')
  } finally {
    previewing.value = false
  }
}

async function startImport(): Promise<void> {
  const file = selectedFile.value
  if (!file || importing.value) return
  if (permissionsLoaded.value && !canImport.value) {
    ElMessage.warning(forbiddenMessage())
    return
  }

  const totalHint = preview.value ? `共 ${preview.value.totalRows} 行，` : ''
  try {
    await ElMessageBox.confirm(
      `${totalHint}将从「${file.name}」导入组织数据。重名或无法解析区划的行会跳过并记录，确定开始导入吗？`,
      '开始导入组织',
      { confirmButtonText: '开始导入', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  importing.value = true
  result.value = null
  errorText.value = ''
  try {
    const data = await importOrganizations(file)
    result.value = {
      success: data.success,
      importedCount: data.importedCount ?? 0,
      errorCount: data.errorCount ?? 0,
      errors: data.errors ?? [],
      warnings: data.warnings ?? [],
      message: data.message ?? '',
    }
    if (data.success) {
      ElMessage.success(data.message || `导入完成：成功 ${data.importedCount} 条`)
    } else {
      errorText.value = data.message || '导入失败'
      ElMessage.error(errorText.value)
    }
  } catch (error) {
    errorText.value = actionErrorText(error, '导入组织失败，请稍后重试')
    ElMessage.error(errorText.value)
  } finally {
    importing.value = false
  }
}

function resetFile(): void {
  if (importing.value || previewing.value) return
  selectedFile.value = null
  preview.value = null
  result.value = null
  errorText.value = ''
  if (fileInput.value) fileInput.value.value = ''
}

function goBack(): void {
  router.push('/system/organizations')
}

onMounted(() => {
  void loadPermissions()
})
</script>

<template>
  <div class="nc-page import-page">
    <NcPageHeader
      title="组织数据导入"
      description="从Excel文件批量导入组织数据"
      kicker="系统 · 经办机构"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回组织管理</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="permissionHint"
      type="warning"
      :title="permissionHint"
      :closable="false"
      show-icon
    />

    <div class="import-layout">
      <NcCard title="选择导入文件" subtitle="支持 .xlsx / .xls / .csv，文件表头需包含「名称」列。">
        <div
          class="drop-zone"
          :class="{ 'is-dragging': dragging, 'is-disabled': importing || (permissionsLoaded && !canImport) }"
          role="button"
          tabindex="0"
          @click="pickFile"
          @keydown.enter.prevent="pickFile"
          @keydown.space.prevent="pickFile"
          @dragover.prevent="dragging = true"
          @dragleave.prevent="dragging = false"
          @drop.prevent="onDrop"
        >
          <el-icon class="drop-zone__icon"><UploadFilled /></el-icon>
          <strong>点击选择文件，或将文件拖拽到此处</strong>
          <span>仅支持 Excel / CSV，单个文件不超过 20 MB</span>
        </div>

        <input
          ref="fileInput"
          class="hidden-input"
          type="file"
          accept=".xlsx,.xls,.csv"
          @change="onFilePicked"
        />

        <div v-if="selectedFile" class="file-row">
          <el-icon class="file-row__icon"><Document /></el-icon>
          <div class="file-row__text">
            <strong :title="selectedFile.name">{{ selectedFile.name }}</strong>
            <span>{{ fileSizeText(selectedFile) }}</span>
          </div>
          <el-button size="small" :disabled="importing || previewing" @click="resetFile">
            移除
          </el-button>
          <el-button
            size="small"
            :loading="previewing"
            :disabled="importing"
            @click="runPreview"
          >
            重新预览
          </el-button>
        </div>

        <p class="import-note">
          导入前请确认文件首行包含「名称」列，可选列包括：简称、负责人、电话、地址、邮编、机构性质、机构类型、机构类别、地级市、县区、乡镇、村/社区。
        </p>
      </NcCard>

      <NcCard title="导入预览">
        <template #actions>
          <span v-if="preview" class="preview-count">共 {{ totalRows }} 条</span>
        </template>

        <div v-loading="previewing" class="preview-body">
          <el-alert
            v-if="errorText && !result"
            type="error"
            :title="errorText"
            :closable="false"
            show-icon
          />

          <el-empty
            v-else-if="!preview"
            description="选择文件后自动显示前 5 行预览"
            :image-size="80"
          />

          <el-empty
            v-else-if="previewRows.length === 0"
            :description="preview.totalRows > 0 ? '文件已读取，但没有可预览的数据行' : '文件中没有数据行'"
            :image-size="80"
          />

          <template v-else>
            <el-table :data="previewRows" size="small" border max-height="320">
              <el-table-column prop="rowNumber" label="文件行" width="70" />
              <el-table-column prop="name" label="名称" min-width="150" show-overflow-tooltip />
              <el-table-column
                prop="abbreviation"
                label="简称"
                min-width="90"
                show-overflow-tooltip
              />
              <el-table-column
                prop="principal"
                label="负责人"
                min-width="90"
                show-overflow-tooltip
              />
              <el-table-column prop="phone" label="电话" min-width="110" show-overflow-tooltip />
              <el-table-column prop="region" label="所在地区" min-width="160" show-overflow-tooltip />
            </el-table>
            <p class="preview-foot">
              仅显示前 {{ previewRows.length }} 行，文件共 {{ totalRows }} 条数据。
            </p>
          </template>
        </div>

        <template #footer>
          <div class="import-actions">
            <span class="import-actions__hint">
              {{
                preview
                  ? `待导入 ${totalRows} 条`
                  : selectedFile
                    ? '等待预览完成'
                    : '请先选择导入文件'
              }}
            </span>
            <el-button
              type="primary"
              :icon="UploadFilled"
              :loading="importing"
              :disabled="!selectedFile || previewing || (permissionsLoaded && !canImport)"
              @click="startImport"
            >
              开始导入
            </el-button>
          </div>
        </template>
      </NcCard>

      <NcCard v-if="importing || result" title="导入结果">
        <div v-if="importing" class="importing-block">
          <p>正在导入，请勿关闭页面…</p>
          <el-progress :percentage="100" :indeterminate="true" :show-text="false" :duration="1.2" />
        </div>

        <template v-else-if="result">
          <el-alert
            :type="result.success ? 'success' : 'error'"
            :title="result.message || (result.success ? '导入完成' : '导入失败')"
            :closable="false"
            show-icon
          />

          <div class="result-grid">
            <div class="result-tile result-tile--success">
              <span>成功</span>
              <strong>{{ result.importedCount }}</strong>
            </div>
            <div class="result-tile result-tile--danger">
              <span>失败</span>
              <strong>{{ result.errorCount }}</strong>
            </div>
            <div class="result-tile">
              <span>总计</span>
              <strong>{{ resultTotal }}</strong>
            </div>
          </div>

          <div v-if="shownErrors.length > 0" class="message-block">
            <div class="message-block__head">
              <strong>失败明细</strong>
              <span v-if="hiddenErrorCount > 0">仅显示前 {{ MAX_ERROR_ROWS }} 条，共 {{ result.errorCount }} 条</span>
            </div>
            <ol class="message-list message-list--danger">
              <li v-for="(item, index) in shownErrors" :key="`error-${index}`">{{ item }}</li>
            </ol>
          </div>

          <div v-if="shownWarnings.length > 0" class="message-block">
            <div class="message-block__head">
              <strong>提示</strong>
            </div>
            <ul class="message-list message-list--warn">
              <li v-for="(item, index) in shownWarnings" :key="`warn-${index}`">{{ item }}</li>
            </ul>
          </div>

          <div class="result-actions">
            <el-button @click="goBack">返回组织管理</el-button>
            <el-button type="primary" plain :disabled="importing" @click="resetFile">
              继续导入
            </el-button>
          </div>
        </template>
      </NcCard>
    </div>
  </div>
</template>

<style scoped>
.import-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.import-layout {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.drop-zone {
  display: flex;
  min-height: 160px;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 24px 18px;
  border: 1px dashed var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  text-align: center;
  transition: border-color var(--nc-transition), background-color var(--nc-transition);
}

.drop-zone:hover,
.drop-zone.is-dragging {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-wash);
}

.drop-zone:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 2px;
}

.drop-zone.is-disabled {
  cursor: not-allowed;
  opacity: 0.65;
}

.drop-zone__icon {
  color: var(--nc-seal);
  font-size: 34px;
}

.drop-zone strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.drop-zone span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.hidden-input {
  display: none;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
}

.file-row__icon {
  flex: none;
  color: var(--nc-pine);
  font-size: 20px;
}

.file-row__text {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  gap: 2px;
}

.file-row__text strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-row__text span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.import-note {
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.preview-count {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.preview-body {
  min-height: 180px;
}

.preview-foot {
  margin-top: 8px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.import-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
}

.import-actions__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.importing-block {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.importing-block p {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.result-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin-top: 12px;
}

.result-tile {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 10px 14px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-ink-2);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
}

.result-tile--success {
  border-left-color: var(--nc-pine);
}

.result-tile--danger {
  border-left-color: var(--nc-seal);
}

.result-tile span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.result-tile strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 22px;
}

.message-block {
  margin-top: 14px;
}

.message-block__head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 6px;
}

.message-block__head strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.message-block__head span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.message-list {
  max-height: 260px;
  margin: 0;
  padding: 8px 12px 8px 28px;
  overflow: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.message-list--danger {
  border-left: 3px solid var(--nc-seal);
}

.message-list--warn {
  border-left: 3px solid var(--nc-amber);
}

.result-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

@media (max-width: 767px) {
  .file-row {
    flex-wrap: wrap;
  }

  .file-row__text {
    flex-basis: 100%;
  }

  .result-grid {
    grid-template-columns: 1fr;
  }

  .import-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .import-actions__hint {
    text-align: center;
  }
}
</style>
