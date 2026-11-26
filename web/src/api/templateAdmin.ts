import type {
  TemplateAdminCreateInput,
  TemplateAdminDetail,
  TemplateAdminListItem,
  TemplateAdminUpdateInput,
  TemplateCategoryNode,
  TemplateMoveDirection,
} from '@/types/templateAdmin'
import { apiHttp } from './http'

function templatePath(id: number | string): string {
  return `/template-admin/${encodeURIComponent(String(id))}`
}

/** 模板列表（关键词可匹配名称 / 文档类型）。 */
export function listTemplateAdmin(keyword?: string): Promise<TemplateAdminListItem[]> {
  return apiHttp
    .get<TemplateAdminListItem[]>('/template-admin', {
      params: { keyword: keyword?.trim() || undefined },
    })
    .then((response) => response.data ?? [])
}

/** 分类树。 */
export function getTemplateCategories(): Promise<TemplateCategoryNode[]> {
  return apiHttp
    .get<TemplateCategoryNode[]>('/template-admin/categories')
    .then((response) => response.data ?? [])
}

/** 文档类型词表。 */
export function getTemplateDocTypes(): Promise<string[]> {
  return apiHttp
    .get<string[]>('/template-admin/doc-types')
    .then((response) => response.data ?? [])
}

/** 模板详情（含 configJson / categories / 默认份数）。 */
export function getTemplateAdmin(id: number | string): Promise<TemplateAdminDetail> {
  return apiHttp.get<TemplateAdminDetail>(templatePath(id)).then((response) => response.data)
}

/** 模板文件下载。 */
export function downloadTemplateAdminFile(id: number | string): Promise<Blob> {
  return apiHttp
    .get<Blob>(`${templatePath(id)}/file`, {
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}

/** 上传模板（multipart）。 */
export function createTemplateAdmin(
  input: TemplateAdminCreateInput,
): Promise<{ id: number }> {
  const form = new FormData()
  form.append('file', input.file)
  form.append('name', input.name)
  form.append('docType', input.docType)
  form.append('categories', JSON.stringify(input.categories))
  if (input.configJson !== undefined) form.append('configJson', input.configJson)
  return apiHttp
    .post<{ id: number }>('/template-admin', form, { timeout: 120_000 })
    .then((response) => response.data)
}

/** 更新模板（multipart，file / 字段均可选）。 */
export function updateTemplateAdmin(
  id: number | string,
  input: TemplateAdminUpdateInput,
): Promise<void> {
  const form = new FormData()
  if (input.file) form.append('file', input.file)
  if (input.name !== undefined) form.append('name', input.name)
  if (input.docType !== undefined) form.append('docType', input.docType)
  if (input.categories !== undefined) {
    form.append('categories', JSON.stringify(input.categories))
  }
  if (input.configJson !== undefined) form.append('configJson', input.configJson)
  if (input.copies !== undefined) form.append('copies', String(input.copies))
  return apiHttp.put(templatePath(id), form, { timeout: 120_000 }).then(() => undefined)
}

/** 删除模板。 */
export function deleteTemplateAdmin(id: number | string): Promise<void> {
  return apiHttp.delete(templatePath(id)).then(() => undefined)
}

/** 排序上移 / 下移。 */
export function moveTemplateAdmin(
  id: number | string,
  direction: TemplateMoveDirection,
): Promise<void> {
  return apiHttp
    .post(`${templatePath(id)}/move`, null, { params: { direction } })
    .then(() => undefined)
}
