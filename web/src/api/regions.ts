import { apiHttp } from './http'

/** 行政区划下拉选项（省/市/县/镇/村统一结构）。 */
export interface RegionOption {
  id: number
  name: string
}

export interface RegionProvince {
  id: number
  provinceName: string
  provinceCode?: string
  sortOrder?: number
  isActive?: boolean
}

export interface RegionCity {
  id: number
  cityName: string
  cityCode: string
  provinceId?: number | null
  sortOrder: number
  isActive: boolean
}

export interface RegionCounty {
  id: number
  countyName: string
  countyCode: string
  cityName: string
  sortOrder: number
  isActive: boolean
}

export interface RegionTown {
  id: number
  countyId: number
  townName: string
  townCode: string
  townType: string
  sortOrder: number
  isActive: boolean
}

export interface RegionVillage {
  id: number
  townId: number
  villageName: string
  villageCode: string
  villageType: string
  sortOrder: number
  isActive: boolean
}

export interface RegionCitySave {
  id?: number
  cityName: string
  cityCode: string
  provinceId?: number | null
  sortOrder: number
  isActive: boolean
}

export interface RegionCountySave {
  id?: number
  countyName: string
  countyCode: string
  cityName?: string
  sortOrder: number
  isActive: boolean
}

export interface RegionTownSave {
  id?: number
  countyId: number
  townName: string
  townCode: string
  townType?: string
  sortOrder: number
  isActive: boolean
}

export interface RegionVillageSave {
  id?: number
  townId: number
  villageName: string
  villageCode: string
  villageType?: string
  sortOrder: number
  isActive: boolean
}

// apiHttp 的 baseURL 已经包含 `/api`，这里只写剩余路径。
function getRegionOptions(url: string): Promise<RegionOption[]> {
  return apiHttp.get<RegionOption[]>(url).then((response) => response.data ?? [])
}

export function getProvinces(): Promise<RegionOption[]> {
  return getRegionOptions('/regions/provinces')
}

export function getCities(provinceId: number): Promise<RegionOption[]> {
  if (!provinceId) return Promise.resolve([])
  return getRegionOptions(`/regions/cities?provinceId=${encodeURIComponent(String(provinceId))}`)
}

export function getCounties(cityId: number): Promise<RegionOption[]> {
  if (!cityId) return Promise.resolve([])
  return getRegionOptions(`/regions/counties?cityId=${encodeURIComponent(String(cityId))}`)
}

export function getTowns(countyId: number): Promise<RegionOption[]> {
  if (!countyId) return Promise.resolve([])
  return getRegionOptions(`/regions/towns?countyId=${encodeURIComponent(String(countyId))}`)
}

export function getVillages(townId: number): Promise<RegionOption[]> {
  if (!townId) return Promise.resolve([])
  return getRegionOptions(`/regions/villages?townId=${encodeURIComponent(String(townId))}`)
}

/* ───────────────────────── 管理页 ───────────────────────── */

export function listManageProvinces(): Promise<RegionProvince[]> {
  return apiHttp.get<RegionProvince[]>('/regions/manage/provinces').then((response) => response.data ?? [])
}

export function listManageCities(provinceId: number): Promise<RegionCity[]> {
  if (!provinceId) return Promise.resolve([])
  return apiHttp
    .get<RegionCity[]>(`/regions/manage/cities?provinceId=${provinceId}`)
    .then((response) => response.data ?? [])
}

export function listManageCounties(cityId: number): Promise<RegionCounty[]> {
  if (!cityId) return Promise.resolve([])
  return apiHttp
    .get<RegionCounty[]>(`/regions/manage/counties?cityId=${cityId}`)
    .then((response) => response.data ?? [])
}

export function listManageTowns(countyId: number): Promise<RegionTown[]> {
  if (!countyId) return Promise.resolve([])
  return apiHttp
    .get<RegionTown[]>(`/regions/manage/towns?countyId=${countyId}`)
    .then((response) => response.data ?? [])
}

export function listManageVillages(townId: number): Promise<RegionVillage[]> {
  if (!townId) return Promise.resolve([])
  return apiHttp
    .get<RegionVillage[]>(`/regions/manage/villages?townId=${townId}`)
    .then((response) => response.data ?? [])
}

export function createCity(payload: RegionCitySave): Promise<RegionCity> {
  return apiHttp.post<RegionCity>('/regions/cities', payload).then((response) => response.data)
}

export function updateCity(id: number, payload: RegionCitySave): Promise<RegionCity> {
  return apiHttp.put<RegionCity>(`/regions/cities/${id}`, payload).then((response) => response.data)
}

export function deleteCity(id: number): Promise<void> {
  return apiHttp.delete(`/regions/cities/${id}`).then(() => undefined)
}

export function createCounty(payload: RegionCountySave): Promise<RegionCounty> {
  return apiHttp.post<RegionCounty>('/regions/counties', payload).then((response) => response.data)
}

export function updateCounty(id: number, payload: RegionCountySave): Promise<RegionCounty> {
  return apiHttp
    .put<RegionCounty>(`/regions/counties/${id}`, payload)
    .then((response) => response.data)
}

export function deleteCounty(id: number): Promise<void> {
  return apiHttp.delete(`/regions/counties/${id}`).then(() => undefined)
}

export function createTown(payload: RegionTownSave): Promise<RegionTown> {
  return apiHttp.post<RegionTown>('/regions/towns', payload).then((response) => response.data)
}

export function updateTown(id: number, payload: RegionTownSave): Promise<RegionTown> {
  return apiHttp.put<RegionTown>(`/regions/towns/${id}`, payload).then((response) => response.data)
}

export function deleteTown(id: number): Promise<void> {
  return apiHttp.delete(`/regions/towns/${id}`).then(() => undefined)
}

export function createVillage(payload: RegionVillageSave): Promise<RegionVillage> {
  return apiHttp
    .post<RegionVillage>('/regions/villages', payload)
    .then((response) => response.data)
}

export function updateVillage(id: number, payload: RegionVillageSave): Promise<RegionVillage> {
  return apiHttp
    .put<RegionVillage>(`/regions/villages/${id}`, payload)
    .then((response) => response.data)
}

export function deleteVillage(id: number): Promise<void> {
  return apiHttp.delete(`/regions/villages/${id}`).then(() => undefined)
}

export function syncRegionSchema(): Promise<void> {
  return apiHttp.post('/regions/sync-schema').then(() => undefined)
}

export function seedRegions(): Promise<void> {
  return apiHttp.post('/regions/seed').then(() => undefined)
}

export function clearRegions(): Promise<void> {
  return apiHttp.post('/regions/clear').then(() => undefined)
}
