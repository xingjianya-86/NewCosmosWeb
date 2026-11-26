import type {
  TemplateAdminCreateInput,
  TemplateAdminCreateResult,
  TemplateAdminDetail,
  TemplateAdminListItem,
  TemplateAdminMutationResult,
  TemplateAdminUpdateInput,
  TemplateCategoryCreateInput,
  TemplateCategoryMutationResult,
  TemplateCategoryNode,
  TemplateCategoryUpdateInput,
  TemplateMoveDirection,
} from '@/types/templateAdmin'
import { apiHttp } from './http'

const CATEGORY_PATH = '/template-admin/categories'

function templatePath(id: number | string): string {
  return `/template-admin/${encodeURIComponent(String(id))}`
}

function categoryPath(id: number | string): string {
  return `${CATEGORY_PATH}/${encodeURIComponent(String(id))}`
}

/** 模板列表（关键词可匹配名称 / 文档类型）。 */
export function listTemplateAdmin(keyword?: string): Promise<TemplateAdminListItem[]> {
  return apiHttp
    .get<TemplateAdminListItem[]>('/template-admin', {
      params: { keyword: keyword?.trim() || undefined },
    })
    .then((response) => response.data ?? [])
}

/** 分类树（含自定义节点；内置节点 id=null / isBuiltIn=true）。 */
export function getTemplateCategories(): Promise<TemplateCategoryNode[]> {
  return apiHttp
    .get<TemplateCategoryNode[]>(CATEGORY_PATH)
    .then((response) => response.data ?? [])
}

/** 文档类型词表。 */
export function getTemplateDocTypes(): Promise<string[]> {
  return apiHttp
    .get<string[]>('/template-admin/doc-types')
    .then((response) => response.data ?? [])
}

/** 模板详情（含 configJson / categories / 默认份数 / 分类级份数 / 配置提示）。 */
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
): Promise<TemplateAdminCreateResult> {
  const form = new FormData()
  form.append('file', input.file)
  form.append('name', input.name)
  form.append('docType', input.docType)
  form.append('categories', JSON.stringify(input.categories))
  if (input.configJson !== undefined) form.append('configJson', input.configJson)
  return apiHttp
    .post<TemplateAdminCreateResult>('/template-admin', form, { timeout: 120_000 })
    .then((response) => response.data)
}

/** 更新模板（multipart，file / 字段均可选）；200 可能返回 configWarnings，204 无响应体。 */
export function updateTemplateAdmin(
  id: number | string,
  input: TemplateAdminUpdateInput,
): Promise<TemplateAdminMutationResult> {
  const form = new FormData()
  if (input.file) form.append('file', input.file)
  if (input.name !== undefined) form.append('name', input.name)
  if (input.docType !== undefined) form.append('docType', input.docType)
  if (input.categories !== undefined) {
    form.append('categories', JSON.stringify(input.categories))
  }
  if (input.configJson !== undefined) form.append('configJson', input.configJson)
  if (input.copies !== undefined) form.append('copies', String(input.copies))
  return apiHttp
    .put<TemplateAdminMutationResult>(templatePath(id), form, { timeout: 120_000 })
    .then((response) => response.data ?? {})
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

/** 新增自定义分类（parentPath 为空表示一级分类）。 */
export function createTemplateCategory(
  input: TemplateCategoryCreateInput,
): Promise<TemplateCategoryMutationResult> {
  return apiHttp
    .post<TemplateCategoryMutationResult>(CATEGORY_PATH, input)
    .then((response) => response.data)
}

/** 重命名自定义分类。 */
export function updateTemplateCategory(
  id: number | string,
  input: TemplateCategoryUpdateInput,
): Promise<TemplateCategoryMutationResult> {
  return apiHttp
    .put<TemplateCategoryMutationResult>(categoryPath(id), input)
    .then((response) => response.data)
}

/** 删除自定义分类；force=true 时同时移除模板引用（仅在被引用 409 时使用）。 */
export function deleteTemplateCategory(
  id: number | string,
  force = false,
): Promise<void> {
  return apiHttp
    .delete(categoryPath(id), {
      params: force ? { force: 'true' } : undefined,
    })
    .then(() => undefined)
}
