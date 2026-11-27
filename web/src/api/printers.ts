import type { BindPrinterRequest, PrinterBinding, PrinterDevice } from '@/types/api'
import { apiHttp } from './http'

export interface PrinterBindCode {
  /** 6 位绑定码，供 Agent 托盘「绑定本机打印机」输入。 */
  code: string
  /** 绑定码过期时间（ISO 字符串）。 */
  expiresAt: string
}

export function listPrinters(): Promise<PrinterBinding[]> {
  return apiHttp.get<PrinterBinding[]>('/printers').then((response) => response.data)
}

/** 生成一次性绑定码：由当前电脑的 Agent 读取本机打印机后写入绑定。 */
export function generatePrinterBindCode(): Promise<PrinterBindCode> {
  return apiHttp
    .post<PrinterBindCode>('/printers/bind-code')
    .then((response) => response.data)
}

export function listDevices(): Promise<PrinterDevice[]> {
  return apiHttp.get<PrinterDevice[]>('/printers/devices').then((response) => response.data)
}

export function bindPrinter(payload: BindPrinterRequest): Promise<PrinterBinding> {
  return apiHttp
    .post<PrinterBinding>('/printers/bind', payload)
    .then((response) => response.data)
}

export async function unbindPrinter(id: number): Promise<void> {
  await apiHttp.delete(`/printers/${id}`)
}

export function setDefaultPrinter(id: number): Promise<PrinterBinding> {
  return apiHttp
    .post<PrinterBinding>(`/printers/${id}/default`)
    .then((response) => response.data)
}

/** POST /api/devices/{deviceId}/local-ops 响应：入队后的任务 ID/任务号。 */
export interface LocalOpCreated {
  jobId: number
  jobNo: string
}

/**
 * 向指定 Agent 设备下发本机操作指令（LocalOp，复用打印任务队列通道）。
 * op 为指令名（如 zerotier.join），args 为可选参数对象。
 * 结果查询复用 GET /print/jobs（status / errorMessage）。
 */
export function createLocalOp(
  deviceId: number,
  op: string,
  args?: Record<string, unknown>,
): Promise<LocalOpCreated> {
  return apiHttp
    .post<LocalOpCreated>(`/devices/${deviceId}/local-ops`, { op, args })
    .then((response) => response.data)
}
