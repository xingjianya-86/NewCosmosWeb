import { computed, ref } from 'vue'
import {
  listDocumentTemplates,
  type CreatedPrintJob,
  type DocumentTemplate,
  type PrintJobMode,
} from './archivePrint'
import { apiHttp, getErrorMessage } from './http'
import { listPrinters } from './printers'
import type { PrinterBinding } from '@/types/api'

/** 资产核查推送打印的业务类型键，与后端 PrintBusinessTypes.AssetVerification 一致。 */
export const ASSET_PRINT_BUSINESS_TYPE = 'AssetVerification'

export type { DocumentTemplate, PrintJobMode, CreatedPrintJob }

export interface CreateAssetPrintJobInput {
  /** nc_biz_asset_checks.id（某一条成员行），后端按 batch_id 取整户数据。 */
  businessId: number
  templateId: number
  templateName?: string
  /** 打印机绑定 id；打印模式必填，预览模式可省略。 */
  bindingId?: number | null
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
      bindingId: input.bindingId ?? undefined,
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
  const printers = ref<PrinterBinding[]>([])
  const printersLoading = ref(false)
  const printersError = ref('')
  const submitting = ref(false)
  const templateId = ref<number | null>(null)
  const printerId = ref<number | null>(null)
  const mode = ref<PrintJobMode>('print')
  const copies = ref(1)
  const isDuplex = ref(false)

  const selectedTemplate = computed(
    () => templates.value.find((template) => template.id === templateId.value) ?? null,
  )
  const selectedPrinter = computed(
    () => printers.value.find((printer) => printer.id === printerId.value) ?? null,
  )
  /** 打印模式必须有打印机；预览模式（PC 端查看）可不选。 */
  const canSubmit = computed(
    () =>
      templateId.value !== null &&
      !submitting.value &&
      !templatesLoading.value &&
      (mode.value === 'preview' || printerId.value !== null),
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

  /** 加载当前账号的打印机绑定；默认选「默认且在线」→「默认」→「在线」→ 第一台。 */
  async function loadPrinters(force = false): Promise<void> {
    if (!force && printers.value.length > 0) return

    printersLoading.value = true
    printersError.value = ''
    try {
      const list = await listPrinters()
      printers.value = list

      if (printerId.value !== null && list.some((item) => item.id === printerId.value)) {
        return
      }
      const preferred =
        list.find((printer) => printer.isDefault && printer.online)
        ?? list.find((printer) => printer.isDefault)
        ?? list.find((printer) => printer.online)
        ?? list[0]
      printerId.value = preferred?.id ?? null
    } catch (error) {
      printers.value = []
      printerId.value = null
      printersError.value = getErrorMessage(error, '打印机列表加载失败')
    } finally {
      printersLoading.value = false
    }
  }

  function reset(): void {
    templateId.value = templates.value[0]?.id ?? null
    mode.value = 'print'
    copies.value = 1
    isDuplex.value = false
    if (printerId.value === null) {
      const preferred =
        printers.value.find((printer) => printer.isDefault && printer.online)
        ?? printers.value.find((printer) => printer.isDefault)
        ?? printers.value.find((printer) => printer.online)
        ?? printers.value[0]
      printerId.value = preferred?.id ?? null
    }
  }

  async function open(): Promise<void> {
    reset()
    await Promise.all([loadTemplates(), loadPrinters(true)])
  }

  async function submit(businessId: number): Promise<CreatedPrintJob> {
    if (businessId <= 0) {
      throw new Error('核对记录 id 无效')
    }
    if (templateId.value === null) {
      throw new Error('请选择打印模板')
    }
    if (mode.value === 'print' && printerId.value === null) {
      throw new Error(
        printers.value.length === 0
          ? '当前账号未绑定打印机，请先在本机 Agent 绑定'
          : '请选择打印机',
      )
    }

    submitting.value = true
    try {
      return await createAssetPrintJob({
        businessId,
        templateId: templateId.value,
        templateName: selectedTemplate.value?.name,
        bindingId: printerId.value,
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
    printers,
    printersLoading,
    printersError,
    submitting,
    templateId,
    printerId,
    mode,
    copies,
    isDuplex,
    selectedTemplate,
    selectedPrinter,
    canSubmit,
    loadTemplates,
    loadPrinters,
    reset,
    open,
    submit,
  }
}
