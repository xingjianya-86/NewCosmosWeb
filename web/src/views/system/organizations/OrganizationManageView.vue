<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, Refresh, Search, Upload } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  createOrganization,
  deleteOrganization,
  getOrganization,
  getOrganizationTree,
  importOrganizations,
  listOrganizationOptions,
  previewOrganizationImport,
  updateOrganization,
} from '@/api/organizations'
import { getCities, getCounties, getProvinces, getTowns, getVillages } from '@/api/regions'
import type { RegionOption } from '@/api/regions'
import { getMyPermissions } from '@/api/users'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  OrganizationDetail,
  OrganizationImportResult,
  OrganizationOption,
  OrganizationOptions,
  OrganizationPreview,
  OrganizationSaveInput,
  OrganizationStats,
  OrganizationTreeNode,
} from '@/types/organizations'

type RegionLevel = 'province' | 'city' | 'county' | 'town' | 'village'

interface TreeExpose {
  filter(value: string): void
  setCurrentKey(key?: number | null): void
}

const REGION_LEVELS: RegionLevel[] = ['province', 'city', 'county', 'town', 'village']
const REGION_LABELS: Record<RegionLevel, string> = {
  province: '省份',
  city: '地级市',
  county: '县/区',
  town: '乡镇/街道',
  village: '村/社区',
}
const IMPORT_EXTENSIONS = ['xlsx', 'xls', 'csv', 'tsv', 'txt']

const isNarrow = useMediaQuery('(max-width: 960px)')
const treeRef = ref<TreeExpose | null>(null)

/* ───────────────────────────── 组织树与详情 ───────────────────────────── */

const tree = ref<OrganizationTreeNode[]>([])
const EMPTY_STATS: OrganizationStats = {
  totalOrganizations: 0,
  totalUsers: 0,
  parentOrganizations: 0,
  rootOrganizations: 0,
}
const stats = ref<OrganizationStats>({ ...EMPTY_STATS })
const treeLoading = ref(false)
const treeError = ref('')
const searchText = ref('')

const selectedId = ref<number | null>(null)
const detail = ref<OrganizationDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
let detailTicket = 0

const detailRegion = computed(() => {
  const current = detail.value
  if (!current) return '—'
  const parts = [
    current.provinceName,
    current.cityName,
    current.countyName,
    current.townName,
    current.villageName,
  ].filter((item) => Boolean(item && item.trim()))
  return parts.length > 0 ? parts.join(' / ') : '未配置'
})

const treeProps = { label: 'name', children: 'children' }

function findNode(nodes: OrganizationTreeNode[], id: number): OrganizationTreeNode | null {
  for (const node of nodes) {
    if (node.id === id) return node
    const child = findNode(node.children ?? [], id)
    if (child) return child
  }
  return null
}

function collectSubtreeIds(node: OrganizationTreeNode | null, target: Set<number>): void {
  if (!node) return
  target.add(node.id)
  for (const child of node.children ?? []) collectSubtreeIds(child, target)
}

async function loadTree(preferId?: number | null): Promise<void> {
  treeLoading.value = true
  treeError.value = ''
  try {
    const data = await getOrganizationTree()
    tree.value = data.nodes ?? []
    stats.value = data.stats ?? { ...EMPTY_STATS }

    await nextTick()
    treeRef.value?.filter(searchText.value)

    const targetId = preferId !== undefined ? preferId : selectedId.value
    if (targetId !== null && findNode(tree.value, targetId)) {
      selectedId.value = targetId
      treeRef.value?.setCurrentKey(targetId)
      await loadDetail(targetId)
    } else if (targetId !== null) {
      selectedId.value = null
      detail.value = null
      detailError.value = ''
    }
  } catch (loadError) {
    treeError.value = getErrorMessage(loadError, '组织树加载失败')
    tree.value = []
  } finally {
    treeLoading.value = false
  }
}

async function loadDetail(id: number): Promise<void> {
  const ticket = ++detailTicket
  detailLoading.value = true
  detailError.value = ''
  try {
    const data = await getOrganization(id)
    if (ticket !== detailTicket) return
    detail.value = data
  } catch (loadError) {
    if (ticket !== detailTicket) return
    detail.value = null
    detailError.value = getErrorMessage(loadError, '机构详情加载失败')
  } finally {
    if (ticket === detailTicket) detailLoading.value = false
  }
}

function handleNodeClick(node: OrganizationTreeNode): void {
  selectedId.value = node.id
  detail.value = null
  void loadDetail(node.id)
}

function retryDetail(): void {
  if (selectedId.value === null) return
  detail.value = null
  void loadDetail(selectedId.value)
}

function filterTreeNode(value: string, data: Record<string, unknown>): boolean {
  const keyword = value.trim().toLowerCase()
  if (!keyword) return true
  const name = typeof data.name === 'string' ? data.name : ''
  const abbreviation = typeof data.abbreviation === 'string' ? data.abbreviation : ''
  return name.toLowerCase().includes(keyword) || abbreviation.toLowerCase().includes(keyword)
}

watch(searchText, (value) => {
  treeRef.value?.filter(value)
})

/* ───────────────────────────── 权限 ───────────────────────────── */

const permissions = ref<string[]>([])
const isSuperAdmin = ref(false)
const permissionsLoaded = ref(false)
const permissionsError = ref('')

function hasPermission(code: string): boolean {
  return permissionsLoaded.value && (isSuperAdmin.value || permissions.value.includes(code))
}

const canCreate = computed(() => hasPermission('ORG_CREATE'))
const canEdit = computed(() => hasPermission('ORG_EDIT'))
const canDelete = computed(() => hasPermission('ORG_DELETE'))

async function loadPermissions(): Promise<void> {
  try {
    const data = await getMyPermissions()
    permissions.value = data.permissions ?? []
    isSuperAdmin.value = data.isSuperAdmin
    permissionsLoaded.value = true
    permissionsError.value = ''
  } catch (loadError) {
    permissionsLoaded.value = false
    permissionsError.value = getErrorMessage(loadError, '权限信息加载失败，写操作已禁用')
  }
}

/* ───────────────────────────── 下拉选项 ───────────────────────────── */

const orgOptions = reactive<OrganizationOptions>({
  organizations: [],
  institutionNatures: [],
  institutionTypes: [],
  institutionCategories: [],
})
const optionsLoading = ref(false)
const optionsError = ref('')
let optionsLoaded = false

async function loadOrgOptions(force = false): Promise<void> {
  if (optionsLoading.value) return
  if (optionsLoaded && !force) return

  optionsLoading.value = true
  optionsError.value = ''
  try {
    const data = await listOrganizationOptions()
    orgOptions.organizations = data.organizations ?? []
    orgOptions.institutionNatures = data.institutionNatures ?? []
    orgOptions.institutionTypes = data.institutionTypes ?? []
    orgOptions.institutionCategories = data.institutionCategories ?? []
    optionsLoaded = true
  } catch (loadError) {
    optionsError.value = getErrorMessage(loadError, '机构下拉选项加载失败')
  } finally {
    optionsLoading.value = false
  }
}

/* ───────────────────────────── 编辑弹窗 ───────────────────────────── */

function emptyForm(): OrganizationSaveInput {
  return {
    name: '',
    abbreviation: '',
    principal: '',
    principalIdentityCard: '',
    phone: '',
    address: '',
    postalCode: '',
    parentId: null,
    institutionNature: '',
    institutionType: '',
    institutionCategory: '',
    isSpecialCareInstitution: false,
    provinceId: null,
    cityId: null,
    countyId: null,
    townId: null,
    villageId: null,
  }
}

const dialog = reactive({
  visible: false,
  editingId: null as number | null,
  saving: false,
  error: '',
})
const form = reactive<OrganizationSaveInput>(emptyForm())

const dialogTitle = computed(() => (dialog.editingId === null ? '新建机构' : '编辑机构'))
const dialogWidth = computed(() => (isNarrow.value ? '94%' : '780px'))

const parentCandidates = computed<OrganizationOption[]>(() => {
  const blocked = new Set<number>()
  if (dialog.editingId !== null) {
    blocked.add(dialog.editingId)
    collectSubtreeIds(findNode(tree.value, dialog.editingId), blocked)
  }
  return orgOptions.organizations.filter((item) => !blocked.has(item.id))
})

const regionOptions = reactive<Record<RegionLevel, RegionOption[]>>({
  province: [],
  city: [],
  county: [],
  town: [],
  village: [],
})
const regionLoading = reactive<Record<RegionLevel, boolean>>({
  province: false,
  city: false,
  county: false,
  town: false,
  village: false,
})
const regionTickets: Record<RegionLevel, number> = {
  province: 0,
  city: 0,
  county: 0,
  town: 0,
  village: 0,
}
const regionError = ref('')

function resetRegionOptions(): void {
  for (const level of REGION_LEVELS) {
    regionTickets[level] += 1
    regionOptions[level] = []
    regionLoading[level] = false
  }
  regionError.value = ''
}

async function loadRegionOptions(
  level: RegionLevel,
  parentId?: number | null,
): Promise<void> {
  const ticket = ++regionTickets[level]
  regionLoading[level] = true
  regionError.value = ''

  try {
    let rows: RegionOption[]
    if (level === 'province') {
      rows = await getProvinces()
    } else if (parentId == null || parentId <= 0) {
      regionOptions[level] = []
      return
    } else if (level === 'city') {
      rows = await getCities(parentId)
    } else if (level === 'county') {
      rows = await getCounties(parentId)
    } else if (level === 'town') {
      rows = await getTowns(parentId)
    } else {
      rows = await getVillages(parentId)
    }

    if (ticket === regionTickets[level]) {
      regionOptions[level] = rows
    }
  } catch (loadError) {
    if (ticket === regionTickets[level]) {
      regionOptions[level] = []
      regionError.value = getErrorMessage(
        loadError,
        `${REGION_LABELS[level]}加载失败，所在地区可能无法回显或选择`,
      )
    }
  } finally {
    if (ticket === regionTickets[level]) regionLoading[level] = false
  }
}

function ensureRegionOption(level: RegionLevel, id: number | null, name: string): void {
  if (id === null || id <= 0) return
  const list = regionOptions[level]
  if (list.some((item) => item.id === id)) return
  if (name && name.trim()) list.unshift({ id, name })
}

function clearRegionBelow(level: RegionLevel): void {
  if (level === 'province') {
    form.cityId = null
    form.countyId = null
    form.townId = null
    form.villageId = null
    regionOptions.city = []
    regionOptions.county = []
    regionOptions.town = []
    regionOptions.village = []
  } else if (level === 'city') {
    form.countyId = null
    form.townId = null
    form.villageId = null
    regionOptions.county = []
    regionOptions.town = []
    regionOptions.village = []
  } else if (level === 'county') {
    form.townId = null
    form.villageId = null
    regionOptions.town = []
    regionOptions.village = []
  } else if (level === 'town') {
    form.villageId = null
    regionOptions.village = []
  }
}

function onProvinceChange(value: number | null): void {
  clearRegionBelow('province')
  if (value != null) void loadRegionOptions('city', value)
}

function onCityChange(value: number | null): void {
  clearRegionBelow('city')
  if (value != null) void loadRegionOptions('county', value)
}

function onCountyChange(value: number | null): void {
  clearRegionBelow('county')
  if (value != null) void loadRegionOptions('town', value)
}

function onTownChange(value: number | null): void {
  clearRegionBelow('town')
  if (value != null) void loadRegionOptions('village', value)
}

function assignForm(source: OrganizationDetail): void {
  Object.assign(form, {
    name: source.name,
    abbreviation: source.abbreviation ?? '',
    principal: source.principal ?? '',
    principalIdentityCard: source.principalIdentityCard ?? '',
    phone: source.phone ?? '',
    address: source.address ?? '',
    postalCode: source.postalCode ?? '',
    parentId: source.parentId ?? null,
    institutionNature: source.institutionNature ?? '',
    institutionType: source.institutionType ?? '',
    institutionCategory: source.institutionCategory ?? '',
    isSpecialCareInstitution: source.isSpecialCareInstitution,
    provinceId: source.provinceId ?? null,
    cityId: source.cityId ?? null,
    countyId: source.countyId ?? null,
    townId: source.townId ?? null,
    villageId: source.villageId ?? null,
  })
}

async function openCreate(parentId: number | null): Promise<void> {
  if (!canCreate.value) {
    ElMessage.warning('当前账号缺少 ORG_CREATE 权限，无法新建机构')
    return
  }

  Object.assign(form, emptyForm())
  form.parentId = parentId
  dialog.editingId = null
  dialog.error = ''
  dialog.visible = true
  resetRegionOptions()
  void loadOrgOptions()
  await loadRegionOptions('province')
}

async function openEdit(): Promise<void> {
  if (!canEdit.value) {
    ElMessage.warning('当前账号缺少 ORG_EDIT 权限，无法编辑机构')
    return
  }
  const source = detail.value
  if (!source) return

  dialog.editingId = source.id
  dialog.error = ''
  assignForm(source)
  dialog.visible = true
  resetRegionOptions()
  void loadOrgOptions()

  await loadRegionOptions('province')
  ensureRegionOption('province', source.provinceId, source.provinceName)
  if (source.provinceId !== null) await loadRegionOptions('city', source.provinceId)
  ensureRegionOption('city', source.cityId, source.cityName)
  if (source.cityId !== null) await loadRegionOptions('county', source.cityId)
  ensureRegionOption('county', source.countyId, source.countyName)
  if (source.countyId !== null) await loadRegionOptions('town', source.countyId)
  ensureRegionOption('town', source.townId, source.townName)
  if (source.townId !== null) await loadRegionOptions('village', source.townId)
  ensureRegionOption('village', source.villageId, source.villageName)
}

function normalizeId(value: number | null): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : null
}

async function saveOrganization(): Promise<void> {
  if (dialog.saving) return

  const name = form.name.trim()
  if (!name) {
    dialog.error = '机构名称不能为空'
    return
  }

  const payload: OrganizationSaveInput = {
    name,
    abbreviation: form.abbreviation.trim(),
    principal: form.principal.trim(),
    principalIdentityCard: form.principalIdentityCard.trim(),
    phone: form.phone.trim(),
    address: form.address.trim(),
    postalCode: form.postalCode.trim(),
    parentId: normalizeId(form.parentId),
    institutionNature: form.institutionNature.trim(),
    institutionType: form.institutionType.trim(),
    institutionCategory: form.institutionCategory.trim(),
    isSpecialCareInstitution: form.isSpecialCareInstitution,
    provinceId: normalizeId(form.provinceId),
    cityId: normalizeId(form.cityId),
    countyId: normalizeId(form.countyId),
    townId: normalizeId(form.townId),
    villageId: normalizeId(form.villageId),
  }

  dialog.saving = true
  dialog.error = ''
  try {
    if (dialog.editingId === null) {
      const result = await createOrganization(payload)
      ElMessage.success(result.message || '机构创建成功')
      dialog.visible = false
      void loadOrgOptions(true)
      await loadTree(result.id)
    } else {
      const targetId = dialog.editingId
      const result = await updateOrganization(targetId, payload)
      ElMessage.success(result.message || '机构保存成功')
      dialog.visible = false
      void loadOrgOptions(true)
      await loadTree(targetId)
    }
  } catch (saveError) {
    dialog.error = getErrorMessage(
      saveError,
      dialog.editingId === null ? '创建机构失败' : '保存机构失败',
    )
  } finally {
    dialog.saving = false
  }
}

function handleDialogClosed(): void {
  dialog.error = ''
  resetRegionOptions()
}

/* ───────────────────────────── 删除 ───────────────────────────── */

const deleting = ref(false)

async function removeOrganization(): Promise<void> {
  const current = detail.value
  if (!current) return
  if (!canDelete.value) {
    ElMessage.warning('当前账号缺少 ORG_DELETE 权限，无法删除机构')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定删除机构「${current.name}」吗？存在下级机构或关联用户时无法删除。`,
      '删除机构',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deleting.value = true
  try {
    const result = await deleteOrganization(current.id)
    ElMessage.success(result.message || '机构已删除')
    selectedId.value = null
    detail.value = null
    detailError.value = ''
    void loadOrgOptions(true)
    await loadTree(null)
  } catch (deleteError) {
    try {
      await ElMessageBox.alert(getErrorMessage(deleteError, '删除机构失败'), '无法删除', {
        confirmButtonText: '知道了',
        type: 'error',
      })
    } catch {
      // 用户关闭提示框即可
    }
  } finally {
    deleting.value = false
  }
}

/* ───────────────────────────── 导入 ───────────────────────────── */

const importDialog = reactive({
  visible: false,
  previewing: false,
  importing: false,
  error: '',
  preview: null as OrganizationPreview | null,
  result: null as OrganizationImportResult | null,
})
const importFile = ref<File | null>(null)
const importInput = ref<HTMLInputElement | null>(null)

function resetImportState(): void {
  importFile.value = null
  importDialog.previewing = false
  importDialog.importing = false
  importDialog.error = ''
  importDialog.preview = null
  importDialog.result = null
  if (importInput.value) importInput.value.value = ''
}

function openImport(): void {
  if (!canCreate.value) {
    ElMessage.warning('当前账号缺少 ORG_CREATE 权限，无法导入机构')
    return
  }
  resetImportState()
  importDialog.visible = true
}

function pickImportFile(): void {
  importInput.value?.click()
}

function onImportFilePicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return

  const extension = file.name.split('.').pop()?.toLowerCase() ?? ''
  if (!IMPORT_EXTENSIONS.includes(extension)) {
    ElMessage.warning('仅支持 .xlsx / .xls / .csv / .tsv / .txt 文件')
    return
  }

  importFile.value = file
  importDialog.preview = null
  importDialog.result = null
  importDialog.error = ''
}

async function previewImport(): Promise<void> {
  const file = importFile.value
  if (!file) {
    ElMessage.warning('请先选择导入文件')
    return
  }

  importDialog.previewing = true
  importDialog.error = ''
  importDialog.result = null
  try {
    const data = await previewOrganizationImport(file)
    importDialog.preview = {
      ...data,
      totalRows: data.totalRows ?? 0,
      previewRows: data.previewRows ?? [],
    }
    if (data.error) importDialog.error = data.error
  } catch (previewError) {
    importDialog.preview = null
    importDialog.error = getErrorMessage(previewError, '导入预览失败')
  } finally {
    importDialog.previewing = false
  }
}

async function startImport(): Promise<void> {
  const file = importFile.value
  if (!file) {
    ElMessage.warning('请先选择导入文件')
    return
  }

  try {
    await ElMessageBox.confirm(
      `将按「${file.name}」导入机构数据。重名或无法解析区划的行会跳过并记录，确定开始导入吗？`,
      '开始导入机构',
      { confirmButtonText: '开始导入', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  importDialog.importing = true
  importDialog.error = ''
  try {
    const data = await importOrganizations(file)
    importDialog.result = {
      ...data,
      errors: data.errors ?? [],
      warnings: data.warnings ?? [],
    }
    if (data.success) {
      ElMessage.success(data.message || `导入完成：成功 ${data.importedCount} 条`)
    } else {
      importDialog.error = data.message || '导入失败'
    }
    void loadOrgOptions(true)
    await loadTree(selectedId.value)
  } catch (importError) {
    importDialog.result = null
    importDialog.error = getErrorMessage(importError, '导入机构失败')
  } finally {
    importDialog.importing = false
  }
}

function createRoot(): void {
  void openCreate(null)
}

function addChild(): void {
  if (!detail.value) return
  void openCreate(detail.value.id)
}

function editSelected(): void {
  void openEdit()
}

onMounted(() => {
  void loadPermissions()
  void loadOrgOptions()
  void loadTree()
})
</script>

<template>
  <div class="nc-page org-page">
    <NcPageHeader
      title="经办机构管理"
      description="维护组织机构树、机构信息与批量导入"
      kicker="系统 · 用户管理"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="treeLoading" @click="loadTree()">刷新</el-button>
        <el-button v-if="canCreate" :icon="Upload" @click="openImport">导入机构</el-button>
        <el-button v-if="canCreate" type="primary" :icon="Plus" @click="createRoot">
          新建机构
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="permissionsError"
      type="warning"
      :title="permissionsError"
      :closable="false"
      show-icon
    />
    <el-alert
      v-else-if="permissionsLoaded && !canCreate && !canEdit && !canDelete"
      type="info"
      title="当前账号没有机构维护权限，仅可查看组织树与机构详情。"
      :closable="false"
      show-icon
    />

    <div class="stats-grid">
      <NcStat
        label="全部机构数"
        :value="stats.totalOrganizations"
        hint="组织树节点总数"
        tone="ink"
        compact
      />
      <NcStat
        label="机构用户数"
        :value="stats.totalUsers"
        hint="机构关联用户数"
        tone="pine"
        compact
      />
      <NcStat
        label="有下级机构数"
        :value="stats.parentOrganizations"
        hint="存在下级的机构数"
        tone="amber"
        compact
      />
      <NcStat
        label="根机构数"
        :value="stats.rootOrganizations"
        hint="无上级的根机构数"
        tone="seal"
        compact
      />
    </div>

    <div class="manage-grid" :class="{ 'is-narrow': isNarrow }">
      <NcCard title="组织树" class="tree-card">
        <template #actions>
          <span class="muted">共 {{ stats.totalOrganizations }} 个</span>
        </template>

        <el-input
          v-model="searchText"
          class="tree-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索机构名称或简称"
        />

        <p v-if="treeError" class="error-note">
          {{ treeError }}
          <el-button link type="primary" @click="loadTree()">重试</el-button>
        </p>

        <div v-else v-loading="treeLoading" class="tree-box">
          <el-tree
            ref="treeRef"
            :data="tree"
            node-key="id"
            :props="treeProps"
            highlight-current
            default-expand-all
            :expand-on-click-node="false"
            :filter-node-method="filterTreeNode"
            empty-text="暂无机构数据"
            @node-click="handleNodeClick"
          >
            <template #default="{ data }">
              <span class="tree-node">
                <span class="tree-node__name">{{ data.name }}</span>
                <span v-if="data.abbreviation" class="tree-node__abbr">{{ data.abbreviation }}</span>
              </span>
            </template>
          </el-tree>
        </div>
      </NcCard>

      <NcCard title="机构详情" class="detail-card">
        <template #actions>
          <el-button
            v-if="detail && canCreate"
            :icon="Plus"
            size="small"
            @click="addChild"
          >
            添加下级
          </el-button>
          <el-button
            v-if="detail && canEdit"
            :icon="Edit"
            size="small"
            @click="editSelected"
          >
            编辑
          </el-button>
          <el-button
            v-if="detail && canDelete"
            type="danger"
            plain
            size="small"
            :icon="Delete"
            :loading="deleting"
            @click="removeOrganization"
          >
            删除
          </el-button>
        </template>

        <div v-loading="detailLoading" class="detail-body">
          <el-alert
            v-if="detailError"
            type="error"
            :title="detailError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="retryDetail">重试</el-button>
          </el-alert>

          <template v-else-if="detail">
            <div class="detail-head">
              <div class="detail-head__main">
                <h2>{{ detail.name }}</h2>
                <p>{{ detail.abbreviation || '未填写机构简称' }}</p>
              </div>
              <el-tag
                :type="detail.isSpecialCareInstitution ? 'warning' : 'info'"
                size="small"
              >
                {{ detail.isSpecialCareInstitution ? '特困供养机构' : '非特困供养机构' }}
              </el-tag>
            </div>

            <el-descriptions :column="isNarrow ? 1 : 2" border size="small">
              <el-descriptions-item label="机构简称">
                {{ detail.abbreviation || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="上级机构">
                {{ detail.parentName || '无（根机构）' }}
              </el-descriptions-item>
              <el-descriptions-item label="负责人">
                {{ detail.principal || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="负责人身份证">
                {{ detail.principalIdentityCard || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="联系电话">
                {{ detail.phone || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="邮编">
                {{ detail.postalCode || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="地址" :span="isNarrow ? 1 : 2">
                {{ detail.address || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="机构性质">
                {{ detail.institutionNature || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="机构类型">
                {{ detail.institutionType || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="机构类别">
                {{ detail.institutionCategory || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="特困供养机构">
                {{ detail.isSpecialCareInstitution ? '是' : '否' }}
              </el-descriptions-item>
              <el-descriptions-item label="所在地区" :span="isNarrow ? 1 : 2">
                {{ detailRegion }}
              </el-descriptions-item>
            </el-descriptions>
          </template>

          <el-empty v-else description="请从左侧选择一个组织查看详情" :image-size="88" />
        </div>
      </NcCard>
    </div>

    <!-- 新建 / 编辑机构 -->
    <el-dialog
      v-model="dialog.visible"
      :title="dialogTitle"
      :width="dialogWidth"
      :close-on-click-modal="false"
      @closed="handleDialogClosed"
    >
      <div class="org-form">
        <el-alert
          v-if="optionsError"
          type="warning"
          :title="optionsError"
          :closable="false"
          show-icon
        >
          <el-button link type="primary" :loading="optionsLoading" @click="loadOrgOptions(true)">
            重试
          </el-button>
        </el-alert>

        <p v-if="dialog.error" class="form-error">{{ dialog.error }}</p>

        <div class="form-grid">
          <div class="form-field">
            <span class="field-label">名称 <em>*</em></span>
            <el-input v-model="form.name" maxlength="100" placeholder="请输入机构名称" />
          </div>
          <div class="form-field">
            <span class="field-label">简称</span>
            <el-input v-model="form.abbreviation" maxlength="50" placeholder="请输入机构简称" />
          </div>

          <div class="form-field form-field--full">
            <span class="field-label">上级机构</span>
            <el-select
              v-model="form.parentId"
              class="field-control"
              clearable
              filterable
              :loading="optionsLoading"
              placeholder="无上级机构（根机构）"
            >
              <el-option
                v-for="item in parentCandidates"
                :key="item.id"
                :label="item.name"
                :value="item.id"
              />
            </el-select>
          </div>

          <div class="form-field">
            <span class="field-label">负责人</span>
            <el-input v-model="form.principal" maxlength="50" placeholder="负责人姓名" />
          </div>
          <div class="form-field">
            <span class="field-label">负责人身份证</span>
            <el-input
              v-model="form.principalIdentityCard"
              maxlength="18"
              placeholder="负责人身份证号"
            />
          </div>

          <div class="form-field">
            <span class="field-label">电话</span>
            <el-input v-model="form.phone" maxlength="30" placeholder="联系电话" />
          </div>
          <div class="form-field">
            <span class="field-label">邮编</span>
            <el-input v-model="form.postalCode" maxlength="10" placeholder="邮政编码" />
          </div>

          <div class="form-field form-field--full">
            <span class="field-label">地址</span>
            <el-input v-model="form.address" maxlength="200" placeholder="请输入地址" />
          </div>

          <div class="form-field">
            <span class="field-label">机构性质</span>
            <el-select
              v-model="form.institutionNature"
              class="field-control"
              filterable
              clearable
              allow-create
              default-first-option
              placeholder="请选择机构性质"
            >
              <el-option
                v-for="item in orgOptions.institutionNatures"
                :key="item"
                :label="item"
                :value="item"
              />
            </el-select>
          </div>
          <div class="form-field">
            <span class="field-label">机构类型</span>
            <el-select
              v-model="form.institutionType"
              class="field-control"
              filterable
              clearable
              allow-create
              default-first-option
              placeholder="请选择机构类型"
            >
              <el-option
                v-for="item in orgOptions.institutionTypes"
                :key="item"
                :label="item"
                :value="item"
              />
            </el-select>
          </div>

          <div class="form-field">
            <span class="field-label">机构类别</span>
            <el-select
              v-model="form.institutionCategory"
              class="field-control"
              filterable
              clearable
              allow-create
              default-first-option
              placeholder="请选择机构类别"
            >
              <el-option
                v-for="item in orgOptions.institutionCategories"
                :key="item"
                :label="item"
                :value="item"
              />
            </el-select>
          </div>
          <div class="form-field form-field--switch">
            <span class="field-label">特困供养机构</span>
            <el-switch v-model="form.isSpecialCareInstitution" active-text="是" inactive-text="否" />
          </div>
        </div>

        <div class="region-block">
          <div class="region-block__head">
            <span class="field-label">所在地区</span>
            <span class="muted">按省 / 市 / 县 / 乡镇 / 村逐级选择，可留空</span>
          </div>
          <p v-if="regionError" class="form-error">{{ regionError }}</p>

          <div class="region-grid">
            <div class="form-field">
              <span class="field-label">省/直辖市</span>
              <el-select
                v-model="form.provinceId"
                class="field-control"
                clearable
                filterable
                :loading="regionLoading.province"
                placeholder="请选择省份"
                @change="onProvinceChange"
              >
                <el-option
                  v-for="item in regionOptions.province"
                  :key="item.id"
                  :label="item.name"
                  :value="item.id"
                />
              </el-select>
            </div>

            <div class="form-field">
              <span class="field-label">地级市</span>
              <el-select
                v-model="form.cityId"
                class="field-control"
                clearable
                filterable
                :loading="regionLoading.city"
                placeholder="请选择地级市"
                @change="onCityChange"
              >
                <el-option
                  v-for="item in regionOptions.city"
                  :key="item.id"
                  :label="item.name"
                  :value="item.id"
                />
              </el-select>
            </div>

            <div class="form-field">
              <span class="field-label">县/区</span>
              <el-select
                v-model="form.countyId"
                class="field-control"
                clearable
                filterable
                :loading="regionLoading.county"
                placeholder="请选择县/区"
                @change="onCountyChange"
              >
                <el-option
                  v-for="item in regionOptions.county"
                  :key="item.id"
                  :label="item.name"
                  :value="item.id"
                />
              </el-select>
            </div>

            <div class="form-field">
              <span class="field-label">乡镇/街道</span>
              <el-select
                v-model="form.townId"
                class="field-control"
                clearable
                filterable
                :loading="regionLoading.town"
                placeholder="请选择乡镇/街道"
                @change="onTownChange"
              >
                <el-option
                  v-for="item in regionOptions.town"
                  :key="item.id"
                  :label="item.name"
                  :value="item.id"
                />
              </el-select>
            </div>

            <div class="form-field">
              <span class="field-label">村/社区</span>
              <el-select
                v-model="form.villageId"
                class="field-control"
                clearable
                filterable
                :loading="regionLoading.village"
                placeholder="请选择村/社区"
              >
                <el-option
                  v-for="item in regionOptions.village"
                  :key="item.id"
                  :label="item.name"
                  :value="item.id"
                />
              </el-select>
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <el-button :disabled="dialog.saving" @click="dialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="dialog.saving" @click="saveOrganization">
          保存
        </el-button>
      </template>
    </el-dialog>

    <!-- 导入机构 -->
    <el-dialog
      v-model="importDialog.visible"
      title="导入机构"
      :width="dialogWidth"
      :close-on-click-modal="false"
      @closed="resetImportState"
    >
      <div class="import-body">
        <p class="hint">
          支持 .xlsx / .xls / .csv / .tsv / .txt。表头需包含「名称」，可选「简称、负责人、电话、地址、邮编、机构性质、机构类型、机构类别、地级市、县区、乡镇、村/社区」。
        </p>

        <div class="import-file">
          <el-button :icon="Upload" :disabled="importDialog.importing" @click="pickImportFile">
            选择文件
          </el-button>
          <span v-if="importFile" class="file-name">{{ importFile.name }}</span>
          <span v-else class="muted">未选择文件</span>
          <input
            ref="importInput"
            class="hidden-input"
            type="file"
            accept=".xlsx,.xls,.csv,.tsv,.txt"
            @change="onImportFilePicked"
          />
        </div>

        <div class="import-actions">
          <el-button
            :loading="importDialog.previewing"
            :disabled="!importFile || importDialog.importing"
            @click="previewImport"
          >
            预览
          </el-button>
          <el-button
            type="primary"
            :loading="importDialog.importing"
            :disabled="!importFile || importDialog.previewing"
            @click="startImport"
          >
            开始导入
          </el-button>
        </div>

        <el-alert
          v-if="importDialog.error"
          type="error"
          :title="importDialog.error"
          :closable="false"
          show-icon
        />

        <div v-if="importDialog.preview" class="preview-block">
          <div class="block-head">
            <strong>导入预览</strong>
            <span class="muted">
              共 {{ importDialog.preview.totalRows }} 行，显示前
              {{ importDialog.preview.previewRows.length }} 行
            </span>
          </div>
          <el-table :data="importDialog.preview.previewRows" size="small" max-height="260" border>
            <el-table-column prop="rowNumber" label="行号" width="64" />
            <el-table-column prop="name" label="名称" min-width="140" show-overflow-tooltip />
            <el-table-column prop="abbreviation" label="简称" min-width="100" show-overflow-tooltip />
            <el-table-column prop="principal" label="负责人" min-width="90" show-overflow-tooltip />
            <el-table-column prop="phone" label="电话" min-width="110" show-overflow-tooltip />
            <el-table-column prop="address" label="地址" min-width="150" show-overflow-tooltip />
            <el-table-column prop="region" label="所在地区" min-width="150" show-overflow-tooltip />
          </el-table>
        </div>

        <div v-if="importDialog.result" class="result-block">
          <el-alert
            :type="importDialog.result.success ? 'success' : 'error'"
            :title="importDialog.result.message || '导入完成'"
            :closable="false"
            show-icon
          />

          <div class="result-tiles">
            <div class="result-tile result-tile--success">
              <span>成功</span>
              <strong>{{ importDialog.result.importedCount }}</strong>
            </div>
            <div class="result-tile result-tile--danger">
              <span>错误</span>
              <strong>{{ importDialog.result.errorCount }}</strong>
            </div>
            <div class="result-tile result-tile--warn">
              <span>警告</span>
              <strong>{{ importDialog.result.warnings.length }}</strong>
            </div>
          </div>

          <div v-if="importDialog.result.errors.length > 0" class="message-block">
            <strong>错误明细</strong>
            <ul class="message-list">
              <li v-for="(item, index) in importDialog.result.errors" :key="`error-${index}`">
                {{ item }}
              </li>
            </ul>
          </div>

          <div v-if="importDialog.result.warnings.length > 0" class="message-block">
            <strong>警告</strong>
            <ul class="message-list message-list--warn">
              <li v-for="(item, index) in importDialog.result.warnings" :key="`warn-${index}`">
                {{ item }}
              </li>
            </ul>
          </div>
        </div>
      </div>

      <template #footer>
        <el-button @click="importDialog.visible = false">关闭</el-button>
        <el-button
          v-if="importDialog.result"
          type="primary"
          @click="importDialog.visible = false"
        >
          完成
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.org-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.manage-grid {
  display: grid;
  grid-template-columns: minmax(280px, 340px) minmax(0, 1fr);
  align-items: start;
  gap: 15px;
}

.manage-grid.is-narrow {
  grid-template-columns: minmax(0, 1fr);
}

.tree-card,
.detail-card {
  min-width: 0;
}

.tree-search {
  margin-bottom: 10px;
}

.tree-box {
  max-height: 560px;
  min-height: 180px;
  overflow: auto;
  padding-right: 4px;
}

.tree-node {
  display: inline-flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
  padding-right: 6px;
}

.tree-node__name {
  overflow: hidden;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-node__abbr {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.detail-body {
  min-height: 200px;
}

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.detail-head__main {
  min-width: 0;
}

.detail-head h2 {
  margin: 0;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  letter-spacing: 0.03em;
  color: var(--nc-ink);
}

.detail-head p {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.org-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 16px;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.form-field--full {
  grid-column: 1 / -1;
}

.form-field--switch {
  justify-content: flex-end;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.field-label em {
  color: var(--nc-seal);
  font-style: normal;
}

.field-control {
  width: 100%;
}

.region-block {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.region-block__head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 12px;
}

.region-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px 16px;
}

.form-error {
  margin: 0;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
}

.error-note {
  margin: 0;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
}

.import-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.hint {
  margin: 0;
  color: var(--nc-muted);
  line-height: 1.7;
}

.import-file {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  border: 1px dashed var(--nc-line);
  border-radius: var(--nc-radius-sm);
}

.file-name {
  color: var(--nc-ink);
  overflow-wrap: anywhere;
}

.import-actions {
  display: flex;
  gap: 10px;
}

.preview-block,
.result-block {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.block-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 6px 12px;
}

.result-tiles {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.result-tile {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.result-tile span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.result-tile strong {
  font-size: 22px;
  font-family: var(--nc-font-serif);
}

.result-tile--success strong {
  color: var(--nc-pine);
}

.result-tile--danger strong {
  color: var(--nc-seal);
}

.result-tile--warn strong {
  color: var(--nc-amber);
}

.message-block {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.message-block > strong {
  color: var(--nc-ink-2);
}

.message-list {
  max-height: 150px;
  margin: 0;
  padding-left: 18px;
  overflow: auto;
  color: var(--nc-danger, #b42318);
  font-size: var(--nc-text-xs);
  line-height: 1.8;
}

.message-list--warn {
  color: var(--nc-amber);
}

.hidden-input {
  display: none;
}

.muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

@media (max-width: 1100px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .stats-grid,
  .form-grid,
  .result-tiles {
    grid-template-columns: 1fr;
  }

  .region-grid {
    grid-template-columns: 1fr;
  }

  .tree-box {
    max-height: 360px;
  }

  .detail-head {
    flex-direction: column;
  }

  .import-actions {
    flex-direction: column;
  }
}
</style>
