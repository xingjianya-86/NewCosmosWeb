import { apiHttp } from './http'

/** 共享 ICD-10 查询结果（低收入认定 / 临时救助共用）。 */
export interface Icd10LookupResult {
  code: string
  name: string
  category: string
}

/** 编码 → 常见病名 + 一级疾病分类；未命中时 name 为空，不阻断手工录入。 */
export function getIcd10(code: string): Promise<Icd10LookupResult> {
  return apiHttp
    .get<Icd10LookupResult>('/icd10', { params: { code } })
    .then((response) => response.data)
}
