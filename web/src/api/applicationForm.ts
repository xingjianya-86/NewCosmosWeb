import type {
  ApplicationFormClassification,
  ApplicationFormModel,
} from '@/views/applications/form/types'
import { apiHttp } from './http'

/** 保存草稿返回；status/statusText 为可选，后端未返回时由前端保持原值。 */
export interface SaveApplicationFormResult {
  id: number
  applicationNo: string
  status?: string
  statusText?: string
}

/** 完成认定返回。 */
export interface CompleteApplicationResult {
  status: string
  currentStep: number
}

/** 分类认定可能返回完整表单，也可能只返回 classification。 */
export type ClassifyApplicationResult = ApplicationFormModel | ApplicationFormClassification

/** 判断分类认定接口返回的是完整表单还是单段 classification。 */
export function isFullApplicationForm(
  value: ClassifyApplicationResult,
): value is ApplicationFormModel {
  return (
    typeof value === 'object' &&
    value !== null &&
    'basic' in value &&
    'members' in value &&
    'classification' in value
  )
}

function formUrl(id: number | string): string {
  return `/applications/${encodeURIComponent(String(id))}/form`
}

/** 读取五步表单（编辑模式）。 */
export function getApplicationForm(id: number | string): Promise<ApplicationFormModel> {
  return apiHttp.get<ApplicationFormModel>(formUrl(id)).then((response) => response.data)
}

/** 新建草稿（id=0 时走此接口）。 */
export function createApplicationForm(
  form: ApplicationFormModel,
): Promise<SaveApplicationFormResult> {
  return apiHttp
    .post<SaveApplicationFormResult>('/applications/form', form)
    .then((response) => response.data)
}

/** 更新草稿。 */
export function updateApplicationForm(
  id: number | string,
  form: ApplicationFormModel,
): Promise<SaveApplicationFormResult> {
  return apiHttp
    .put<SaveApplicationFormResult>(formUrl(id), form)
    .then((response) => response.data)
}

/** 分类认定；body 可选，外壳会先保存再调用。 */
export function classifyApplication(
  id: number | string,
  form?: ApplicationFormModel,
): Promise<ClassifyApplicationResult> {
  const url = `/applications/${encodeURIComponent(String(id))}/classify`
  return apiHttp
    .post<ClassifyApplicationResult>(url, form)
    .then((response) => response.data)
}

/** 完成认定并生成档案。 */
export function completeApplication(id: number | string): Promise<CompleteApplicationResult> {
  return apiHttp
    .post<CompleteApplicationResult>(`/applications/${encodeURIComponent(String(id))}/complete`)
    .then((response) => response.data)
}

/** 删除申请。 */
export function deleteApplication(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/applications/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}
