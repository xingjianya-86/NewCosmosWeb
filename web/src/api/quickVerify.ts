import type {
  QuickVerifyReportRequest,
  QuickVerifyReportResponse,
  QuickVerifyResponse,
  QuickVerifySimulationInput,
  QuickVerifySimulationResult,
  QuickVerifyStandards,
} from '@/types/quickVerify'
import { apiHttp } from './http'

/** 按身份证（+可选姓名）核验 10 组台账（INCOME_VIEW；只读）。 */
export function getQuickVerify(idCard: string, name?: string): Promise<QuickVerifyResponse> {
  return apiHttp
    .get<QuickVerifyResponse>('/social-assistance/quick-verify', {
      params: { idCard, name: name?.trim() || undefined },
    })
    .then((response) => response.data)
}

/** 读取测算所需标准（低保档 / 边缘上限 / 赡养费默认值 / 土地单价）。 */
export function getQuickVerifyStandards(
  hukou: string,
  year?: number,
): Promise<QuickVerifyStandards> {
  return apiHttp
    .get<QuickVerifyStandards>('/social-assistance/quick-verify/standards', {
      params: { hukou, year },
    })
    .then((response) => response.data)
}

/** 收入模拟测算（公式全部在 Core，前端只提交输入并渲染结果）。 */
export function simulateQuickVerify(
  payload: QuickVerifySimulationInput,
): Promise<QuickVerifySimulationResult> {
  return apiHttp
    .post<QuickVerifySimulationResult>('/social-assistance/quick-verify/simulate', payload)
    .then((response) => response.data)
}

/** 生成三段式简易报告（文本由 Core 生成，前端只复制/打印）。 */
export function buildQuickVerifyReport(
  payload: QuickVerifyReportRequest,
): Promise<QuickVerifyReportResponse> {
  return apiHttp
    .post<QuickVerifyReportResponse>('/social-assistance/quick-verify/report', payload)
    .then((response) => response.data)
}
