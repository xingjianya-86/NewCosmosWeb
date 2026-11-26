<script setup lang="ts">
import { computed, h, nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, ElNotification } from 'element-plus'
import {
  ArrowLeft,
  Back,
  Bottom,
  CollectionTag,
  Delete,
  Download,
  Edit,
  Plus,
  Refresh,
  Search,
  Top,
  Upload,
  UploadFilled,
} from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  createTemplateAdmin,
  createTemplateCategory,
  deleteTemplateAdmin,
  deleteTemplateCategory,
  downloadTemplateAdminFile,
  getTemplateAdmin,
  getTemplateCategories,
  getTemplateDocTypes,
  listTemplateAdmin,
  moveTemplateAdmin,
  updateTemplateAdmin,
  updateTemplateCategory,
} from '@/api/templateAdmin'
import type {
  TemplateAdminDetail,
  TemplateAdminListItem,
  TemplateCategoryNode,
  TemplateMoveDirection,
} from '@/types/templateAdmin'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { formatDateTimeText } from './archiveDisplay'

interface CategoryTreeNodeInstance {
  expand(callback?: () => void, expandParent?: boolean): void
  collapse(): void
}

interface CategoryTreeInstance {
  setCheckedKeys(keys: string[], leafOnly?: boolean): void
  getCheckedKeys(leafOnly?: boolean): Array<string | number>
  getNode(key: string | number): CategoryTreeNodeInstance | null
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 1100px)')

const mode = ref<'list' | 'upload'>('list')
const keyword = ref('')
const templates = ref<TemplateAdminListItem[]>([])
const listLoading = ref(false)
const listError = ref('')

const filterCategory = ref('')
const filterDocType = ref('')
const filterFileType = ref('')

const selectedId = ref<number | null>(null)
const detail = ref<TemplateAdminDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
const copies = ref(1)
const savingCopies = ref(false)
const detailDocType = ref('')
const savingDocType = ref(false)
const downloading = ref(false)
const changingFile = ref(false)
const moving = ref<TemplateMoveDirection | null>(null)
const deleting = ref(false)
const replaceFileInput = ref<HTMLInputElement | null>(null)

const categoryTree = ref<TemplateCategoryNode[]>([])
const categoryLoading = ref(false)
const categoryError = ref('')
const categoryTreeProps = { label: 'label', children: 'children' }

const docTypes = ref<string[]>([])
const docTypesLoading = ref(false)
const docTypesError = ref('')

const uploadFile = ref<File | null>(null)
const uploadName = ref('')
const uploadDocType = ref('')
const uploadConfigJson = ref('')
const uploadFileInput = ref<HTMLInputElement | null>(null)
const uploadTreeRef = ref<CategoryTreeInstance | null>(null)
const uploadSubmitting = ref(false)

const configVisible = ref(false)
const configDraft = ref('')
const savingConfig = ref(false)

const categoryVisible = ref(false)
const categoryKeys = ref<string[]>([])
const categoryTreeRef = ref<CategoryTreeInstance | null>(null)
const savingCategories = ref(false)

const manageVisible = ref(false)
const manageTreeRef = ref<CategoryTreeInstance | null>(null)
const categoryMutating = ref(false)

const nodeDialogVisible = ref(false)
const nodeDialogMode = ref<'create' | 'rename'>('create')
const nodeParent = ref<TemplateCategoryNode | null>(null)
const nodeEditing = ref<TemplateCategoryNode | null>(null)
const nodeLabel = ref('')
const nodeSaving = ref(false)

let listTicket = 0
let detailTicket = 0

function actionError(
  error: unknown,
  fallback: string,
  action: string,
  permission = 'TEMPLATE_MANAGE',
): string {
  if (error instanceof ApiError && error.status === 403) {
    return `无权限${action}（需要 ${permission} 权限）`
  }
  return getErrorMessage(error, fallback)
}

function normalizeWarnings(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  return value
    .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
    .map((item) => item.trim())
}

function warningsOf(result: unknown): string[] {
  if (!result || typeof result !== 'object') return []
  return normalizeWarnings((result as { configWarnings?: unknown }).configWarnings)
}

function showConfigWarnings(value: unknown): void {
  const warnings = normalizeWarnings(value)
  if (warnings.length === 0) return
  ElNotification({
    title: `配置提示（${warnings.length}）`,
    type: 'warning',
    duration: 12_000,
    showClose: true,
    message: h(
      'div',
      { style: 'display:flex;flex-direction:column;gap:4px;line-height:1.6;' },
      [
        ...warnings.slice(0, 5).map((text, index) =>
          h('p', { key: index, style: 'margin:0' }, text),
        ),
        ...(warnings.length > 5
          ? [
              h(
                'p',
                { key: 'more', style: 'margin:0;color:#909399' },
                `…另有 ${warnings.length - 5} 项，详见模板详情`,
              ),
            ]
          : []),
      ],
    ),
  })
}

function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

function fileKind(value: string | null | undefined): 'word' | 'excel' | 'other' {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized.includes('excel') || normalized.includes('xls')) return 'excel'
  if (normalized.includes('word') || normalized.includes('doc')) return 'word'
  return 'other'
}

function fileTypeLabel(value: string | null | undefined): string {
  const kind = fileKind(value)
  if (kind === 'excel') return 'Excel 表格'
  if (kind === 'word') return 'Word 文档'
  return value || '—'
}

function extensionFor(fileType: string | null | undefined): string {
  const value = (fileType ?? '').trim().toLowerCase()
  if (value.includes('xls')) return '.xlsx'
  if (value.includes('doc')) return '.docx'
  if (value.startsWith('.')) return value
  return '.docx'
}

function templateFileName(item: TemplateAdminDetail): string {
  const base = (item.name || `模板_${item.id}`).replace(/[\\/:*?"<>|]/g, '_')
  const extension = extensionFor(item.fileType)
  return base.toLowerCase().endsWith(extension) ? base : `${base}${extension}`
}

function categoryShort(path: string): string {
  const parts = (path ?? '').split('/').filter(Boolean)
  return parts.length > 0 ? parts[parts.length - 1] : path
}

function isCustomCategory(node: TemplateCategoryNode): boolean {
  return node.isBuiltIn === false && typeof node.id === 'number'
}

const categoryLabelMap = computed(() => {
  const map = new Map<string, string>()
  const walk = (nodes: TemplateCategoryNode[]): void => {
    for (const node of nodes) {
      if (node.path) map.set(node.path, node.label)
      if (node.children && node.children.length > 0) walk(node.children)
    }
  }
  walk(categoryTree.value)
  return map
})

const categoryAncestorMap = computed(() => {
  const map = new Map<string, string[]>()
  const walk = (nodes: TemplateCategoryNode[], ancestors: string[]): void => {
    for (const node of nodes) {
      if (node.path) map.set(node.path, ancestors)
      const nextAncestors = node.path ? [...ancestors, node.path] : ancestors
      if (node.children && node.children.length > 0) walk(node.children, nextAncestors)
    }
  }
  walk(categoryTree.value, [])
  return map
})

function categoryLabel(path: string): string {
  return categoryLabelMap.value.get(path) ?? categoryShort(path)
}

function collectCategoryPaths(nodes: TemplateCategoryNode[]): string[] {
  const paths: string[] = []
  const walk = (list: TemplateCategoryNode[]): void => {
    for (const node of list) {
      if (node.path) paths.push(node.path)
      if (node.children && node.children.length > 0) walk(node.children)
    }
  }
  walk(nodes)
  return paths
}

function readCheckedKeys(tree: CategoryTreeInstance | null): string[] {
  if (!tree) return []
  return tree.getCheckedKeys(false).map((key) => String(key))
}

function setTreeExpanded(tree: CategoryTreeInstance | null, expanded: boolean): void {
  if (!tree) return
  for (const path of collectCategoryPaths(categoryTree.value)) {
    const node = tree.getNode(path)
    if (!node) continue
    if (expanded) {
      node.expand()
    } else {
      node.collapse()
    }
  }
}

function checkAllTree(tree: CategoryTreeInstance | null): void {
  tree?.setCheckedKeys(collectCategoryPaths(categoryTree.value))
}

function clearTree(tree: CategoryTreeInstance | null): void {
  tree?.setCheckedKeys([])
}

function parseConfigJson(text: string | null | undefined): Record<string, unknown> | null {
  const raw = (text ?? '').trim()
  if (!raw) return null
  try {
    const parsed: unknown = JSON.parse(raw)
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>
    }
    return null
  } catch {
    return null
  }
}

function clampCopies(value: number): number {
  return Math.min(99, Math.max(1, Math.trunc(value)))
}

function numericCopies(value: unknown): number | null {
  const parsed =
    typeof value === 'number'
      ? value
      : typeof value === 'string' && value.trim()
        ? Number(value)
        : Number.NaN
  return Number.isFinite(parsed) ? clampCopies(parsed) : null
}

function resolveCopies(item: TemplateAdminDetail): number {
  const direct = numericCopies(item.copies ?? item.defaultCopies)
  if (direct !== null) return direct
  const config = parseConfigJson(item.configJson)
  const configCopies = numericCopies(config?.copies)
  return configCopies ?? 1
}

function prettyConfig(text: string | null | undefined): string {
  const raw = (text ?? '').trim()
  if (!raw) return ''
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

const hasFilters = computed(
  () =>
    filterCategory.value !== '' ||
    filterDocType.value !== '' ||
    filterFileType.value !== '',
)

const filteredTemplates = computed(() => {
  const category = filterCategory.value
  const docType = filterDocType.value
  const kind = filterFileType.value
  return templates.value.filter((item) => {
    if (category) {
      const matched = (item.categories ?? []).some((path) => {
        if (path === category || path.startsWith(`${category}/`)) return true
        const ancestors = categoryAncestorMap.value.get(path) ?? []
        return ancestors.includes(category)
      })
      if (!matched) return false
    }
    if (docType && (item.docType ?? '') !== docType) return false
    if (kind && fileKind(item.fileType) !== kind) return false
    return true
  })
})

const categoryFilterOptions = computed(() => {
  const paths = new Set<string>()
  for (const item of templates.value) {
    for (const path of item.categories ?? []) {
      if (path) paths.add(path)
    }
  }
  return [...paths].sort((left, right) => left.localeCompare(right, 'zh-CN'))
})

const docTypeFilterOptions = computed(() => {
  const types = new Set<string>(docTypes.value)
  for (const item of templates.value) {
    if (item.docType) types.add(item.docType)
  }
  return [...types].filter(Boolean).sort((left, right) => left.localeCompare(right, 'zh-CN'))
})

const detailDocTypeOptions = computed(() => {
  const types = new Set<string>(docTypes.value)
  const current = (detail.value?.docType ?? '').trim()
  if (current) types.add(current)
  return [...types].filter(Boolean)
})

const classificationCopiesCount = computed(() => {
  const map = detail.value?.copiesByClassification
  if (!map || typeof map !== 'object' || Array.isArray(map)) return 0
  return Object.keys(map).length
})

const detailWarnings = computed(() => normalizeWarnings(detail.value?.configWarnings))

function clearFilters(): void {
  filterCategory.value = ''
  filterDocType.value = ''
  filterFileType.value = ''
}

async function loadCategories(): Promise<void> {
  const uploadChecked = uploadTreeRef.value ? readCheckedKeys(uploadTreeRef.value) : null
  categoryLoading.value = true
  categoryError.value = ''
  try {
    categoryTree.value = await getTemplateCategories()
    await nextTick()
    if (uploadTreeRef.value && uploadChecked) {
      uploadTreeRef.value.setCheckedKeys(uploadChecked)
    }
    if (categoryVisible.value) {
      categoryTreeRef.value?.setCheckedKeys(categoryKeys.value)
    }
    if (manageVisible.value) {
      setTreeExpanded(manageTreeRef.value, true)
    }
  } catch (error) {
    categoryTree.value = []
    categoryError.value = actionError(error, '分类树加载失败', '查看模板分类')
  } finally {
    categoryLoading.value = false
  }
}

async function loadDocTypes(): Promise<void> {
  docTypesLoading.value = true
  docTypesError.value = ''
  try {
    docTypes.value = await getTemplateDocTypes()
  } catch (error) {
    docTypes.value = []
    docTypesError.value = actionError(error, '文档类型加载失败', '查看文档类型')
  } finally {
    docTypesLoading.value = false
  }
}

async function loadTemplates(): Promise<void> {
  const ticket = ++listTicket
  listLoading.value = true
  listError.value = ''
  try {
    const rows = await listTemplateAdmin(keyword.value)
    if (ticket !== listTicket) return
    templates.value = rows
    if (selectedId.value !== null && !rows.some((row) => row.id === selectedId.value)) {
      selectedId.value = null
      detail.value = null
      detailError.value = ''
    }
  } catch (error) {
    if (ticket !== listTicket) return
    templates.value = []
    listError.value = actionError(error, '模板列表加载失败', '查看文书模板')
  } finally {
    if (ticket === listTicket) listLoading.value = false
  }
}

async function loadDetail(id: number): Promise<void> {
  const ticket = ++detailTicket
  detailLoading.value = true
  detailError.value = ''
  detail.value = null
  try {
    const response = await getTemplateAdmin(id)
    if (ticket !== detailTicket || selectedId.value !== id) return
    detail.value = response
    copies.value = resolveCopies(response)
    detailDocType.value = response.docType ?? ''
  } catch (error) {
    if (ticket !== detailTicket || selectedId.value !== id) return
    detailError.value = actionError(error, '模板详情加载失败', '查看文书模板')
  } finally {
    if (ticket === detailTicket) detailLoading.value = false
  }
}

function selectTemplate(item: TemplateAdminListItem): void {
  if (selectedId.value === item.id) return
  selectedId.value = item.id
  void loadDetail(item.id)
}

function handleSearch(): void {
  if (mode.value === 'upload') return
  void loadTemplates()
}

function resetSearch(): void {
  keyword.value = ''
  clearFilters()
  void loadTemplates()
}

function backToList(): void {
  if (mode.value === 'upload') {
    mode.value = 'list'
    resetUpload()
  }
  selectedId.value = null
  detail.value = null
  detailError.value = ''
}

function openUpload(): void {
  mode.value = 'upload'
  selectedId.value = null
  detail.value = null
  detailError.value = ''
  resetUpload()
  if (docTypes.value.length === 0 && !docTypesLoading.value) void loadDocTypes()
  if (categoryTree.value.length === 0 && !categoryLoading.value) void loadCategories()
}

function resetUpload(): void {
  uploadFile.value = null
  uploadName.value = ''
  uploadDocType.value = ''
  uploadConfigJson.value = ''
  if (uploadFileInput.value) uploadFileInput.value.value = ''
  void nextTick(() => clearTree(uploadTreeRef.value))
}

function pickUploadFile(): void {
  uploadFileInput.value?.click()
}

function onUploadFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  if (!file) {
    uploadFile.value = null
    return
  }
  if (!/\.(docx|xlsx)$/i.test(file.name)) {
    ElMessage.warning('仅支持 .docx / .xlsx 模板文件')
    input.value = ''
    uploadFile.value = null
    return
  }
  uploadFile.value = file
  if (!uploadName.value.trim()) {
    uploadName.value = file.name.replace(/\.[^.]+$/, '')
  }
}

function checkAllUploadCategories(): void {
  checkAllTree(uploadTreeRef.value)
}

function clearUploadCategories(): void {
  clearTree(uploadTreeRef.value)
}

function expandUploadCategories(): void {
  setTreeExpanded(uploadTreeRef.value, true)
}

function collapseUploadCategories(): void {
  setTreeExpanded(uploadTreeRef.value, false)
}

async function submitUpload(): Promise<void> {
  if (!uploadFile.value) {
    ElMessage.warning('请选择模板文件（.docx / .xlsx）')
    return
  }
  const name = uploadName.value.trim()
  if (!name) {
    ElMessage.warning('请输入模板名称')
    return
  }
  if (!uploadDocType.value) {
    ElMessage.warning('请选择文档类型')
    return
  }

  const configJson = uploadConfigJson.value.trim()
  if (configJson) {
    try {
      JSON.parse(configJson)
    } catch {
      ElMessage.warning('字段映射 JSON 格式不正确，请检查后再上传')
      return
    }
  }

  const categories = readCheckedKeys(uploadTreeRef.value)
  uploadSubmitting.value = true
  try {
    const created = await createTemplateAdmin({
      file: uploadFile.value,
      name,
      docType: uploadDocType.value,
      categories,
      configJson: configJson || undefined,
    })
    ElMessage.success('模板上传成功')
    showConfigWarnings(warningsOf(created))
    mode.value = 'list'
    keyword.value = ''
    clearFilters()
    resetUpload()
    await loadTemplates()
    if (created?.id) {
      selectedId.value = created.id
      await loadDetail(created.id)
    }
  } catch (error) {
    ElMessage.error(actionError(error, '模板上传失败', '上传文书模板'))
  } finally {
    uploadSubmitting.value = false
  }
}

async function refreshAll(): Promise<void> {
  await loadTemplates()
  if (selectedId.value !== null) await loadDetail(selectedId.value)
  void loadCategories()
  void loadDocTypes()
}

function chooseReplaceFile(): void {
  replaceFileInput.value?.click()
}

async function onReplaceFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file || !detail.value) return
  if (!/\.(docx|xlsx)$/i.test(file.name)) {
    ElMessage.warning('仅支持 .docx / .xlsx 模板文件')
    return
  }

  const current = detail.value
  changingFile.value = true
  try {
    const result = await updateTemplateAdmin(current.id, {
      file,
      name: current.name,
      docType: current.docType,
      categories: current.categories ?? [],
      configJson: current.configJson ?? '',
    })
    ElMessage.success('模板文件已更新')
    showConfigWarnings(warningsOf(result))
    await loadDetail(current.id)
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '变更模板文件失败', '变更模板文件'))
  } finally {
    changingFile.value = false
  }
}

async function downloadFile(): Promise<void> {
  if (!detail.value) return
  downloading.value = true
  try {
    const blob = await downloadTemplateAdminFile(detail.value.id)
    saveBlob(blob, templateFileName(detail.value))
    ElMessage.success('模板文件已开始下载')
  } catch (error) {
    ElMessage.error(actionError(error, '下载模板文件失败', '下载文书模板'))
  } finally {
    downloading.value = false
  }
}

async function saveCopies(): Promise<void> {
  if (!detail.value) return
  const value = Math.trunc(Number(copies.value))
  if (!Number.isFinite(value) || value < 1 || value > 99) {
    ElMessage.warning('默认份数需为 1-99 的整数')
    return
  }

  savingCopies.value = true
  try {
    const result = await updateTemplateAdmin(detail.value.id, { copies: value })
    ElMessage.success('默认份数已保存')
    showConfigWarnings(warningsOf(result))
    await loadDetail(detail.value.id)
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '保存默认份数失败', '修改文书模板'))
  } finally {
    savingCopies.value = false
  }
}

function openConfigEdit(): void {
  if (!detail.value) return
  configDraft.value = prettyConfig(detail.value.configJson)
  configVisible.value = true
}

async function saveConfig(): Promise<void> {
  if (!detail.value) return
  const text = configDraft.value.trim()
  if (text) {
    try {
      JSON.parse(text)
    } catch {
      ElMessage.warning('JSON 格式不正确，请检查后再保存')
      return
    }
  }

  savingConfig.value = true
  try {
    const result = await updateTemplateAdmin(detail.value.id, { configJson: text })
    ElMessage.success('字段映射已保存')
    showConfigWarnings(warningsOf(result))
    configVisible.value = false
    await loadDetail(detail.value.id)
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '保存字段映射失败', '修改文书模板'))
  } finally {
    savingConfig.value = false
  }
}

async function saveDocType(): Promise<void> {
  if (!detail.value) return
  const value = detailDocType.value.trim()
  if (!value) {
    ElMessage.warning('请选择文档类型')
    return
  }

  savingDocType.value = true
  try {
    const result = await updateTemplateAdmin(detail.value.id, { docType: value })
    ElMessage.success('文档类型已保存')
    showConfigWarnings(warningsOf(result))
    await loadDetail(detail.value.id)
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '保存文档类型失败', '修改文书模板'))
  } finally {
    savingDocType.value = false
  }
}

function openCategoryEdit(): void {
  if (!detail.value) return
  categoryKeys.value = [...(detail.value.categories ?? [])]
  categoryVisible.value = true
  if (categoryTree.value.length === 0 && !categoryLoading.value) void loadCategories()
}

function applyCategoryDialogKeys(): void {
  categoryTreeRef.value?.setCheckedKeys(categoryKeys.value)
}

function checkAllDialogCategories(): void {
  checkAllTree(categoryTreeRef.value)
}

function clearDialogCategories(): void {
  clearTree(categoryTreeRef.value)
}

function expandDialogCategories(): void {
  setTreeExpanded(categoryTreeRef.value, true)
}

function collapseDialogCategories(): void {
  setTreeExpanded(categoryTreeRef.value, false)
}

async function saveCategories(): Promise<void> {
  if (!detail.value) return
  const keys = readCheckedKeys(categoryTreeRef.value)
  savingCategories.value = true
  try {
    const result = await updateTemplateAdmin(detail.value.id, { categories: keys })
    ElMessage.success('模板分类已保存')
    showConfigWarnings(warningsOf(result))
    categoryVisible.value = false
    await loadDetail(detail.value.id)
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '保存模板分类失败', '修改文书模板'))
  } finally {
    savingCategories.value = false
  }
}

function openCategoryManager(): void {
  manageVisible.value = true
  if (categoryTree.value.length === 0 && !categoryLoading.value) void loadCategories()
}

function handleManageOpened(): void {
  setTreeExpanded(manageTreeRef.value, true)
}

function expandManageTree(expanded: boolean): void {
  setTreeExpanded(manageTreeRef.value, expanded)
}

function openRootCategoryDialog(): void {
  nodeDialogMode.value = 'create'
  nodeParent.value = null
  nodeEditing.value = null
  nodeLabel.value = ''
  nodeDialogVisible.value = true
}

function openChildCategoryDialog(node: TemplateCategoryNode): void {
  nodeDialogMode.value = 'create'
  nodeParent.value = node
  nodeEditing.value = null
  nodeLabel.value = ''
  nodeDialogVisible.value = true
}

function openRenameCategoryDialog(node: TemplateCategoryNode): void {
  if (!isCustomCategory(node)) return
  nodeDialogMode.value = 'rename'
  nodeParent.value = null
  nodeEditing.value = node
  nodeLabel.value = node.label
  nodeDialogVisible.value = true
}

async function afterCategoryMutation(): Promise<void> {
  await loadCategories()
  await loadTemplates()
  if (selectedId.value !== null) await loadDetail(selectedId.value)
  if (categoryVisible.value && detail.value) {
    categoryKeys.value = [...(detail.value.categories ?? [])]
    await nextTick()
    categoryTreeRef.value?.setCheckedKeys(categoryKeys.value)
  }
  if (manageVisible.value) {
    await nextTick()
    setTreeExpanded(manageTreeRef.value, true)
  }
}

async function submitCategoryNode(): Promise<void> {
  if (nodeSaving.value) return
  const label = nodeLabel.value.trim()
  if (!label) {
    ElMessage.warning('请输入分类名称')
    return
  }

  if (nodeDialogMode.value === 'rename') {
    const node = nodeEditing.value
    const id = node?.id
    if (!node || !isCustomCategory(node) || typeof id !== 'number') return
    nodeSaving.value = true
    try {
      await updateTemplateCategory(id, { label })
      ElMessage.success('分类已重命名')
      nodeDialogVisible.value = false
      await afterCategoryMutation()
    } catch (error) {
      ElMessage.error(actionError(error, '重命名分类失败', '管理模板分类'))
    } finally {
      nodeSaving.value = false
    }
    return
  }

  nodeSaving.value = true
  try {
    await createTemplateCategory({
      parentPath: nodeParent.value?.path ?? null,
      label,
    })
    ElMessage.success('分类已新增')
    nodeDialogVisible.value = false
    await afterCategoryMutation()
  } catch (error) {
    ElMessage.error(actionError(error, '新增分类失败', '管理模板分类'))
  } finally {
    nodeSaving.value = false
  }
}

async function removeCategoryNode(node: TemplateCategoryNode): Promise<void> {
  if (!isCustomCategory(node) || typeof node.id !== 'number') {
    ElMessage.warning('内置分类不可删除')
    return
  }
  if ((node.children ?? []).length > 0) {
    ElMessage.warning('请先删除该分类下的子分类')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定删除分类「${node.label}」吗？删除后不可恢复。`,
      '确认删除分类',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  const id = node.id
  categoryMutating.value = true
  try {
    await deleteTemplateCategory(id)
    ElMessage.success('分类已删除')
    await afterCategoryMutation()
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      if (error.message.includes('子分类')) {
        ElMessage.error(error.message)
        return
      }
      try {
        await ElMessageBox.confirm(
          `${error.message}\n\n是否强制移除引用该分类的模板配置，并删除此分类？`,
          '分类仍被模板引用',
          {
            confirmButtonText: '强制移除并删除',
            cancelButtonText: '取消',
            type: 'warning',
          },
        )
      } catch {
        return
      }
      try {
        await deleteTemplateCategory(id, true)
        ElMessage.success('分类及模板引用已强制移除')
        await afterCategoryMutation()
      } catch (forceError) {
        ElMessage.error(actionError(forceError, '强制删除分类失败', '管理模板分类'))
      }
      return
    }
    ElMessage.error(actionError(error, '删除分类失败', '管理模板分类'))
  } finally {
    categoryMutating.value = false
  }
}

async function moveTemplate(direction: TemplateMoveDirection): Promise<void> {
  if (!detail.value) return
  const id = detail.value.id
  moving.value = direction
  try {
    await moveTemplateAdmin(id, direction)
    ElMessage.success(direction === 'up' ? '模板已上移' : '模板已下移')
    await loadTemplates()
    if (selectedId.value === id) await loadDetail(id)
  } catch (error) {
    ElMessage.error(actionError(error, '调整模板排序失败', '修改文书模板'))
  } finally {
    moving.value = null
  }
}

async function removeTemplate(): Promise<void> {
  if (!detail.value) return
  const current = detail.value
  try {
    await ElMessageBox.confirm(
      `确定删除模板「${current.name}」吗？\n删除后不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deleting.value = true
  try {
    await deleteTemplateAdmin(current.id)
    ElMessage.success('模板已删除')
    selectedId.value = null
    detail.value = null
    detailError.value = ''
    await loadTemplates()
  } catch (error) {
    ElMessage.error(actionError(error, '删除模板失败', '删除文书模板'))
  } finally {
    deleting.value = false
  }
}

function goBack(): void {
  void router.push('/archives/query')
}

onMounted(() => {
  void loadCategories()
  void loadDocTypes()
  void loadTemplates()
})
</script>

<template>
  <div class="nc-page template-page">
    <NcPageHeader
      title="文书模板管理"
      description="维护档案文书模板、文档类型、适用分类与字段映射配置"
      kicker="救助档案 · 文书模板"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回档案查询</el-button>
        <el-button
          :icon="Refresh"
          :loading="listLoading || detailLoading"
          @click="refreshAll()"
        >
          刷新
        </el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="sm" class="toolbar-card">
      <form class="toolbar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="toolbar-search"
          clearable
          :prefix-icon="Search"
          :disabled="mode === 'upload'"
          placeholder="搜索模板名称 / 文档类型"
          @clear="handleSearch"
        />
        <el-button
          type="primary"
          :icon="Search"
          native-type="submit"
          :loading="listLoading"
          :disabled="mode === 'upload'"
        >
          搜索
        </el-button>
        <el-button v-if="keyword && mode === 'list'" text @click="resetSearch">重置</el-button>

        <div class="toolbar-spacer" />

        <el-button type="primary" :icon="Plus" :disabled="mode === 'upload'" @click="openUpload">
          新增模板
        </el-button>
        <el-button :icon="CollectionTag" @click="openCategoryManager">分类管理</el-button>
        <el-button
          :icon="Back"
          :disabled="mode === 'list' && selectedId === null"
          @click="backToList"
        >
          返回列表
        </el-button>
      </form>

      <div v-if="mode === 'list'" class="filter-bar">
        <span class="filter-label">筛选</span>
        <el-select
          v-model="filterCategory"
          class="filter-select filter-select--wide"
          clearable
          filterable
          placeholder="全部分类"
        >
          <el-option
            v-for="path in categoryFilterOptions"
            :key="path"
            :label="path"
            :value="path"
          />
        </el-select>
        <el-select
          v-model="filterDocType"
          class="filter-select"
          clearable
          filterable
          placeholder="全部文档类型"
        >
          <el-option
            v-for="type in docTypeFilterOptions"
            :key="type"
            :label="type"
            :value="type"
          />
        </el-select>
        <el-select
          v-model="filterFileType"
          class="filter-select"
          clearable
          placeholder="全部文件类型"
        >
          <el-option label="Word 文档" value="word" />
          <el-option label="Excel 表格" value="excel" />
          <el-option label="其他" value="other" />
        </el-select>
        <el-button v-if="hasFilters" link type="primary" @click="clearFilters">
          清除筛选
        </el-button>
        <span class="filter-count muted">
          显示 {{ filteredTemplates.length }} / {{ templates.length }}
        </span>
      </div>
    </NcCard>

    <el-alert
      v-if="listError && mode === 'list'"
      class="load-error"
      type="error"
      :title="listError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadTemplates()">重新加载</el-button>
    </el-alert>

    <NcCard v-if="mode === 'upload'" title="新增文书模板" class="upload-card">
      <template #actions>
        <NcStatusDot status="default" label="新建模板" />
      </template>

      <el-alert
        v-if="docTypesError"
        class="inline-alert"
        type="warning"
        :title="docTypesError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadDocTypes()">重新加载文档类型</el-button>
      </el-alert>

      <div class="form-grid">
        <div class="form-field form-field--full">
          <span class="field-label">模板文件 <em>*</em></span>
          <input
            ref="uploadFileInput"
            class="file-input"
            type="file"
            accept=".docx,.xlsx"
            @change="onUploadFileChange"
          />
          <div class="file-row">
            <div class="file-box">
              <span v-if="uploadFile" class="file-name">{{ uploadFile.name }}</span>
              <span v-else class="file-placeholder">未选择文件（支持 .docx / .xlsx）</span>
            </div>
            <el-button :icon="Upload" @click="pickUploadFile">选择文件</el-button>
          </div>
        </div>

        <div class="form-field">
          <label class="field-label" for="template-name">
            模板名称 <em>*</em>
          </label>
          <el-input
            id="template-name"
            v-model="uploadName"
            maxlength="120"
            show-word-limit
            placeholder="请输入模板名称"
          />
        </div>

        <div class="form-field">
          <label class="field-label" for="template-doc-type">
            文档类型 <em>*</em>
          </label>
          <el-select
            id="template-doc-type"
            v-model="uploadDocType"
            class="full-width"
            filterable
            clearable
            :loading="docTypesLoading"
            placeholder="请选择文档类型"
          >
            <el-option
              v-for="type in docTypes"
              :key="type"
              :label="type"
              :value="type"
            />
          </el-select>
        </div>

        <div class="form-field form-field--full">
          <span class="field-label">适用分类（可多选，支持任意层级独立勾选）</span>
          <div class="tree-tools">
            <el-button link type="primary" @click="expandUploadCategories">展开全部</el-button>
            <el-button link @click="collapseUploadCategories">收起全部</el-button>
            <el-button link type="primary" @click="checkAllUploadCategories">全选</el-button>
            <el-button link @click="clearUploadCategories">清空</el-button>
            <el-button link type="primary" @click="openCategoryManager">管理分类</el-button>
            <span class="muted">保存时保留父分类路径</span>
          </div>
          <el-alert
            v-if="categoryError"
            class="inline-alert"
            type="warning"
            :title="categoryError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadCategories()">重新加载分类树</el-button>
          </el-alert>
          <div v-loading="categoryLoading" class="tree-box">
            <el-tree
              ref="uploadTreeRef"
              :data="categoryTree"
              :props="categoryTreeProps"
              node-key="path"
              show-checkbox
              check-strictly
              default-expand-all
              :empty-text="categoryError ? '分类加载失败，请重试' : '暂无分类'"
            />
          </div>
        </div>

        <div class="form-field form-field--full">
          <span class="field-label">字段映射 JSON（可选）</span>
          <el-input
            v-model="uploadConfigJson"
            type="textarea"
            :rows="6"
            class="config-input"
            placeholder='可选，例：{ "copies": 1, "fields": [] }'
          />
          <p class="field-hint">上传前会校验 JSON 格式；留空表示不配置字段映射。</p>
        </div>
      </div>

      <div class="form-actions form-actions--sticky">
        <el-button :disabled="uploadSubmitting" @click="backToList">取消</el-button>
        <el-button type="primary" :loading="uploadSubmitting" @click="submitUpload">
          上传模板
        </el-button>
      </div>
    </NcCard>

    <div v-else class="browse-grid" :class="{ 'browse-grid--detail': selectedId !== null }">
      <NcCard padding="none" class="list-card" title="模板列表">
        <template #actions>
          <NcStatusDot
            status="default"
            :label="
              hasFilters
                ? `显示 ${filteredTemplates.length} / ${templates.length} 个`
                : `共 ${templates.length} 个`
            "
          />
        </template>

        <div v-loading="listLoading && templates.length > 0" class="list-area">
          <el-skeleton v-if="listLoading && templates.length === 0" :rows="7" animated class="list-skeleton" />

          <div v-else-if="filteredTemplates.length > 0" class="template-table">
            <div class="template-table__head">
              <span>模板名称</span>
              <span>文件类型</span>
              <span>文档类型</span>
              <span>分类</span>
              <span class="sort-head">排序</span>
            </div>

            <button
              v-for="item in filteredTemplates"
              :key="item.id"
              type="button"
              class="template-row"
              :class="{ 'is-active': selectedId === item.id }"
              @click="selectTemplate(item)"
            >
              <span class="template-name" :title="item.name">{{ item.name }}</span>
              <span class="file-chip">{{ fileTypeLabel(item.fileType) }}</span>
              <span class="doc-type" :title="item.docType">{{ item.docType || '—' }}</span>
              <span class="category-tags">
                <span
                  v-for="path in (item.categories ?? []).slice(0, 3)"
                  :key="path"
                  class="tag"
                  :title="path"
                >
                  {{ categoryLabel(path) }}
                </span>
                <span v-if="(item.categories ?? []).length > 3" class="tag tag--more">
                  +{{ (item.categories ?? []).length - 3 }}
                </span>
                <span v-if="(item.categories ?? []).length === 0" class="muted">未分类</span>
              </span>
              <span class="sort-order">{{ item.sortOrder }}</span>
            </button>
          </div>

          <div v-else-if="!listLoading && !listError" class="empty-wrap">
            <el-empty
              :description="templates.length > 0 ? '没有符合筛选条件的模板' : '暂无文书模板'"
              :image-size="72"
            >
              <el-button v-if="hasFilters" type="primary" plain @click="clearFilters">
                清除筛选
              </el-button>
              <p v-else class="empty-hint">点击「新增模板」添加第一份文书模板。</p>
            </el-empty>
          </div>
        </div>
      </NcCard>

      <aside v-if="selectedId !== null" class="detail-column">
        <NcCard title="模板详情" class="detail-card">
          <template #actions>
            <NcStatusDot
              v-if="detail"
              status="default"
              :label="`排序 ${detail.sortOrder}`"
            />
          </template>

          <el-skeleton v-if="detailLoading" :rows="8" animated />

          <el-alert
            v-else-if="detailError"
            type="error"
            :title="detailError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="selectedId && loadDetail(selectedId)">
              重新加载
            </el-button>
          </el-alert>

          <div v-else-if="detail" class="detail-body">
            <header class="detail-head">
              <h3>{{ detail.name }}</h3>
              <div class="detail-chips">
                <span class="file-chip">{{ fileTypeLabel(detail.fileType) }}</span>
                <span class="chip chip--seal">{{ detail.docType || '未设文档类型' }}</span>
              </div>
            </header>

            <div class="detail-actions detail-actions--top">
              <el-button :icon="Download" :loading="downloading" @click="downloadFile">
                下载
              </el-button>
              <el-button
                :icon="UploadFilled"
                :loading="changingFile"
                @click="chooseReplaceFile"
              >
                变更文件
              </el-button>
              <el-button :icon="Edit" @click="openConfigEdit">修改字段映射</el-button>
              <el-button :icon="CollectionTag" @click="openCategoryEdit">修改分类</el-button>
              <el-button
                :icon="Top"
                :loading="moving === 'up'"
                :disabled="moving !== null"
                @click="moveTemplate('up')"
              >
                上移
              </el-button>
              <el-button
                :icon="Bottom"
                :loading="moving === 'down'"
                :disabled="moving !== null"
                @click="moveTemplate('down')"
              >
                下移
              </el-button>
              <el-button
                type="danger"
                plain
                :icon="Delete"
                :loading="deleting"
                @click="removeTemplate"
              >
                删除
              </el-button>
            </div>

            <el-alert
              v-if="detailWarnings.length > 0"
              class="inline-alert"
              type="warning"
              :closable="false"
              show-icon
            >
              <template #title>配置提示（{{ detailWarnings.length }}）</template>
              <ul class="warning-list">
                <li v-for="(warning, index) in detailWarnings.slice(0, 12)" :key="index">
                  {{ warning }}
                </li>
                <li v-if="detailWarnings.length > 12" class="warning-more">
                  …另有 {{ detailWarnings.length - 12 }} 项（仅提示，不影响保存）
                </li>
              </ul>
            </el-alert>

            <dl class="meta-grid">
              <div>
                <dt>文件类型</dt>
                <dd>{{ fileTypeLabel(detail.fileType) }}</dd>
              </div>
              <div>
                <dt>文档类型</dt>
                <dd>{{ detail.docType || '—' }}</dd>
              </div>
              <div>
                <dt>默认份数</dt>
                <dd>{{ resolveCopies(detail) }} 份</dd>
              </div>
              <div>
                <dt>排序</dt>
                <dd>{{ detail.sortOrder }}</dd>
              </div>
              <div>
                <dt>创建时间</dt>
                <dd>{{ formatDateTimeText(detail.createdAt) }}</dd>
              </div>
              <div>
                <dt>更新时间</dt>
                <dd>{{ formatDateTimeText(detail.updatedAt) }}</dd>
              </div>
              <div class="meta-full">
                <dt>分类</dt>
                <dd class="category-tags">
                  <span
                    v-for="path in (detail.categories ?? [])"
                    :key="path"
                    class="tag"
                    :title="path"
                  >
                    {{ categoryLabel(path) }}
                  </span>
                  <span v-if="(detail.categories ?? []).length === 0" class="muted">
                    未设置分类
                  </span>
                </dd>
              </div>
            </dl>

            <div class="doc-type-editor">
              <span class="field-label">文档类型</span>
              <el-alert
                v-if="docTypesError"
                class="inline-alert"
                type="warning"
                :title="docTypesError"
                :closable="false"
                show-icon
              >
                <el-button link type="primary" @click="loadDocTypes()">重新加载文档类型</el-button>
              </el-alert>
              <div class="copies-row">
                <el-select
                  v-model="detailDocType"
                  class="doc-type-select"
                  filterable
                  clearable
                  :loading="docTypesLoading"
                  placeholder="请选择文档类型"
                >
                  <el-option
                    v-for="type in detailDocTypeOptions"
                    :key="type"
                    :label="type"
                    :value="type"
                  />
                </el-select>
                <el-button
                  type="primary"
                  plain
                  :loading="savingDocType"
                  :disabled="!detailDocType"
                  @click="saveDocType"
                >
                  保存文档类型
                </el-button>
              </div>
            </div>

            <div class="copies-editor">
              <span class="field-label">默认份数（1-99）</span>
              <p class="field-hint copies-hint">
                {{
                  classificationCopiesCount > 0
                    ? `该模板已配置 ${classificationCopiesCount} 项分类级份数（分类命中时优先）`
                    : '未配置分类级份数'
                }}
              </p>
              <div class="copies-row">
                <el-input-number
                  v-model="copies"
                  class="copies-input"
                  :min="1"
                  :max="99"
                  :step="1"
                  controls-position="right"
                />
                <el-button type="primary" plain :loading="savingCopies" @click="saveCopies">
                  保存默认份数
                </el-button>
              </div>
            </div>

            <div class="config-block">
              <div class="config-head">
                <span class="field-label">config_json</span>
                <el-button link type="primary" :icon="Edit" @click="openConfigEdit">
                  修改字段映射
                </el-button>
              </div>
              <pre v-if="(detail.configJson ?? '').trim()">{{ prettyConfig(detail.configJson) }}</pre>
              <p v-else class="muted config-empty">未配置字段映射</p>
            </div>

            <input
              ref="replaceFileInput"
              class="file-input"
              type="file"
              accept=".docx,.xlsx"
              @change="onReplaceFileChange"
            />
          </div>
        </NcCard>
      </aside>
    </div>

    <el-dialog
      v-model="configVisible"
      title="修改字段映射"
      :width="isNarrow ? '92%' : '640px'"
      append-to-body
      destroy-on-close
    >
      <p class="dialog-hint">
        编辑模板的 config_json。保存前会执行 JSON.parse 校验；留空表示清除配置。
      </p>
      <el-input
        v-model="configDraft"
        type="textarea"
        :rows="14"
        class="config-input"
        placeholder='例：{ "copies": 1, "fields": [] }'
      />
      <template #footer>
        <el-button :disabled="savingConfig" @click="configVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingConfig" @click="saveConfig">
          保存配置
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="categoryVisible"
      title="修改模板分类"
      :width="isNarrow ? '92%' : '600px'"
      append-to-body
      destroy-on-close
      @opened="applyCategoryDialogKeys"
    >
      <p class="dialog-hint">
        勾选模板适用的业务分类；支持任意层级独立勾选，保存时保留父分类路径。
      </p>
      <div class="tree-tools">
        <el-button link type="primary" @click="expandDialogCategories">展开全部</el-button>
        <el-button link @click="collapseDialogCategories">收起全部</el-button>
        <el-button link type="primary" @click="checkAllDialogCategories">全选</el-button>
        <el-button link @click="clearDialogCategories">清空</el-button>
        <el-button link type="primary" @click="openCategoryManager">管理分类</el-button>
      </div>
      <el-alert
        v-if="categoryError"
        class="inline-alert"
        type="warning"
        :title="categoryError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadCategories()">重新加载分类树</el-button>
      </el-alert>
      <div v-loading="categoryLoading" class="tree-box">
        <el-tree
          ref="categoryTreeRef"
          :data="categoryTree"
          :props="categoryTreeProps"
          node-key="path"
          show-checkbox
          check-strictly
          default-expand-all
          :default-checked-keys="categoryKeys"
          :empty-text="categoryError ? '分类加载失败，请重试' : '暂无分类'"
        />
      </div>
      <template #footer>
        <el-button :disabled="savingCategories" @click="categoryVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingCategories" @click="saveCategories">
          保存分类
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="manageVisible"
      title="分类节点管理"
      :width="isNarrow ? '94%' : '720px'"
      append-to-body
      destroy-on-close
      @opened="handleManageOpened"
    >
      <div class="manage-toolbar">
        <el-button
          type="primary"
          :icon="Plus"
          :disabled="categoryMutating"
          @click="openRootCategoryDialog"
        >
          新增一级分类
        </el-button>
        <el-button @click="expandManageTree(true)">展开全部</el-button>
        <el-button @click="expandManageTree(false)">收起全部</el-button>
        <el-button :icon="Refresh" :loading="categoryLoading" @click="loadCategories()">
          刷新
        </el-button>
      </div>
      <p class="dialog-hint">
        内置分类不可改名 / 删除，但可在其下新增子分类；自定义分类可新增子分类、重命名、删除。
      </p>
      <el-alert
        v-if="categoryError"
        class="inline-alert"
        type="warning"
        :title="categoryError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadCategories()">重新加载分类树</el-button>
      </el-alert>
      <div v-loading="categoryLoading" class="tree-box tree-box--manage">
        <el-tree
          ref="manageTreeRef"
          :data="categoryTree"
          :props="categoryTreeProps"
          node-key="path"
          default-expand-all
          :expand-on-click-node="false"
          :empty-text="categoryError ? '分类加载失败，请重试' : '暂无分类'"
        >
          <template #default="{ data }">
            <div class="tree-node">
              <span class="tree-node__label">
                <span class="tree-node__text" :title="data.path">{{ data.label }}</span>
                <em
                  v-if="isCustomCategory(data)"
                  class="tree-node__badge tree-node__badge--custom"
                >
                  自定义
                </em>
                <em v-else class="tree-node__badge">内置</em>
              </span>
              <span class="tree-node__actions">
                <el-button
                  link
                  type="primary"
                  size="small"
                  :disabled="categoryMutating"
                  @click.stop="openChildCategoryDialog(data)"
                >
                  新增子分类
                </el-button>
                <template v-if="isCustomCategory(data)">
                  <el-button
                    link
                    size="small"
                    :disabled="categoryMutating"
                    @click.stop="openRenameCategoryDialog(data)"
                  >
                    重命名
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    size="small"
                    :disabled="categoryMutating"
                    @click.stop="removeCategoryNode(data)"
                  >
                    删除
                  </el-button>
                </template>
              </span>
            </div>
          </template>
        </el-tree>
      </div>
      <template #footer>
        <el-button @click="manageVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="nodeDialogVisible"
      :title="nodeDialogMode === 'rename' ? '重命名分类' : '新增分类'"
      :width="isNarrow ? '92%' : '460px'"
      append-to-body
      destroy-on-close
    >
      <p v-if="nodeDialogMode === 'rename'" class="dialog-hint">
        原分类：{{ nodeEditing?.path }}
      </p>
      <p v-else class="dialog-hint">
        上级分类：{{ nodeParent ? nodeParent.path : '（无，新增为一级分类）' }}
      </p>
      <el-form label-position="top" @submit.prevent="submitCategoryNode">
        <el-form-item label="分类名称">
          <el-input
            v-model="nodeLabel"
            maxlength="120"
            show-word-limit
            placeholder="请输入分类名称"
            @keyup.enter="submitCategoryNode"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="nodeSaving" @click="nodeDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="nodeSaving" @click="submitCategoryNode">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.template-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.toolbar-card,
.list-card {
  overflow: hidden;
}

.upload-card {
  /* sticky 提交栏需要非滚动祖先，不能使用 overflow: hidden */
  overflow: visible;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 4px 2px;
}

.toolbar-search {
  flex: 1 1 260px;
  max-width: 420px;
}

.toolbar-spacer {
  flex: 1;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line-faint);
}

.filter-label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.filter-select {
  width: 150px;
}

.filter-select--wide {
  width: 220px;
}

.filter-count {
  margin-left: auto;
}

.load-error {
  margin: 0;
}

.inline-alert {
  margin: 0 0 10px;
}

.browse-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 15px;
  align-items: start;
}

.browse-grid--detail {
  grid-template-columns: minmax(0, 1fr) minmax(340px, 430px);
}

.list-area {
  min-height: 220px;
  padding: 10px;
}

.list-skeleton {
  padding: 12px;
}

.template-table {
  display: flex;
  flex-direction: column;
}

.template-table__head,
.template-row {
  display: grid;
  grid-template-columns: minmax(0, 1.5fr) 104px 126px minmax(0, 1.25fr) 56px;
  gap: 12px;
  align-items: center;
}

.template-table__head {
  padding: 6px 12px 8px;
  border-bottom: 1px solid var(--nc-line);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.sort-head {
  text-align: right;
}

.template-row {
  width: 100%;
  padding: 12px;
  border: none;
  border-bottom: 1px solid var(--nc-line-faint);
  border-radius: 0;
  background: transparent;
  color: inherit;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.template-row:last-child {
  border-bottom: none;
}

.template-row:hover {
  background-color: var(--nc-paper-3);
}

.template-row.is-active {
  background-color: var(--nc-seal-wash);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.template-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-chip,
.chip {
  display: inline-block;
  padding: 1px 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: 10px;
  font-weight: 600;
  line-height: 1.7;
  white-space: nowrap;
}

.chip--seal {
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
}

.doc-type {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-tags {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  min-width: 0;
}

.tag {
  padding: 0 6px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted-2);
  font-size: 10px;
  line-height: 1.7;
  white-space: nowrap;
}

.tag--more {
  border-color: transparent;
  background-color: var(--nc-paper-3);
}

.sort-order {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  text-align: right;
}

.muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.empty-wrap {
  padding: 14px 8px;
}

.empty-hint {
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

/* 详情列不再 sticky，详情卡自身限高滚动，避免底部操作被视口裁掉 */
.detail-column {
  min-width: 0;
}

.detail-card {
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 32px);
}

.detail-card :deep(.nc-card__head) {
  flex: none;
}

.detail-card :deep(.nc-card__body) {
  min-height: 0;
  overflow-y: auto;
}

.detail-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.detail-head h3 {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.03em;
}

.detail-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 7px;
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 9px 14px;
  margin: 0;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.meta-grid div {
  min-width: 0;
}

.meta-grid .meta-full {
  grid-column: 1 / -1;
}

.meta-grid dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.meta-grid dd {
  margin: 2px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.meta-grid dd.category-tags {
  overflow: visible;
  white-space: normal;
}

.copies-editor,
.doc-type-editor {
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.copies-hint {
  margin: 4px 0 0;
}

.copies-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 7px;
}

.copies-input {
  width: 130px;
}

.doc-type-select {
  width: 100%;
  max-width: 260px;
}

.copies-row :deep(.el-button) {
  margin-left: 0;
}

.warning-list {
  max-height: 180px;
  margin: 4px 0 0;
  padding-left: 18px;
  overflow: auto;
}

.warning-list li {
  line-height: 1.7;
}

.warning-list .warning-more {
  color: var(--nc-muted);
}

.config-block {
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.config-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.config-block pre {
  max-height: 260px;
  margin: 0;
  padding: 10px 12px;
  overflow: auto;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
}

.config-empty {
  margin: 0;
  padding: 12px;
}

.detail-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.detail-actions--top {
  position: sticky;
  top: 0;
  z-index: 2;
  padding-top: 0;
  padding-bottom: 12px;
  border-top: none;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-2);
}

.detail-actions :deep(.el-button) {
  margin-left: 0;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 18px;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 18px;
  min-width: 0;
}

.form-field--full {
  grid-column: 1 / -1;
}

.field-label {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.field-label em {
  font-style: normal;
  color: var(--nc-seal);
}

.field-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.full-width {
  width: 100%;
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
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-name {
  color: var(--nc-ink);
}

.file-placeholder {
  color: var(--nc-muted);
}

.tree-tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.tree-tools :deep(.el-button + .el-button) {
  margin-left: 0;
}

.tree-box {
  max-height: 300px;
  padding: 8px;
  overflow: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.tree-box--manage {
  max-height: 460px;
}

.tree-box :deep(.el-tree-node__label) {
  flex: 1;
  min-width: 0;
}

.config-input :deep(.el-textarea__inner) {
  font-family: var(--nc-font-mono);
  font-size: 12px;
  line-height: 1.6;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.form-actions--sticky {
  position: sticky;
  bottom: 0;
  z-index: 3;
  margin: 6px -22px -20px;
  padding: 12px 22px;
  border-top: 1px solid var(--nc-line);
  border-radius: 0 0 var(--nc-radius-sm) var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: 0 -6px 12px -10px rgb(0 0 0 / 28%);
}

.form-actions :deep(.el-button) {
  margin-left: 0;
}

.dialog-hint {
  margin: 0 0 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.manage-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 10px;
}

.manage-toolbar :deep(.el-button) {
  margin-left: 0;
}

.tree-node {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  width: 100%;
  min-width: 0;
  padding: 1px 0;
}

.tree-node__label {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.tree-node__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-node__badge {
  flex: none;
  padding: 0 4px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-muted);
  font-size: 9px;
  font-style: normal;
  line-height: 1.6;
}

.tree-node__badge--custom {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.tree-node__actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 2px;
  opacity: 0;
  transition: opacity var(--nc-transition);
}

.tree-node__actions :deep(.el-button) {
  margin-left: 0;
}

:deep(.el-tree-node__content:hover) .tree-node__actions,
.tree-node:focus-within .tree-node__actions {
  opacity: 1;
}

@media (max-width: 1280px) {
  .browse-grid--detail {
    grid-template-columns: minmax(0, 1fr) 340px;
  }

  .template-table__head,
  .template-row {
    grid-template-columns: minmax(0, 1.5fr) 80px 100px minmax(0, 1.25fr) 40px;
    gap: 8px;
  }
}

@media (max-width: 1100px) {
  .browse-grid--detail {
    grid-template-columns: minmax(0, 1fr);
  }

  .detail-card {
    display: block;
    max-height: none;
  }

  .detail-card :deep(.nc-card__body) {
    overflow-y: visible;
  }

  .detail-actions--top {
    position: static;
  }

  .tree-node__actions {
    opacity: 1;
  }
}

@media (max-width: 767px) {
  .toolbar {
    padding: 0;
  }

  .toolbar-search {
    flex-basis: 100%;
    max-width: none;
  }

  .toolbar-spacer {
    display: none;
  }

  .toolbar :deep(.el-button) {
    flex: 1;
  }

  .filter-select,
  .filter-select--wide {
    flex: 1 1 100%;
    width: 100%;
  }

  .filter-count {
    margin-left: 0;
  }

  .template-table__head {
    display: none;
  }

  .template-row {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      'name file'
      'doc doc'
      'tags tags'
      'sort sort';
    gap: 7px 10px;
    padding: 12px 10px;
  }

  .template-row .template-name {
    grid-area: name;
  }

  .template-row .file-chip {
    grid-area: file;
    justify-self: end;
  }

  .template-row .doc-type {
    grid-area: doc;
  }

  .template-row .category-tags {
    grid-area: tags;
  }

  .template-row .sort-order {
    grid-area: sort;
    text-align: left;
  }

  .sort-order::before {
    content: '排序 ';
    color: var(--nc-muted);
    font-family: var(--nc-font-sans);
    font-size: 10px;
  }

  .meta-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-actions {
    flex-direction: column-reverse;
  }

  .form-actions--sticky {
    margin: 6px -15px -16px;
    padding: 12px 15px;
  }

  .form-actions :deep(.el-button) {
    width: 100%;
  }

  .file-row {
    flex-wrap: wrap;
  }

  .file-box {
    flex-basis: 100%;
  }

  .detail-actions :deep(.el-button) {
    flex: 1 1 calc(50% - 8px);
  }
}
</style>
