<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Edit,
  Plus,
  Refresh,
  Search,
  Upload,
} from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  createOrganization,
  deleteOrganization,
  getOrganization,
  getOrganizationTree,
  listOrganizationOptions,
  updateOrganization,
} from '@/api/organizations'
import { getCities, getCounties, getProvinces, getTowns, getVillages } from '@/api/regions'
import type { RegionOption } from '@/api/regions'
import { listDictItems } from '@/api/dictionary'
import type { DictItem } from '@/types/dictionary'
import { getMyPermissions } from '@/api/users'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  OrganizationDetail,
  OrganizationOption,
  OrganizationSaveInput,
  OrganizationStats,
  OrganizationTreeNode,
} from '@/types/organizations'

type RegionLevel = 'province' | 'city' | 'county' | 'town' | 'village'

interface FlatTreeNode {
  id: number
  name: string
  abbreviation: string
  depth: number
  childrenCount: number
}

interface RegionNames {
  province?: string
  city?: string
  county?: string
  town?: string
  village?: string
}

const REGION_LEVELS: RegionLevel[] = ['province', 'city', 'county', 'town', 'village']
const REGION_LABELS: Record<RegionLevel, string> = {
  province: '省份',
  city: '地级市',
  county: '县/区',
  town: '乡镇/街道',
  village: '村/社区',
}
const REGION_ID_KEYS: Record<
  RegionLevel,
  'provinceId' | 'cityId' | 'countyId' | 'townId' | 'villageId'
> = {
  province: 'provinceId',
  city: 'cityId',
  county: 'countyId',
  town: 'townId',
  village: 'villageId',
}
const EMPTY_STATS: OrganizationStats = {
  totalOrganizations: 0,
  totalUsers: 0,
  parentOrganizations: 0,
  rootOrganizations: 0,
  specialCareInstitutionCount: 0,
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 1024px)')

/* ───────────────────────────── 权限 ───────────────────────────── */

const permissions = ref<string[]>([])
const isSuperAdmin = ref(false)
const permissionsLoaded = ref(false)
const permissionsError = ref('')

function hasPermission(code: string): boolean {
  return permissionsLoaded.value && (isSuperAdmin.value || permissions.value.includes(code))
}

const canView = computed(() => hasPermission('ORG_VIEW'))
const canCreate = computed(() => hasPermission('ORG_CREATE'))
const canEdit = computed(() => hasPermission('ORG_EDIT'))
const canDelete = computed(() => hasPermission('ORG_DELETE'))

const missingPermissions = computed(() => {
  if (!permissionsLoaded.value) return []
  return (['ORG_VIEW', 'ORG_CREATE', 'ORG_EDIT', 'ORG_DELETE'] as const).filter(
    (code) => !hasPermission(code),
  )
})

const permissionHint = computed(() => {
  if (permissionsError.value) return permissionsError.value
  if (missingPermissions.value.length === 0) return ''
  return `当前账号缺少权限：${missingPermissions.value.join(' / ')}；对应操作将不可用。`
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
    permissionsError.value = getErrorMessage(error, '权限信息加载失败，写操作已禁用')
  }
}

function forbiddenMessage(code: string): string {
  const label: Record<string, string> = {
    ORG_VIEW: '查看组织',
    ORG_CREATE: '新建组织',
    ORG_EDIT: '编辑组织',
    ORG_DELETE: '删除组织',
  }
  return `当前账号缺少 ${code} 权限，无法${label[code] ?? '执行该操作'}；请联系管理员授权。`
}

function actionErrorText(error: unknown, code: string, fallback: string): string {
  if (error instanceof ApiError && error.status === 403) return forbiddenMessage(code)
  return getErrorMessage(error, fallback)
}

/* ───────────────────────────── 组织树与详情 ───────────────────────────── */

const treeNodes = ref<OrganizationTreeNode[]>([])
const stats = ref<OrganizationStats>({ ...EMPTY_STATS })
const treeLoading = ref(false)
const treeError = ref('')
const searchText = ref('')

const selectedId = ref<number | null>(null)
const detail = ref<OrganizationDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
const deleting = ref(false)
let detailTicket = 0

function findNode(nodes: OrganizationTreeNode[], id: number): OrganizationTreeNode | null {
  for (const node of nodes) {
    if (node.id === id) return node
    const child = findNode(node.children ?? [], id)
    if (child) return child
  }
  return null
}

function flattenNodes(
  nodes: OrganizationTreeNode[],
  fallbackDepth = 0,
  output: FlatTreeNode[] = [],
): FlatTreeNode[] {
  for (const node of nodes) {
    output.push({
      id: node.id,
      name: node.name,
      abbreviation: node.abbreviation ?? '',
      depth: Number.isFinite(node.depth) ? node.depth : fallbackDepth,
      childrenCount: node.children?.length ?? 0,
    })
    flattenNodes(node.children ?? [], fallbackDepth + 1, output)
  }
  return output
}

const flatNodes = computed(() => {
  const keyword = searchText.value.trim().toLowerCase()
  const rows = flattenNodes(treeNodes.value)
  if (!keyword) return rows
  return rows.filter(
    (row) =>
      row.name.toLowerCase().includes(keyword) ||
      row.abbreviation.toLowerCase().includes(keyword),
  )
})

const selectedNode = computed(() =>
  selectedId.value === null ? null : findNode(treeNodes.value, selectedId.value),
)

const selectedChildCount = computed(() => selectedNode.value?.children?.length ?? 0)
const specialCareCount = computed(() => stats.value.specialCareInstitutionCount ?? 0)
const totalUsers = computed(() => stats.value.totalUsers ?? 0)

function treeRowStyle(row: FlatTreeNode): Record<string, string> {
  const depth = Math.min(Math.max(row.depth, 0), 12)
  return { paddingLeft: `${10 + depth * 16}px` }
}

async function loadTree(preferId?: number | null): Promise<void> {
  treeLoading.value = true
  treeError.value = ''
  try {
    const data = await getOrganizationTree()
    treeNodes.value = data.nodes ?? []
    stats.value = data.stats ?? { ...EMPTY_STATS }

    const targetId = preferId !== undefined ? preferId : selectedId.value
    if (targetId !== null && findNode(treeNodes.value, targetId)) {
      selectedId.value = targetId
      await loadDetail(targetId)
    } else {
      selectedId.value = null
      detail.value = null
      detailError.value = ''
    }
  } catch (error) {
    treeNodes.value = []
    stats.value = { ...EMPTY_STATS }
    selectedId.value = null
    detail.value = null
    detailError.value = ''
    treeError.value = actionErrorText(error, 'ORG_VIEW', '组织树加载失败')
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
  } catch (error) {
    if (ticket !== detailTicket) return
    detail.value = null
    detailError.value = actionErrorText(error, 'ORG_VIEW', '组织详情加载失败')
  } finally {
    if (ticket === detailTicket) detailLoading.value = false
  }
}

function selectNode(row: FlatTreeNode): void {
  if (selectedId.value === row.id) return
  selectedId.value = row.id
  detail.value = null
  void loadDetail(row.id)
}

function retryDetail(): void {
  if (selectedId.value === null) return
  void loadDetail(selectedId.value)
}

/* ───────────────────────────── 下拉选项 ───────────────────────────── */

const organizationOptions = ref<OrganizationOption[]>([])
const parentOptionsLoading = ref(false)
const parentOptionsError = ref('')
let parentOptionsLoaded = false

async function loadParentOptions(force = false): Promise<void> {
  if (parentOptionsLoading.value) return
  if (parentOptionsLoaded && !force) return

  parentOptionsLoading.value = true
  parentOptionsError.value = ''
  try {
    const data = await listOrganizationOptions()
    organizationOptions.value = data.organizations ?? []
    parentOptionsLoaded = true
  } catch (error) {
    organizationOptions.value = []
    parentOptionsError.value = actionErrorText(error, 'ORG_VIEW', '上级组织选项加载失败')
  } finally {
    parentOptionsLoading.value = false
  }
}

const dictCategories = ref<DictItem[]>([])
const dictTypes = ref<DictItem[]>([])
const dictLoading = ref(false)
const dictError = ref('')
let dictLoaded = false

async function loadDictionaries(force = false): Promise<void> {
  if (dictLoading.value) return
  if (dictLoaded && !force) return

  dictLoading.value = true
  dictError.value = ''
  try {
    const [categories, types] = await Promise.all([
      listDictItems('InstitutionCategory'),
      listDictItems('InstitutionType'),
    ])
    dictCategories.value = categories ?? []
    dictTypes.value = types ?? []
    dictLoaded = true
  } catch (error) {
    dictCategories.value = []
    dictTypes.value = []
    dictError.value = getErrorMessage(error, '机构类别 / 类型字典加载失败')
  } finally {
    dictLoading.value = false
  }
}

function activeItems(items: DictItem[]): DictItem[] {
  return items.filter((item) => item.isActive !== false)
}

const categoryOptions = computed(() => activeItems(dictCategories.value))

const typeOptions = computed(() => {
  const categoryKey =
    dictCategories.value.find((item) => item.itemValue === form.institutionCategory)?.itemKey ??
    form.institutionCategory
  const all = activeItems(dictTypes.value)
  const filtered = categoryKey
    ? all.filter((item) => (item.description ?? '').trim() === categoryKey)
    : all
  if (form.institutionType && !filtered.some((item) => item.itemValue === form.institutionType)) {
    const current =
      all.find((item) => item.itemValue === form.institutionType) ??
      all.find((item) => item.itemKey === form.institutionType)
    if (current) return [...filtered, current]
  }
  return filtered
})

function dictionaryDisplayValue(items: DictItem[], value: string): string {
  if (!value) return ''
  if (items.some((item) => item.itemValue === value)) return value
  const byKey = items.find((item) => item.itemKey === value)
  return byKey?.itemValue ?? value
}

function normalizeInstitutionValues(): void {
  form.institutionCategory = dictionaryDisplayValue(
    dictCategories.value,
    form.institutionCategory,
  )
  form.institutionType = dictionaryDisplayValue(dictTypes.value, form.institutionType)
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

const dialogTitle = computed(() => (dialog.editingId === null ? '新建组织' : '编辑组织'))

function collectSubtreeIds(node: OrganizationTreeNode | null, target: Set<number>): void {
  if (!node) return
  target.add(node.id)
  for (const child of node.children ?? []) collectSubtreeIds(child, target)
}

const parentCandidates = computed(() => {
  const blocked = new Set<number>()
  if (dialog.editingId !== null) {
    blocked.add(dialog.editingId)
    collectSubtreeIds(findNode(treeNodes.value, dialog.editingId), blocked)
  }
  return organizationOptions.value.filter((item) => !blocked.has(item.id))
})

/* ───────────────────────────── 区划级联 ───────────────────────────── */

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
const regionLoaded = reactive<Record<RegionLevel, boolean>>({
  province: false,
  city: false,
  county: false,
  town: false,
  village: false,
})
const regionErrors = reactive<Record<RegionLevel, string>>({
  province: '',
  city: '',
  county: '',
  town: '',
  village: '',
})
const regionTickets: Record<RegionLevel, number> = {
  province: 0,
  city: 0,
  county: 0,
  town: 0,
  village: 0,
}

const regionErrorText = computed(() =>
  REGION_LEVELS.map((level) => regionErrors[level])
    .filter(Boolean)
    .join('；'),
)

function resetRegionLevel(level: RegionLevel): void {
  regionTickets[level] += 1
  regionOptions[level] = []
  regionLoading[level] = false
  regionLoaded[level] = false
  regionErrors[level] = ''
}

function resetRegionOptions(): void {
  for (const level of REGION_LEVELS) resetRegionLevel(level)
}

function clearRegionBelow(level: RegionLevel): void {
  const index = REGION_LEVELS.indexOf(level)
  for (let cursor = index + 1; cursor < REGION_LEVELS.length; cursor += 1) {
    const childLevel = REGION_LEVELS[cursor]
    form[REGION_ID_KEYS[childLevel]] = null
    resetRegionLevel(childLevel)
  }
}

function ensureRegionOption(level: RegionLevel, id: number | null, name?: string): void {
  if (id === null || id <= 0) return
  if (regionOptions[level].some((item) => item.id === id)) return
  const label = name?.trim() || `区划 ${id}`
  regionOptions[level] = [{ id, name: label }, ...regionOptions[level]]
}

async function loadRegionOptions(level: RegionLevel, parentId?: number | null): Promise<void> {
  const ticket = ++regionTickets[level]
  regionErrors[level] = ''

  if (level !== 'province' && (parentId == null || parentId <= 0)) {
    regionOptions[level] = []
    regionLoaded[level] = false
    regionLoading[level] = false
    return
  }

  regionLoading[level] = true
  try {
    let rows: RegionOption[]
    if (level === 'province') {
      rows = await getProvinces()
    } else if (level === 'city') {
      rows = await getCities(parentId as number)
    } else if (level === 'county') {
      rows = await getCounties(parentId as number)
    } else if (level === 'town') {
      rows = await getTowns(parentId as number)
    } else {
      rows = await getVillages(parentId as number)
    }

    if (ticket !== regionTickets[level]) return
    regionOptions[level] = rows ?? []
    regionLoaded[level] = true
  } catch (error) {
    if (ticket !== regionTickets[level]) return
    regionOptions[level] = []
    regionLoaded[level] = false
    regionErrors[level] = `${REGION_LABELS[level]}加载失败：${getErrorMessage(
      error,
      '请稍后重试',
    )}`
  } finally {
    if (ticket === regionTickets[level]) regionLoading[level] = false
  }
}

async function loadRegionChain(names: RegionNames = {}): Promise<void> {
  await loadRegionOptions('province')
  ensureRegionOption('province', form.provinceId, names.province)

  if (form.provinceId) await loadRegionOptions('city', form.provinceId)
  ensureRegionOption('city', form.cityId, names.city)

  if (form.cityId) await loadRegionOptions('county', form.cityId)
  ensureRegionOption('county', form.countyId, names.county)

  if (form.countyId) await loadRegionOptions('town', form.countyId)
  ensureRegionOption('town', form.townId, names.town)

  if (form.townId) await loadRegionOptions('village', form.townId)
  ensureRegionOption('village', form.villageId, names.village)
}

function onProvinceChange(value: number | null): void {
  clearRegionBelow('province')
  if (value) void loadRegionOptions('city', value)
}

function onCityChange(value: number | null): void {
  clearRegionBelow('city')
  if (value) void loadRegionOptions('county', value)
}

function onCountyChange(value: number | null): void {
  clearRegionBelow('county')
  if (value) void loadRegionOptions('town', value)
}

function onTownChange(value: number | null): void {
  clearRegionBelow('town')
  if (value) void loadRegionOptions('village', value)
}

function onCategoryChange(): void {
  const available = typeOptions.value.map((item) => item.itemValue)
  if (form.institutionType && !available.includes(form.institutionType)) {
    form.institutionType = ''
  }
}

async function retryRegions(): Promise<void> {
  const names: RegionNames = {
    province: detail.value?.provinceName,
    city: detail.value?.cityName,
    county: detail.value?.countyName,
    town: detail.value?.townName,
    village: detail.value?.villageName,
  }
  await loadRegionChain(names)
}

/* ───────────────────────────── 新建 / 编辑 ───────────────────────────── */

function assignForm(source: OrganizationDetail): void {
  Object.assign(form, {
    name: source.name ?? '',
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

async function openCreate(parentId: number | null = selectedId.value): Promise<void> {
  if (!canCreate.value) {
    ElMessage.warning(forbiddenMessage('ORG_CREATE'))
    return
  }

  Object.assign(form, emptyForm())
  form.parentId = parentId
  dialog.editingId = null
  dialog.error = ''
  dialog.visible = true
  resetRegionOptions()

  void loadParentOptions()
  void loadDictionaries()
  await loadRegionChain()
}

async function openEdit(): Promise<void> {
  const source = detail.value
  if (!source) return
  if (!canEdit.value) {
    ElMessage.warning(forbiddenMessage('ORG_EDIT'))
    return
  }

  dialog.editingId = source.id
  dialog.error = ''
  assignForm(source)
  dialog.visible = true
  resetRegionOptions()

  void loadParentOptions()
  void loadDictionaries().then(() => {
    normalizeInstitutionValues()
  })

  const names: RegionNames = {
    province: source.provinceName,
    city: source.cityName,
    county: source.countyName,
    town: source.townName,
    village: source.villageName,
  }
  await loadRegionChain(names)
}

function normalizeId(value: number | null): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : null
}

function regionSelectionError(): string {
  if (REGION_LEVELS.some((level) => regionLoading[level])) {
    return '所在地区选项正在加载，请稍候再保存。'
  }
  for (const level of REGION_LEVELS) {
    const id = normalizeId(form[REGION_ID_KEYS[level]])
    if (id === null) continue
    if (!regionLoaded[level]) {
      return `${REGION_LABELS[level]}数据未加载成功，无法确认所在地区。请点击“重试区划”或重新选择地区后再保存。`
    }
    if (!regionOptions[level].some((item) => item.id === id)) {
      return `当前${REGION_LABELS[level]}不在可选列表中，请重新选择所在地区。`
    }
  }
  return ''
}

async function saveOrganization(): Promise<void> {
  if (dialog.saving) return

  const name = form.name.trim()
  if (!name) {
    dialog.error = '组织名称不能为空'
    return
  }

  const regionIssue = regionSelectionError()
  if (regionIssue) {
    dialog.error = regionIssue
    ElMessage.warning(regionIssue)
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
      ElMessage.success(result.message || '组织创建成功')
      dialog.visible = false
      void loadParentOptions(true)
      await loadTree(result.id)
    } else {
      const targetId = dialog.editingId
      const result = await updateOrganization(targetId, payload)
      ElMessage.success(result.message || '组织保存成功')
      dialog.visible = false
      void loadParentOptions(true)
      await loadTree(targetId)
    }
  } catch (error) {
    dialog.error = actionErrorText(
      error,
      dialog.editingId === null ? 'ORG_CREATE' : 'ORG_EDIT',
      dialog.editingId === null ? '创建组织失败' : '保存组织失败',
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

async function removeOrganization(): Promise<void> {
  const current = detail.value
  if (!current) return
  if (!canDelete.value) {
    ElMessage.warning(forbiddenMessage('ORG_DELETE'))
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定删除组织「${current.name}」吗？存在下级组织或关联用户时将无法删除，此操作不可恢复。`,
      '删除组织',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deleting.value = true
  try {
    const result = await deleteOrganization(current.id)
    ElMessage.success(result.message || '组织已删除')
    selectedId.value = null
    detail.value = null
    detailError.value = ''
    void loadParentOptions(true)
    await loadTree(null)
  } catch (error) {
    const text = actionErrorText(error, 'ORG_DELETE', '删除组织失败')
    try {
      await ElMessageBox.alert(text, '无法删除', {
        confirmButtonText: '知道了',
        type: 'error',
      })
    } catch {
      ElMessage.error(text)
    }
  } finally {
    deleting.value = false
  }
}

/* ───────────────────────────── 展示辅助 ───────────────────────────── */

function displayValue(value: string | null | undefined): string {
  return value && value.trim() ? value : '—'
}

function formatDateTime(value: string | null | undefined): string {
  if (!value || value.startsWith('0001-01-01')) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (input: number) => String(input).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}`
}

function goBack(): void {
  router.push('/')
}

onMounted(async () => {
  await loadPermissions()
  if (permissionsLoaded.value && !canView.value) {
    treeError.value = forbiddenMessage('ORG_VIEW')
    return
  }
  await loadTree()
})
</script>

<template>
  <div class="nc-page org-page">
    <NcPageHeader title="经办机构管理" description="管理组织架构与机构树" kicker="系统 · 用户管理">
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="treeLoading" @click="loadTree()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="permissionHint"
      type="warning"
      :title="permissionHint"
      :closable="false"
      show-icon
    />

    <div class="org-layout" :class="{ 'is-narrow': isNarrow }">
      <NcCard class="tree-card" padding="sm">
        <div class="tree-toolbar">
          <el-input
            v-model="searchText"
            clearable
            :prefix-icon="Search"
            placeholder="搜索机构名称或简称"
          />
          <el-button
            type="primary"
            :icon="Plus"
            :disabled="!canCreate"
            class="tree-create"
            @click="openCreate()"
          >
            新建组织
          </el-button>
        </div>

        <div class="tree-heading">
          <strong>组织列表</strong>
          <span>共 {{ stats.totalOrganizations }} 个</span>
        </div>

        <div v-loading="treeLoading" class="tree-body">
          <div v-if="treeError" class="tree-error">
            <p>{{ treeError }}</p>
            <el-button link type="primary" @click="loadTree()">重试</el-button>
          </div>

          <el-empty
            v-else-if="flatNodes.length === 0"
            :description="searchText ? '未找到匹配的组织' : '暂无组织数据'"
            :image-size="72"
          />

          <div v-else class="org-list">
            <div
              v-for="row in flatNodes"
              :key="row.id"
              class="org-row"
              :class="{ 'is-selected': selectedId === row.id }"
              :style="treeRowStyle(row)"
              role="button"
              tabindex="0"
              :title="row.name"
              @click="selectNode(row)"
              @keydown.enter.prevent="selectNode(row)"
              @keydown.space.prevent="selectNode(row)"
            >
              <span class="org-row__dot" aria-hidden="true"></span>
              <span class="org-row__name">{{ row.name || '未命名组织' }}</span>
              <span v-if="row.abbreviation" class="org-row__abbr">{{ row.abbreviation }}</span>
              <span v-if="row.childrenCount > 0" class="org-row__count">{{ row.childrenCount }}</span>
            </div>
          </div>
        </div>
      </NcCard>

      <div class="org-main">
        <div class="stats-grid">
          <NcStat
            label="组织总数"
            :value="stats.totalOrganizations"
            hint="范围内机构数量"
            tone="ink"
            compact
          />
          <NcStat
            label="关联用户"
            :value="totalUsers"
            hint="机构关联用户数"
            tone="pine"
            compact
          />
          <NcStat
            label="子组织数"
            :value="selectedChildCount"
            hint="选中组织的直接下级"
            tone="amber"
            compact
          />
          <NcStat
            label="特困机构"
            :value="specialCareCount"
            hint="特困供养机构数"
            tone="seal"
            compact
          />
        </div>

        <NcCard title="快捷操作" subtitle="新建组织或从 Excel 批量导入机构数据。">
          <div class="quick-grid">
            <div class="quick-card">
              <div class="quick-card__text">
                <strong>新建组织</strong>
                <span>{{ selectedId ? '在选中组织下新增下级机构' : '创建新的组织机构' }}</span>
              </div>
              <el-button
                type="primary"
                plain
                :icon="Plus"
                :disabled="!canCreate"
                @click="openCreate()"
              >
                新建组织
              </el-button>
            </div>

            <div class="quick-card">
              <div class="quick-card__text">
                <strong>导入组织</strong>
                <span>从 Excel 文件批量导入组织数据</span>
              </div>
              <el-button
                plain
                :icon="Upload"
                :disabled="!canCreate"
                @click="router.push('/system/organizations/import')"
              >
                导入组织
              </el-button>
            </div>
          </div>
        </NcCard>

        <NcCard title="组织详情">
          <template #actions>
            <el-button
              v-if="detail"
              size="small"
              :icon="Edit"
              :disabled="!canEdit"
              @click="openEdit"
            >
              编辑
            </el-button>
            <el-button
              v-if="detail"
              size="small"
              type="danger"
              plain
              :icon="Delete"
              :loading="deleting"
              :disabled="!canDelete"
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
              <el-button
                v-if="selectedId !== null"
                link
                type="primary"
                @click="retryDetail"
              >
                重试
              </el-button>
            </el-alert>

            <template v-else-if="detail">
              <div class="detail-head">
                <div class="detail-head__main">
                  <h2>{{ detail.name }}</h2>
                  <p>{{ detail.abbreviation || '未填写机构简称' }}</p>
                </div>
                <el-tag :type="detail.isSpecialCareInstitution ? 'warning' : 'info'" size="small">
                  {{ detail.isSpecialCareInstitution ? '特困供养机构' : '非特困供养机构' }}
                </el-tag>
              </div>

              <el-descriptions :column="isNarrow ? 1 : 2" border size="small">
                <el-descriptions-item label="名称">
                  {{ displayValue(detail.name) }}
                </el-descriptions-item>
                <el-descriptions-item label="简称">
                  {{ displayValue(detail.abbreviation) }}
                </el-descriptions-item>
                <el-descriptions-item label="上级组织">
                  {{ detail.parentName || '无（根机构）' }}
                </el-descriptions-item>
                <el-descriptions-item label="负责人">
                  {{ displayValue(detail.principal) }}
                </el-descriptions-item>
                <el-descriptions-item label="电话">
                  {{ displayValue(detail.phone) }}
                </el-descriptions-item>
                <el-descriptions-item label="邮编">
                  {{ displayValue(detail.postalCode) }}
                </el-descriptions-item>
                <el-descriptions-item label="地址" :span="isNarrow ? 1 : 2">
                  {{ displayValue(detail.address) }}
                </el-descriptions-item>
                <el-descriptions-item label="机构性质">
                  {{ displayValue(detail.institutionNature) }}
                </el-descriptions-item>
                <el-descriptions-item label="机构类别">
                  {{ displayValue(detail.institutionCategory) }}
                </el-descriptions-item>
                <el-descriptions-item label="机构类型">
                  {{ displayValue(detail.institutionType) }}
                </el-descriptions-item>
                <el-descriptions-item label="特困供养">
                  {{ detail.isSpecialCareInstitution ? '是' : '否' }}
                </el-descriptions-item>
                <el-descriptions-item label="省/直辖市">
                  {{ displayValue(detail.provinceName) }}
                </el-descriptions-item>
                <el-descriptions-item label="地级市">
                  {{ displayValue(detail.cityName) }}
                </el-descriptions-item>
                <el-descriptions-item label="县/区">
                  {{ displayValue(detail.countyName) }}
                </el-descriptions-item>
                <el-descriptions-item label="乡镇/街道">
                  {{ displayValue(detail.townName) }}
                </el-descriptions-item>
                <el-descriptions-item label="村/社区">
                  {{ displayValue(detail.villageName) }}
                </el-descriptions-item>
                <el-descriptions-item label="创建时间">
                  {{ formatDateTime(detail.createdAt) }}
                </el-descriptions-item>
                <el-descriptions-item label="更新时间">
                  {{ formatDateTime(detail.updatedAt) }}
                </el-descriptions-item>
                <el-descriptions-item label="组织用户数">
                  {{ detail.userCount ?? 0 }}
                </el-descriptions-item>
              </el-descriptions>
            </template>

            <el-empty v-else description="请从左侧选择一个组织查看详情" :image-size="88" />
          </div>
        </NcCard>
      </div>
    </div>

    <!-- 新建 / 编辑组织 -->
    <el-dialog
      v-model="dialog.visible"
      :title="dialogTitle"
      width="min(560px, 94vw)"
      :close-on-click-modal="false"
      @closed="handleDialogClosed"
    >
      <div class="org-form">
        <el-alert
          v-if="dialog.error"
          type="error"
          :title="dialog.error"
          :closable="false"
          show-icon
        />

        <el-alert
          v-if="parentOptionsError"
          type="warning"
          :title="parentOptionsError"
          :closable="false"
          show-icon
        >
          <el-button link type="primary" @click="loadParentOptions(true)">重试</el-button>
        </el-alert>

        <el-alert v-if="dictError" type="warning" :title="dictError" :closable="false" show-icon>
          <el-button link type="primary" @click="loadDictionaries(true)">重试</el-button>
        </el-alert>

        <div class="form-grid">
          <div class="form-field">
            <span class="field-label">名称 <em>*</em></span>
            <el-input v-model="form.name" maxlength="100" placeholder="请输入组织名称" />
          </div>
          <div class="form-field">
            <span class="field-label">简称</span>
            <el-input v-model="form.abbreviation" maxlength="50" placeholder="请输入组织简称" />
          </div>

          <div class="form-field form-field--full">
            <span class="field-label">上级组织</span>
            <el-select
              v-model="form.parentId"
              class="field-control"
              clearable
              filterable
              :loading="parentOptionsLoading"
              placeholder="无上级组织（根机构）"
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
            <span class="field-label">电话</span>
            <el-input v-model="form.phone" maxlength="30" placeholder="联系电话" />
          </div>

          <div class="form-field form-field--full">
            <span class="field-label">地址</span>
            <el-input v-model="form.address" maxlength="200" placeholder="请输入地址" />
          </div>

          <div class="form-field">
            <span class="field-label">邮编</span>
            <el-input v-model="form.postalCode" maxlength="10" placeholder="邮政编码" />
          </div>
          <div class="form-field">
            <span class="field-label">机构性质</span>
            <el-input v-model="form.institutionNature" maxlength="50" placeholder="请输入机构性质" />
          </div>

          <div class="form-field">
            <span class="field-label">机构类别</span>
            <el-select
              v-model="form.institutionCategory"
              class="field-control"
              clearable
              filterable
              :loading="dictLoading"
              placeholder="请选择机构类别"
              @change="onCategoryChange"
            >
              <el-option
                v-for="item in categoryOptions"
                :key="item.id"
                :label="item.itemValue"
                :value="item.itemValue"
              />
            </el-select>
          </div>
          <div class="form-field">
            <span class="field-label">机构类型</span>
            <el-select
              v-model="form.institutionType"
              class="field-control"
              clearable
              filterable
              :loading="dictLoading"
              :placeholder="form.institutionCategory ? '请选择机构类型' : '请先选择机构类别'"
            >
              <el-option
                v-for="item in typeOptions"
                :key="item.id"
                :label="item.itemValue"
                :value="item.itemValue"
              />
            </el-select>
          </div>

          <div class="form-field form-field--switch">
            <span class="field-label">特困供养</span>
            <el-switch
              v-model="form.isSpecialCareInstitution"
              active-text="是"
              inactive-text="否"
            />
          </div>
        </div>

        <div class="region-block">
          <div class="region-block__head">
            <span class="field-label">所在地区</span>
            <span class="region-block__hint">按省 / 市 / 县 / 乡镇 / 村逐级选择，可留空</span>
          </div>

          <el-alert
            v-if="regionErrorText"
            type="warning"
            :title="regionErrorText"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" :loading="regionLoading.province" @click="retryRegions">
              重试区划
            </el-button>
          </el-alert>

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
                :disabled="!form.provinceId"
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
                :disabled="!form.cityId"
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
                :disabled="!form.countyId"
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
                :disabled="!form.townId"
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
  </div>
</template>

<style scoped>
.org-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.org-layout {
  display: grid;
  grid-template-columns: 320px minmax(0, 1fr);
  align-items: start;
  gap: 15px;
}

.org-layout.is-narrow {
  grid-template-columns: minmax(0, 1fr);
}

.tree-card,
.org-main,
.detail-body {
  min-width: 0;
}

.org-main {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.tree-toolbar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
}

.tree-create {
  width: 100%;
}

.tree-heading {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.tree-heading strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  letter-spacing: 0.02em;
}

.tree-heading span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.tree-body {
  min-height: 220px;
}

.org-list {
  display: flex;
  max-height: 620px;
  flex-direction: column;
  gap: 2px;
  padding-right: 2px;
  overflow: auto;
}

.org-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  padding-top: 7px;
  padding-right: 8px;
  padding-bottom: 7px;
  border: 1px solid transparent;
  border-left: 2px solid transparent;
  border-radius: var(--nc-radius-xs);
  color: var(--nc-ink-2);
  cursor: pointer;
  transition: border-color var(--nc-transition), background-color var(--nc-transition);
}

.org-row:hover {
  background-color: var(--nc-paper-soft);
}

.org-row:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.org-row.is-selected {
  border-color: rgba(171, 50, 39, 0.3);
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-ink);
}

.org-row__dot {
  flex: none;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background-color: var(--nc-line-strong);
}

.org-row.is-selected .org-row__dot {
  background-color: var(--nc-seal);
}

.org-row__name {
  overflow: hidden;
  min-width: 0;
  flex: 1;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.org-row__abbr {
  overflow: hidden;
  flex: none;
  max-width: 70px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.org-row__count {
  flex: none;
  min-width: 18px;
  padding: 0 4px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  line-height: 15px;
  text-align: center;
}

.tree-error {
  padding: 16px 10px;
  color: #b42318;
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.tree-error p {
  margin: 0 0 6px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.quick-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.quick-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-width: 0;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.quick-card__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 3px;
}

.quick-card__text strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  letter-spacing: 0.02em;
}

.quick-card__text span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
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
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  letter-spacing: 0.03em;
  word-break: break-all;
}

.detail-head p {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.org-form {
  display: flex;
  flex-direction: column;
  gap: 13px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 14px;
}

.form-field {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 6px;
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

.region-block__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.region-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 14px;
}

@media (max-width: 1280px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1024px) {
  .org-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .org-list {
    max-height: 380px;
  }
}

@media (max-width: 767px) {
  .stats-grid,
  .quick-grid,
  .form-grid,
  .region-grid {
    grid-template-columns: 1fr;
  }

  .quick-card {
    flex-direction: column;
    align-items: stretch;
    text-align: left;
  }
}
</style>
