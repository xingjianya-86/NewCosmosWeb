import { computed, ref } from 'vue'
import {
  listDocumentTemplates,
  type CreatedPrintJob,
  type DocumentTemplate,
  type PrintJobMode,
} from './archivePrint'
import { apiHttp, getErrorMessage } from './http'

/** 资产核查推送打印的业务类型键，与后端 PrintBusinessTypes.AssetVerification 一致。 */
export const ASSET_PRINT_BUSINESS_TYPE = 'AssetVerification'

export type { DocumentTemplate, PrintJobMode, CreatedPrintJob }

export interface CreateAssetPrintJobInput {
  /** nc_biz_asset_checks.id（某一条成员行），后端按 batch_id 取整户数据。 */
  businessId: number
  templateId: number
  templateName?: string
  mode?: PrintJobMode
  copies?: number
  isDuplex?: boolean
}

/** 资产核查可用文书模板（授权承诺书/一次性告知书/咨询台账等，由后端按业务域筛选）。 */
export function listAssetPrintTemplates(): Promise<DocumentTemplate[]> {
  return listDocumentTemplates(ASSET_PRINT_BUSINESS_TYPE)
}

/** 推送资产核查打印任务（预览/打印共用同一份数据快照）。 */
export function createAssetPrintJob(input: CreateAssetPrintJobInput): Promise<CreatedPrintJob> {
  return apiHttp
    .post<unknown>('/print/jobs', {
      businessType: ASSET_PRINT_BUSINESS_TYPE,
      businessId: input.businessId,
      templateId: input.templateId,
      templateName: input.templateName,
      copies: Math.min(99, Math.max(1, input.copies ?? 1)),
      isDuplex: input.isDuplex ?? false,
      mode: input.mode ?? 'print',
    })
    .then((response) => {
      const raw = (response.data ?? {}) as Record<string, unknown>
      const mode = String(raw.mode ?? input.mode ?? 'print').toLowerCase() === 'preview'
        ? 'preview'
        : 'print'
      return {
        id: Number(raw.id ?? 0),
        jobNo: typeof raw.jobNo === 'string' ? raw.jobNo : '',
        status: typeof raw.status === 'string' ? raw.status : 'Pending',
        jobKind: typeof raw.jobKind === 'string' ? raw.jobKind : 'Template',
        mode,
      } satisfies CreatedPrintJob
    })
}

/**
 * 打印对话框共用状态与提交逻辑（列表页与详情抽屉复用同一套表单与接口）。
 * 模板按业务域懒加载并缓存；提交成功后由调用方提示并刷新列表。
 */
export function useAssetPrintDialog() {
  const templates = ref<DocumentTemplate[]>([])
  const templatesLoading = ref(false)
  const templatesError = ref('')
  const submitting = ref(false)
  const templateId = ref<number | null>(null)
  const mode = ref<PrintJobMode>('print')
  const copies = ref(1)
  const isDuplex = ref(false)

  const selectedTemplate = computed(
    () => templates.value.find((template) => template.id === templateId.value) ?? null,
  )
  const canSubmit = computed(
    () => templateId.value !== null && !submitting.value && !templatesLoading.value,
  )

  async function loadTemplates(force = false): Promise<void> {
    if (!force && templates.value.length > 0) return

    templatesLoading.value = true
    templatesError.value = ''
    try {
      const list = await listAssetPrintTemplates()
      templates.value = list
      if (templateId.value === null || !list.some((item) => item.id === templateId.value)) {
        templateId.value = list[0]?.id ?? null
      }
    } catch (error) {
      templates.value = []
      templateId.value = null
      templatesError.value = getErrorMessage(error, '打印模板加载失败')
    } finally {
      templatesLoading.value = false
    }
  }

  function reset(): void {
    templateId.value = templates.value[0]?.id ?? null
    mode.value = 'print'
    copies.value = 1
    isDuplex.value = false
  }

  async function open(): Promise<void> {
    reset()
    await loadTemplates()
  }

  async function submit(businessId: number): Promise<CreatedPrintJob> {
    if (businessId <= 0) {
      throw new Error('核对记录 id 无效')
    }
    if (templateId.value === null) {
      throw new Error('请选择打印模板')
    }

    submitting.value = true
    try {
      return await createAssetPrintJob({
        businessId,
        templateId: templateId.value,
        templateName: selectedTemplate.value?.name,
        mode: mode.value,
        copies: copies.value,
        isDuplex: isDuplex.value,
      })
    } finally {
      submitting.value = false
    }
  }

  return {
    templates,
    templatesLoading,
    templatesError,
    submitting,
    templateId,
    mode,
    copies,
    isDuplex,
    selectedTemplate,
    canSubmit,
    loadTemplates,
    reset,
    open,
    submit,
  }
}
