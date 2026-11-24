import type { BindPrinterRequest, PrinterBinding, PrinterDevice } from '@/types/api'
import { apiHttp } from './http'

export function listPrinters(): Promise<PrinterBinding[]> {
  return apiHttp.get<PrinterBinding[]>('/printers').then((response) => response.data)
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
