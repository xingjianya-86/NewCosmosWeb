<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Connection, Delete, Download, Plus } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  clearRegions,
  createCity,
  createCounty,
  createTown,
  createVillage,
  deleteCity,
  deleteCounty,
  deleteTown,
  deleteVillage,
  listManageCities,
  listManageCounties,
  listManageProvinces,
  listManageTowns,
  listManageVillages,
  seedRegions,
  syncRegionSchema,
  updateCity,
  updateCounty,
  updateTown,
  updateVillage,
} from '@/api/regions'
import type {
  RegionCity,
  RegionCitySave,
  RegionCounty,
  RegionCountySave,
  RegionProvince,
  RegionTown,
  RegionTownSave,
  RegionVillage,
  RegionVillageSave,
} from '@/api/regions'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'

type RegionLevel = 'city' | 'county' | 'town' | 'village'

interface RegionRow {
  id: number
  name: string
  code: string
  sortOrder: number
  isActive: boolean
  type?: string
}

interface RegionColumn {
  key: RegionLevel
  label: string
  parentReady: boolean
  parentHint: string
  selectedId: number | null
  items: RegionRow[]
}

const LEVEL_LABELS: Record<RegionLevel, string> = {
  city: '地级市',
  county: '县区',
  town: '乡镇/街道',
  village: '村/社区',
}

const provinces = ref<RegionProvince[]>([])
const selectedProvinceId = ref<number | null>(null)
const cities = ref<RegionCity[]>([])
const selectedCityId = ref<number | null>(null)
const counties = ref<RegionCounty[]>([])
const selectedCountyId = ref<number | null>(null)
const towns = ref<RegionTown[]>([])
const selectedTownId = ref<number | null>(null)
const villages = ref<RegionVillage[]>([])
const selectedVillageId = ref<number | null>(null)

const provinceLoading = ref(false)
const provinceError = ref('')
const loading = reactive<Record<RegionLevel, boolean>>({
  city: false,
  county: false,
  town: false,
  village: false,
})
const errors = reactive<Record<RegionLevel, string>>({
  city: '',
  county: '',
  town: '',
  village: '',
})
const tickets: Record<RegionLevel, number> = { city: 0, county: 0, town: 0, village: 0 }
const deletingKey = ref('')
const actionLoading = reactive({ schema: false, seed: false, clear: false })

const selectedProvince = computed(
  () => provinces.value.find((item) => item.id === selectedProvinceId.value) ?? null,
)
const selectedCity = computed(
  () => cities.value.find((item) => item.id === selectedCityId.value) ?? null,
)
const selectedCounty = computed(
  () => counties.value.find((item) => item.id === selectedCountyId.value) ?? null,
)
const selectedTown = computed(
  () => towns.value.find((item) => item.id === selectedTownId.value) ?? null,
)

const columns = computed<RegionColumn[]>(() => [
  {
    key: 'city',
    label: '地级市',
    parentReady: selectedProvinceId.value !== null,
    parentHint: '请先选择省份',
    selectedId: selectedCityId.value,
    items: cities.value.map((item) => ({
      id: item.id,
      name: item.cityName,
      code: item.cityCode,
      sortOrder: item.sortOrder,
      isActive: item.isActive,
    })),
  },
  {
    key: 'county',
    label: '县区',
    parentReady: selectedCityId.value !== null,
    parentHint: '请先选择地级市',
    selectedId: selectedCountyId.value,
    items: counties.value.map((item) => ({
      id: item.id,
      name: item.countyName,
      code: item.countyCode,
      sortOrder: item.sortOrder,
      isActive: item.isActive,
    })),
  },
  {
    key: 'town',
    label: '乡镇/街道',
    parentReady: selectedCountyId.value !== null,
    parentHint: '请先选择县区',
    selectedId: selectedTownId.value,
    items: towns.value.map((item) => ({
      id: item.id,
      name: item.townName,
      code: item.townCode,
      sortOrder: item.sortOrder,
      isActive: item.isActive,
      type: item.townType,
    })),
  },
  {
    key: 'village',
    label: '村/社区',
    parentReady: selectedTownId.value !== null,
    parentHint: '请先选择乡镇/街道',
    selectedId: selectedVillageId.value,
    items: villages.value.map((item) => ({
      id: item.id,
      name: item.villageName,
      code: item.villageCode,
      sortOrder: item.sortOrder,
      isActive: item.isActive,
      type: item.villageType,
    })),
  },
])

const editor = reactive({
  visible: false,
  level: 'city' as RegionLevel,
  editingId: null as number | null,
  submitting: false,
  error: '',
  name: '',
  code: '',
  type: '',
  sortOrder: 0,
  isActive: true,
})

const editorTitle = computed(
  () => `${editor.editingId === null ? '新增' : '编辑'}${LEVEL_LABELS[editor.level]}`,
)
const editorParentLabel = computed(() => {
  if (editor.level === 'county') return '所属地级市'
  if (editor.level === 'town') return '所属县区'
  if (editor.level === 'village') return '所属乡镇/街道'
  return ''
})
const editorParentName = computed(() => {
  if (editor.level === 'county') return selectedCity.value?.cityName ?? ''
  if (editor.level === 'town') return selectedCounty.value?.countyName ?? ''
  if (editor.level === 'village') return selectedTown.value?.townName ?? ''
  return ''
})
const editorTypeLabel = computed(() => {
  if (editor.level === 'town') return '类型'
  if (editor.level === 'village') return '类型'
  return ''
})
const editorTypePlaceholder = computed(() => {
  if (editor.level === 'town') return '如：镇 / 乡 / 街道'
  if (editor.level === 'village') return '如：村委会 / 居委会'
  return ''
})

function clearVillages(): void {
  villages.value = []
  selectedVillageId.value = null
}

function clearTowns(): void {
  towns.value = []
  selectedTownId.value = null
  clearVillages()
}

function clearCounties(): void {
  counties.value = []
  selectedCountyId.value = null
  clearTowns()
}

function clearCities(): void {
  cities.value = []
  selectedCityId.value = null
  clearCounties()
}

async function loadProvinces(): Promise<void> {
  provinceLoading.value = true
  provinceError.value = ''
  try {
    provinces.value = await listManageProvinces()
  } catch (error) {
    provinces.value = []
    provinceError.value = getErrorMessage(error, '省份列表加载失败')
  } finally {
    provinceLoading.value = false
  }
}

async function loadCities(provinceId: number | null): Promise<void> {
  const ticket = ++tickets.city
  cities.value = []
  errors.city = ''
  if (!provinceId) {
    loading.city = false
    return
  }
  loading.city = true
  try {
    const rows = await listManageCities(provinceId)
    if (ticket === tickets.city) {
      cities.value = rows
      if (selectedCityId.value !== null && !rows.some((item) => item.id === selectedCityId.value)) {
        selectedCityId.value = null
        clearCounties()
      }
    }
  } catch (error) {
    if (ticket === tickets.city) errors.city = getErrorMessage(error, '地级市加载失败')
  } finally {
    if (ticket === tickets.city) loading.city = false
  }
}

async function loadCounties(cityId: number | null): Promise<void> {
  const ticket = ++tickets.county
  counties.value = []
  errors.county = ''
  if (!cityId) {
    loading.county = false
    return
  }
  loading.county = true
  try {
    const rows = await listManageCounties(cityId)
    if (ticket === tickets.county) {
      counties.value = rows
      if (
        selectedCountyId.value !== null &&
        !rows.some((item) => item.id === selectedCountyId.value)
      ) {
        selectedCountyId.value = null
        clearTowns()
      }
    }
  } catch (error) {
    if (ticket === tickets.county) errors.county = getErrorMessage(error, '县区加载失败')
  } finally {
    if (ticket === tickets.county) loading.county = false
  }
}

async function loadTowns(countyId: number | null): Promise<void> {
  const ticket = ++tickets.town
  towns.value = []
  errors.town = ''
  if (!countyId) {
    loading.town = false
    return
  }
  loading.town = true
  try {
    const rows = await listManageTowns(countyId)
    if (ticket === tickets.town) {
      towns.value = rows
      if (selectedTownId.value !== null && !rows.some((item) => item.id === selectedTownId.value)) {
        selectedTownId.value = null
        clearVillages()
      }
    }
  } catch (error) {
    if (ticket === tickets.town) errors.town = getErrorMessage(error, '乡镇/街道加载失败')
  } finally {
    if (ticket === tickets.town) loading.town = false
  }
}

async function loadVillages(townId: number | null): Promise<void> {
  const ticket = ++tickets.village
  villages.value = []
  errors.village = ''
  if (!townId) {
    loading.village = false
    return
  }
  loading.village = true
  try {
    const rows = await listManageVillages(townId)
    if (ticket === tickets.village) {
      villages.value = rows
      if (
        selectedVillageId.value !== null &&
        !rows.some((item) => item.id === selectedVillageId.value)
      ) {
        selectedVillageId.value = null
      }
    }
  } catch (error) {
    if (ticket === tickets.village) errors.village = getErrorMessage(error, '村/社区加载失败')
  } finally {
    if (ticket === tickets.village) loading.village = false
  }
}

function handleProvinceChange(value: number | null): void {
  selectedProvinceId.value = value
  clearCities()
  void loadCities(value)
}

function selectRow(level: RegionLevel, row: RegionRow): void {
  if (level === 'city') {
    selectedCityId.value = row.id
    clearCounties()
    void loadCounties(row.id)
    return
  }
  if (level === 'county') {
    selectedCountyId.value = row.id
    clearTowns()
    void loadTowns(row.id)
    return
  }
  if (level === 'town') {
    selectedTownId.value = row.id
    clearVillages()
    void loadVillages(row.id)
    return
  }
  selectedVillageId.value = row.id
}

function retryColumn(level: RegionLevel): void {
  if (level === 'city') {
    void loadCities(selectedProvinceId.value)
  } else if (level === 'county') {
    void loadCounties(selectedCityId.value)
  } else if (level === 'town') {
    void loadTowns(selectedCountyId.value)
  } else {
    void loadVillages(selectedTownId.value)
  }
}

function canCreate(level: RegionLevel): boolean {
  if (level === 'city') return selectedProvinceId.value !== null
  if (level === 'county') return selectedCity.value !== null
  if (level === 'town') return selectedCounty.value !== null
  return selectedTown.value !== null
}

function resetEditor(): void {
  editor.error = ''
  editor.name = ''
  editor.code = ''
  editor.type = ''
  editor.sortOrder = 0
  editor.isActive = true
}

function openCreate(level: RegionLevel): void {
  if (!canCreate(level)) {
    ElMessage.warning(`请先选择${level === 'city' ? '省份' : level === 'county' ? '地级市' : level === 'town' ? '县区' : '乡镇/街道'}`)
    return
  }
  editor.level = level
  editor.editingId = null
  resetEditor()
  editor.visible = true
}

function openEdit(level: RegionLevel, row: RegionRow): void {
  editor.level = level
  editor.editingId = row.id
  resetEditor()
  editor.name = row.name
  editor.code = row.code
  editor.type = row.type ?? ''
  editor.sortOrder = row.sortOrder
  editor.isActive = row.isActive
  editor.visible = true
}

async function reloadAfterChange(level: RegionLevel): Promise<void> {
  if (level === 'city') {
    clearCounties()
    await loadCities(selectedProvinceId.value)
  } else if (level === 'county') {
    clearTowns()
    await loadCounties(selectedCityId.value)
  } else if (level === 'town') {
    clearVillages()
    await loadTowns(selectedCountyId.value)
  } else {
    await loadVillages(selectedTownId.value)
  }
}

async function submitEditor(): Promise<void> {
  const name = editor.name.trim()
  if (!name) {
    editor.error = '名称不能为空'
    return
  }

  const city = selectedCity.value
  const county = selectedCounty.value
  const town = selectedTown.value
  if (editor.level === 'county' && !city) {
    editor.error = '请先选择地级市'
    return
  }
  if (editor.level === 'town' && !county) {
    editor.error = '请先选择县区'
    return
  }
  if (editor.level === 'village' && !town) {
    editor.error = '请先选择乡镇/街道'
    return
  }

  const code = editor.code.trim()
  const sortOrder = Number.isFinite(editor.sortOrder) ? Math.max(0, Math.trunc(editor.sortOrder)) : 0
  const editingId = editor.editingId
  const level = editor.level

  editor.submitting = true
  editor.error = ''
  try {
    if (level === 'city') {
      const payload: RegionCitySave = {
        cityName: name,
        cityCode: code,
        provinceId: selectedProvinceId.value,
        sortOrder,
        isActive: editor.isActive,
      }
      if (editingId === null) await createCity(payload)
      else await updateCity(editingId, payload)
    } else if (level === 'county' && city) {
      const payload: RegionCountySave = {
        countyName: name,
        countyCode: code,
        cityName: city.cityName,
        sortOrder,
        isActive: editor.isActive,
      }
      if (editingId === null) await createCounty(payload)
      else await updateCounty(editingId, payload)
    } else if (level === 'town' && county) {
      const payload: RegionTownSave = {
        countyId: county.id,
        townName: name,
        townCode: code,
        townType: editor.type.trim() || undefined,
        sortOrder,
        isActive: editor.isActive,
      }
      if (editingId === null) await createTown(payload)
      else await updateTown(editingId, payload)
    } else if (level === 'village' && town) {
      const payload: RegionVillageSave = {
        townId: town.id,
        villageName: name,
        villageCode: code,
        villageType: editor.type.trim() || undefined,
        sortOrder,
        isActive: editor.isActive,
      }
      if (editingId === null) await createVillage(payload)
      else await updateVillage(editingId, payload)
    }

    editor.visible = false
    ElMessage.success(editingId === null ? '已新增' : '已保存')
    await reloadAfterChange(level)
  } catch (error) {
    editor.error = getErrorMessage(error, '保存失败')
  } finally {
    editor.submitting = false
  }
}

async function removeRow(level: RegionLevel, row: RegionRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除${LEVEL_LABELS[level]}「${row.name}」吗？删除后不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deletingKey.value = `${level}:${row.id}`
  try {
    if (level === 'city') {
      await deleteCity(row.id)
      if (selectedCityId.value === row.id) selectedCityId.value = null
      clearCounties()
      await loadCities(selectedProvinceId.value)
    } else if (level === 'county') {
      await deleteCounty(row.id)
      if (selectedCountyId.value === row.id) selectedCountyId.value = null
      clearTowns()
      await loadCounties(selectedCityId.value)
    } else if (level === 'town') {
      await deleteTown(row.id)
      if (selectedTownId.value === row.id) selectedTownId.value = null
      clearVillages()
      await loadTowns(selectedCountyId.value)
    } else {
      await deleteVillage(row.id)
      if (selectedVillageId.value === row.id) selectedVillageId.value = null
      await loadVillages(selectedTownId.value)
    }
    ElMessage.success('已删除')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  } finally {
    deletingKey.value = ''
  }
}

async function reloadAll(): Promise<void> {
  selectedProvinceId.value = null
  clearCities()
  await loadProvinces()
  const first = provinces.value[0]
  if (!first) return
  selectedProvinceId.value = first.id
  await loadCities(first.id)
}

async function handleSyncSchema(): Promise<void> {
  actionLoading.schema = true
  try {
    await syncRegionSchema()
    ElMessage.success('行政区划表结构同步完成')
    await reloadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '同步表结构失败'))
  } finally {
    actionLoading.schema = false
  }
}

async function handleSeed(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将从种子数据重新初始化区划：先清空地级市及以下数据，再导入黑龙江省全量区划。此操作会覆盖现有区划，且不可撤销。',
      '从种子初始化',
      { confirmButtonText: '确认初始化', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  actionLoading.seed = true
  try {
    await seedRegions()
    ElMessage.success('行政区划种子数据初始化完成')
    await reloadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '初始化失败'))
  } finally {
    actionLoading.seed = false
  }
}

async function handleClear(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将清空县区、乡镇/街道、村/社区全部数据（省份与地级市保留）。此操作不可撤销，确定继续吗？',
      '清空区划数据',
      { confirmButtonText: '确认清空', cancelButtonText: '取消', type: 'error' },
    )
  } catch {
    return
  }

  actionLoading.clear = true
  try {
    await clearRegions()
    ElMessage.success('行政区划数据已清空')
    await reloadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '清空数据失败'))
  } finally {
    actionLoading.clear = false
  }
}

onMounted(async () => {
  await loadProvinces()
  const first = provinces.value[0]
  if (!first) return
  selectedProvinceId.value = first.id
  await loadCities(first.id)
})
</script>

<template>
  <div class="nc-page region-page">
    <NcPageHeader
      title="行政区划管理"
      description="维护省、市、县区、乡镇街道、村社区五级数据"
      kicker="系统 · 数据中心"
    >
      <template #actions>
        <el-button :icon="Connection" :loading="actionLoading.schema" @click="handleSyncSchema">
          同步表结构
        </el-button>
        <el-button :icon="Download" :loading="actionLoading.seed" @click="handleSeed">
          从种子初始化
        </el-button>
        <el-button
          type="danger"
          plain
          :icon="Delete"
          :loading="actionLoading.clear"
          @click="handleClear"
        >
          清空数据
        </el-button>
      </template>
    </NcPageHeader>

    <NcCard title="五级行政区划" subtitle="选择省份后逐级维护；点击地级市、县区、乡镇/街道行可展开下一级。">
      <div class="region-toolbar">
        <span class="region-toolbar__label">省份</span>
        <el-select
          v-model="selectedProvinceId"
          class="region-toolbar__select"
          placeholder="请选择省份"
          filterable
          :loading="provinceLoading"
          @change="handleProvinceChange"
        >
          <el-option
            v-for="province in provinces"
            :key="province.id"
            :label="province.provinceName"
            :value="province.id"
          />
        </el-select>
        <span v-if="selectedProvince?.provinceCode" class="region-toolbar__hint">
          编码 {{ selectedProvince.provinceCode }}
        </span>
        <span v-else-if="!provinceLoading && provinces.length === 0" class="region-toolbar__hint">
          暂无省份数据，请先同步表结构或从种子初始化
        </span>
      </div>

      <p v-if="provinceError" class="region-error">{{ provinceError }}</p>

      <div class="region-grid">
        <section v-for="column in columns" :key="column.key" class="region-column">
          <header class="column-head">
            <div class="column-head__text">
              <strong>{{ column.label }}（{{ column.items.length }}）</strong>
              <small v-if="!column.parentReady">{{ column.parentHint }}</small>
              <small v-else>{{ column.key === 'village' ? '点击行选中' : '点击行查看下一级' }}</small>
            </div>
            <el-button
              size="small"
              type="primary"
              plain
              :icon="Plus"
              :disabled="!column.parentReady"
              @click="openCreate(column.key)"
            >
              新增
            </el-button>
          </header>

          <div v-loading="loading[column.key]" class="column-body">
            <div v-if="!column.parentReady" class="column-hint">
              {{ column.parentHint }}
            </div>
            <p v-else-if="errors[column.key]" class="column-error">
              {{ errors[column.key] }}
              <el-button link type="primary" @click="retryColumn(column.key)">重试</el-button>
            </p>
            <div v-else-if="column.items.length === 0" class="column-hint">暂无数据</div>

            <div v-else class="region-list">
              <div
                v-for="row in column.items"
                :key="row.id"
                class="region-row"
                :class="{ 'is-selected': column.selectedId === row.id }"
                role="button"
                tabindex="0"
                @click="selectRow(column.key, row)"
                @keydown.enter.prevent="selectRow(column.key, row)"
                @keydown.space.prevent="selectRow(column.key, row)"
              >
                <div class="region-row__name">
                  <span v-if="row.type" class="region-row__type">{{ row.type }}</span>
                  <strong :title="row.name">{{ row.name || '未命名' }}</strong>
                </div>
                <div class="region-row__actions">
                  <el-button link type="primary" @click.stop="openEdit(column.key, row)">
                    编辑
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    :loading="deletingKey === `${column.key}:${row.id}`"
                    @click.stop="removeRow(column.key, row)"
                  >
                    删除
                  </el-button>
                </div>
                <div class="region-row__meta">
                  <span class="region-row__code" :title="row.code">{{ row.code || '无编码' }}</span>
                  <span>排序 {{ row.sortOrder }}</span>
                  <span class="region-row__state" :class="row.isActive ? 'is-on' : 'is-off'">
                    {{ row.isActive ? '启用' : '停用' }}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </section>
      </div>
    </NcCard>

    <el-dialog
      v-model="editor.visible"
      :title="editorTitle"
      width="min(520px, 94vw)"
      append-to-body
      @closed="resetEditor"
    >
      <el-form label-position="top" class="editor-form" @submit.prevent="submitEditor">
        <el-alert
          v-if="editor.error"
          type="error"
          :title="editor.error"
          :closable="false"
          show-icon
        />

        <el-form-item v-if="editorParentLabel" :label="editorParentLabel">
          <el-input :model-value="editorParentName" disabled />
        </el-form-item>

        <el-form-item label="名称" required>
          <el-input v-model="editor.name" maxlength="60" placeholder="请输入名称" />
        </el-form-item>

        <el-form-item label="编码">
          <el-input v-model="editor.code" maxlength="40" placeholder="行政区划代码（可留空）" />
        </el-form-item>

        <el-form-item v-if="editorTypeLabel" :label="editorTypeLabel">
          <el-input v-model="editor.type" maxlength="20" :placeholder="editorTypePlaceholder" />
        </el-form-item>

        <div class="editor-row">
          <el-form-item label="排序">
            <el-input-number v-model="editor.sortOrder" :min="0" :step="1" step-strictly />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="editor.isActive" />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="editor.visible = false">取消</el-button>
        <el-button type="primary" :loading="editor.submitting" @click="submitEditor">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.region-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.region-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.region-toolbar__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.08em;
}

.region-toolbar__select {
  width: 260px;
}

.region-toolbar__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.region-error {
  margin: 0 0 12px;
  color: #b42318;
  font-size: var(--nc-text-sm);
}

.region-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.region-column {
  display: flex;
  min-width: 0;
  flex-direction: column;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  overflow: hidden;
}

.column-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  padding: 12px 12px 10px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-2);
}

.column-head__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 3px;
}

.column-head__text strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  letter-spacing: 0.02em;
}

.column-head__text small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.column-body {
  min-height: 240px;
  max-height: 540px;
  padding: 8px;
  overflow: auto;
}

.column-hint {
  padding: 30px 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  text-align: center;
}

.column-error {
  margin: 0;
  padding: 14px 10px;
  color: #b42318;
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.region-list {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.region-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 3px 6px;
  padding: 8px 9px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  cursor: pointer;
  transition: border-color 0.15s ease, background-color 0.15s ease;
}

.region-row:hover {
  border-color: var(--nc-line);
  background-color: #fff;
}

.region-row:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.region-row.is-selected {
  border-color: rgba(171, 50, 39, 0.36);
  background-color: var(--nc-seal-wash);
}

.region-row__name {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 5px;
}

.region-row__name strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.region-row__type {
  flex: none;
  padding: 0 4px;
  border: 1px solid rgba(171, 50, 39, 0.22);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-seal-dark);
  font-size: 10px;
  line-height: 16px;
}

.region-row__actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 2px;
}

.region-row__actions :deep(.el-button) {
  padding: 0;
  font-size: var(--nc-text-xs);
}

.region-row__meta {
  display: flex;
  min-width: 0;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
  color: var(--nc-muted);
  font-size: 11px;
}

.region-row__code {
  overflow: hidden;
  font-family: var(--nc-font-mono);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.region-row__state.is-on {
  color: var(--nc-pine);
}

.region-row__state.is-off {
  color: var(--nc-seal);
  font-weight: 600;
}

.editor-form {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.editor-form :deep(.el-alert) {
  margin-bottom: 12px;
}

.editor-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

@media (max-width: 1280px) {
  .region-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .region-toolbar__select {
    width: 100%;
  }

  .region-grid {
    grid-template-columns: 1fr;
  }

  .column-body {
    max-height: 380px;
  }

  .editor-row {
    grid-template-columns: 1fr;
    gap: 0;
  }
}
</style>
