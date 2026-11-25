<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
  getCities,
  getCounties,
  getProvinces,
  getTowns,
  getVillages,
  type RegionOption,
} from '@/api/regions'

/**
 * 省/市/县/镇/村五级地址级联。
 *
 * - v-model 语义：每一级都以**名称**双向绑定（update:province/city/district/town/village），
 *   与表单里已有的文本字段一致；内部按名称匹配 nc_regions_* 的主键来加载下一级。
 * - 旧数据只有文本没有 ID 时按名称匹配；匹配不到就把原值作为下拉的“当前项”保留，避免空白。
 * - 省份只读时用 el-input readonly 展示（历史档案的省份通常不可改）。
 */
const props = withDefaults(
  defineProps<{
    province: string
    city: string
    district: string
    town: string
    village: string
    provinceReadonly?: boolean
    disabled?: boolean
    /** 0=不锁定；1=省、2=市、3=县、4=镇、5=村，该级及以上全部锁定。 */
    lockedLevel?: number
  }>(),
  {
    provinceReadonly: false,
    disabled: false,
    lockedLevel: 0,
  },
)

const emit = defineEmits<{
  'update:province': [value: string]
  'update:city': [value: string]
  'update:district': [value: string]
  'update:town': [value: string]
  'update:village': [value: string]
}>()

const provinces = ref<RegionOption[]>([])
const cities = ref<RegionOption[]>([])
const counties = ref<RegionOption[]>([])
const towns = ref<RegionOption[]>([])
const villages = ref<RegionOption[]>([])

// 已经加载过下级的父级 ID，避免同名值反复触发请求。
const loaded = {
  provinceId: null as number | null,
  cityId: null as number | null,
  countyId: null as number | null,
  townId: null as number | null,
}

// 快速连续选择上级时，旧请求返回后不得覆盖新结果。
let syncToken = 0

/** 省份只读时仍可能需要用 el-input 展示，这里保证选项里也有当前值。 */
function withLegacyValue(options: RegionOption[], value: string): RegionOption[] {
  const name = (value ?? '').trim()
  if (!name) return options
  if (options.some((option) => option.name.trim() === name)) return options
  return [{ id: -1, name }, ...options]
}

const provinceLocked = computed(() => props.lockedLevel >= 1)
const cityLocked = computed(() => props.lockedLevel >= 2)
const countyLocked = computed(() => props.lockedLevel >= 3)
const townLocked = computed(() => props.lockedLevel >= 4)
const villageLocked = computed(() => props.lockedLevel >= 5)

const provinceOptions = computed(() => withLegacyValue(provinces.value, props.province))
const cityOptions = computed(() => withLegacyValue(cities.value, props.city))
const countyOptions = computed(() => withLegacyValue(counties.value, props.district))
const townOptions = computed(() => withLegacyValue(towns.value, props.town))
const villageOptions = computed(() => withLegacyValue(villages.value, props.village))

/** 去掉行政区划常见后缀，兼容“林口”/“林口县”这类历史文本。 */
function normalizeRegionName(value: string): string {
  return value
    .trim()
    .replace(/(特别行政区|自治区|自治州|自治县|自治旗|街道|社区|村委会|居委会|省|市|区|县|镇|乡|村)$/u, '')
}

function resolveId(options: RegionOption[], value: string): number | null {
  const name = (value ?? '').trim()
  if (!name) return null

  const exact = options.find((option) => option.name.trim() === name)
  if (exact) return exact.id

  const normalized = normalizeRegionName(name)
  if (!normalized) return null
  const matched = options.find((option) => normalizeRegionName(option.name) === normalized)
  return matched ? matched.id : null
}

async function safeLoad(loader: () => Promise<RegionOption[]>): Promise<RegionOption[]> {
  try {
    return await loader()
  } catch {
    // 区划数据是辅助信息：接口失败时保持空列表，页面仍可手工填写文本。
    return []
  }
}

function resetBelow(level: 'province' | 'city' | 'district' | 'town'): void {
  if (level === 'province') {
    cities.value = []
    loaded.cityId = null
    level = 'city'
  }
  if (level === 'city') {
    counties.value = []
    loaded.countyId = null
    level = 'district'
  }
  if (level === 'district') {
    towns.value = []
    loaded.townId = null
    level = 'town'
  }
  villages.value = []
}

/** 按当前 props 里的名称逐级对齐选项；只在父级 ID 变化时请求下一级。 */
async function refreshOptions(): Promise<void> {
  const token = ++syncToken

  if (provinces.value.length === 0) {
    provinces.value = await safeLoad(getProvinces)
    if (token !== syncToken) return
  }

  const provinceId = resolveId(provinces.value, props.province)
  if (provinceId !== loaded.provinceId) {
    loaded.provinceId = provinceId
    resetBelow('province')
    if (provinceId != null) {
      cities.value = await safeLoad(() => getCities(provinceId))
      if (token !== syncToken) return
    }
  }

  const cityId = resolveId(cities.value, props.city)
  if (cityId !== loaded.cityId) {
    loaded.cityId = cityId
    resetBelow('city')
    if (cityId != null) {
      counties.value = await safeLoad(() => getCounties(cityId))
      if (token !== syncToken) return
    }
  }

  const countyId = resolveId(counties.value, props.district)
  if (countyId !== loaded.countyId) {
    loaded.countyId = countyId
    resetBelow('district')
    if (countyId != null) {
      towns.value = await safeLoad(() => getTowns(countyId))
      if (token !== syncToken) return
    }
  }

  const townId = resolveId(towns.value, props.town)
  if (townId !== loaded.townId) {
    loaded.townId = townId
    villages.value = []
    if (townId != null) {
      villages.value = await safeLoad(() => getVillages(townId))
    }
  }
}

function onProvinceChange(value: string): void {
  if (value === props.province) return
  emit('update:province', value ?? '')
  emit('update:city', '')
  emit('update:district', '')
  emit('update:town', '')
  emit('update:village', '')
}

function onCityChange(value: string): void {
  if (value === props.city) return
  emit('update:city', value ?? '')
  emit('update:district', '')
  emit('update:town', '')
  emit('update:village', '')
}

function onDistrictChange(value: string): void {
  if (value === props.district) return
  emit('update:district', value ?? '')
  emit('update:town', '')
  emit('update:village', '')
}

function onTownChange(value: string): void {
  if (value === props.town) return
  emit('update:town', value ?? '')
  emit('update:village', '')
}

function onVillageChange(value: string): void {
  if (value === props.village) return
  emit('update:village', value ?? '')
}

onMounted(() => {
  void refreshOptions()
})

watch(
  [() => props.province, () => props.city, () => props.district, () => props.town, () => props.village],
  () => {
    void refreshOptions()
  },
)
</script>

<template>
  <div class="address-cascader">
    <label class="address-cascader__field">
      <span class="address-cascader__label">省份</span>
      <el-input v-if="provinceReadonly" :model-value="province" readonly />
      <el-select
        v-else
        :model-value="province"
        filterable
        clearable
        placeholder="请选择省份"
        :disabled="disabled || provinceLocked"
        @change="onProvinceChange"
      >
        <el-option
          v-for="item in provinceOptions"
          :key="item.name"
          :label="item.name"
          :value="item.name"
        />
      </el-select>
    </label>

    <label class="address-cascader__field">
      <span class="address-cascader__label">城市</span>
      <el-select
        :model-value="city"
        filterable
        clearable
        placeholder="请选择城市"
        :disabled="disabled || cityLocked"
        @change="onCityChange"
      >
        <el-option v-for="item in cityOptions" :key="item.name" :label="item.name" :value="item.name" />
      </el-select>
    </label>

    <label class="address-cascader__field">
      <span class="address-cascader__label">区县</span>
      <el-select
        :model-value="district"
        filterable
        clearable
        placeholder="请选择区县"
        :disabled="disabled || countyLocked"
        @change="onDistrictChange"
      >
        <el-option
          v-for="item in countyOptions"
          :key="item.name"
          :label="item.name"
          :value="item.name"
        />
      </el-select>
    </label>

    <label class="address-cascader__field">
      <span class="address-cascader__label">乡镇</span>
      <el-select
        :model-value="town"
        filterable
        clearable
        placeholder="请选择乡镇/街道"
        :disabled="disabled || townLocked"
        @change="onTownChange"
      >
        <el-option v-for="item in townOptions" :key="item.name" :label="item.name" :value="item.name" />
      </el-select>
    </label>

    <label class="address-cascader__field">
      <span class="address-cascader__label">村社区</span>
      <el-select
        :model-value="village"
        filterable
        clearable
        placeholder="请选择村/社区"
        :disabled="disabled || villageLocked"
        @change="onVillageChange"
      >
        <el-option
          v-for="item in villageOptions"
          :key="item.name"
          :label="item.name"
          :value="item.name"
        />
      </el-select>
    </label>
  </div>
</template>

<style scoped>
.address-cascader {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px 16px;
  width: 100%;
}

.address-cascader__field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.address-cascader__label {
  font-size: var(--nc-text-sm, 13px);
  color: var(--nc-muted-2, #666);
}

.address-cascader__field :deep(.el-select),
.address-cascader__field :deep(.el-input) {
  width: 100%;
}

@media (max-width: 767px) {
  .address-cascader {
    grid-template-columns: 1fr;
  }
}
</style>
