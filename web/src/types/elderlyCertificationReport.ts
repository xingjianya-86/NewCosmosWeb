/**
 * 高龄年度认证报表（v1，Excel 先行）：key/口径严格对齐
 * docs/API_CONTRACT_ELDERLY_CERTIFICATION_REPORT.md，不得在前端自行发明。
 *
 * 本批只做 Excel（.xlsx）导出；PDF/打印在下一批接 Agent/PrintJob。
 */

export type CertificationReportFormKey = 'ROSTER' | 'SUMMARY' | 'UNVERIFIED'

export interface CertificationReportFormOption {
  key: CertificationReportFormKey
  name: string
  description: string
}

/** 合同 1. 报表 key 与数据范围说明（顺序即页面展示顺序）。 */
export const CERTIFICATION_REPORT_FORMS: readonly CertificationReportFormOption[] = [
  {
    key: 'ROSTER',
    name: '年度认证花名册',
    description: '批次全部任务逐人明细：姓名、身份证号、认证方式与结果、凭证状态、停发决定等。',
  },
  {
    key: 'SUMMARY',
    name: '年度认证情况汇总表',
    description: '按乡镇/村社区聚合应认证、已通过、未通过、凭证异常与认证率，末行为合计。',
  },
  {
    key: 'UNVERIFIED',
    name: '未认证人员名单',
    description: '待核查/拒绝核查/核查不通过/无法联系且未停发的人员，已排除停发决定为 Approved 的。',
  },
]
