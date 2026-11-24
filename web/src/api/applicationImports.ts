import { apiHttp } from './http'

/** 农业补贴导入记录（对应 Core SubsidyImportRecord）。 */
export interface SubsidyImportRecord {
  name: string
  idCard: string
  subsidyType: string
  area: number
  amount: number
}

/** 土地确权台账导入记录（对应 Core LandConfirmationRecord）。 */
export interface LandConfirmationImportRecord {
  memberName: string
  memberIdCard: string
  landPlotInfo: string
  plotCode: string
  contractArea: number
  measuredArea: number
  landArea: number
  landUsage: string
  unitPrice: number
  isImported: boolean
}

function importsUrl(id: number | string, segment: string): string {
  return `/applications/${encodeURIComponent(String(id))}/imports/${segment}`
}

/** 按当前申请人 + 共同生活成员身份证号查询农业补贴。 */
export function getApplicationSubsidyImports(
  id: number | string,
): Promise<SubsidyImportRecord[]> {
  return apiHttp
    .get<SubsidyImportRecord[]>(importsUrl(id, 'subsidies'))
    .then((response) => response.data ?? [])
}

/** 按当前申请人 + 共同生活成员姓名查询土地确权台账。 */
export function getApplicationLandConfirmationImports(
  id: number | string,
): Promise<LandConfirmationImportRecord[]> {
  return apiHttp
    .get<LandConfirmationImportRecord[]>(importsUrl(id, 'land-confirmation'))
    .then((response) => response.data ?? [])
}
