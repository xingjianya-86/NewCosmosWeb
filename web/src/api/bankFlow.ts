import { apiHttp } from './http'

export interface BankFlowImportBatch {
  id: number
  batchNo: string
  sourceFileName: string
  totalCount: number
  exceedCount: number
  invalidIdCardCount: number
  exceedThreshold: number
  createdAt: string
}

export interface BankFlowRecord {
  id: number
  rowNo: number | null
  personName: string
  idCard: string
  idCardValid: boolean
  relationship: string
  familyAddress: string
  totalBalance: number
  maxFlowAmount: number
  bankSummary: string
  isExceed: boolean
  machineryPower: number | null
  totalBalanceDisplay: string
  maxFlowAmountDisplay: string
  machineryPowerDisplay: string
  idCardValidDisplay: string
}

export interface BankFlowRecordPage {
  items: BankFlowRecord[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface BankFlowQuery {
  keyword?: string
  importId?: number | null
  exceedOnly?: boolean
  page?: number
  pageSize?: number
}

export function listBankFlowImports(): Promise<BankFlowImportBatch[]> {
  return apiHttp
    .get<BankFlowImportBatch[]>('/bank-flow/imports')
    .then((response) => response.data ?? [])
}

export function listBankFlowRecords(params: BankFlowQuery = {}): Promise<BankFlowRecordPage> {
  return apiHttp
    .get<BankFlowRecordPage>('/bank-flow/records', {
      params: {
        keyword: params.keyword?.trim() || undefined,
        importId: params.importId ?? undefined,
        exceedOnly: params.exceedOnly || undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 20,
      },
    })
    .then((response) => response.data)
}

export function exportBankFlow(params: BankFlowQuery = {}): Promise<Blob> {
  return apiHttp
    .get<Blob>('/bank-flow/export', {
      params: {
        keyword: params.keyword?.trim() || undefined,
        importId: params.importId ?? undefined,
        exceedOnly: params.exceedOnly || undefined,
      },
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}
