import { apiHttp } from './http'

/** 行政区划下拉选项（省/市/县/镇/村统一结构）。 */
export interface RegionOption {
  id: number
  name: string
}

// apiHttp 的 baseURL 已经是 `${API_BASE}/api`，这里只写剩余路径，
// 实际请求为 /api/regions/...（与其它 api 模块一致）。
function getRegionOptions(url: string): Promise<RegionOption[]> {
  return apiHttp.get<RegionOption[]>(url).then((response) => response.data ?? [])
}

/** 省级行政区（34 个）。 */
export function getProvinces(): Promise<RegionOption[]> {
  return getRegionOptions('/regions/provinces')
}

/** 按省取地级市。 */
export function getCities(provinceId: number): Promise<RegionOption[]> {
  if (!provinceId) return Promise.resolve([])
  return getRegionOptions(`/regions/cities?provinceId=${encodeURIComponent(String(provinceId))}`)
}

/** 按市取县区。 */
export function getCounties(cityId: number): Promise<RegionOption[]> {
  if (!cityId) return Promise.resolve([])
  return getRegionOptions(`/regions/counties?cityId=${encodeURIComponent(String(cityId))}`)
}

/** 按县区取乡镇/街道。 */
export function getTowns(countyId: number): Promise<RegionOption[]> {
  if (!countyId) return Promise.resolve([])
  return getRegionOptions(`/regions/towns?countyId=${encodeURIComponent(String(countyId))}`)
}

/** 按乡镇取村/社区。 */
export function getVillages(townId: number): Promise<RegionOption[]> {
  if (!townId) return Promise.resolve([])
  return getRegionOptions(`/regions/villages?townId=${encodeURIComponent(String(townId))}`)
}
