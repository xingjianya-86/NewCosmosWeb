/**
 * 死亡情况说明（死亡申报 → 身份聚合 → 文书出具）。
 *
 * 字段与 src/NewCosmos.Server 的 /api/death-statements 契约保持一致；
 * 上下文中的身份/提醒/联动兼容旧桌面端字段名，统一由 api/deathStatement.ts 归一化后再交给页面，
 * 页面只消费本文件里的稳定结构。
 */

/** 查询范围（账号组织解析；degraded=true 表示未绑定村社区、按全镇范围降级）。 */
export interface DeathStatementScope {
  village: string
  town: string
  degraded: boolean
  /** 降级说明（黄条）；为空表示未降级。 */
  degradedReason: string
}

/** 搜索行：申报台账 / 档案死亡登记 / 未申报候选合并后的展示行。 */
export interface DeathStatementSearchItem {
  /** 行主键（申报台账 id 或死亡登记 id）。 */
  id: number
  /** 申报台账 id；档案死亡登记候选行为 null。 */
  declarationId: number | null
  idCard: string
  personName: string
  /** 死亡日期（yyyy-MM-dd 或 ISO 字符串，页面统一转成日期时间输入值）。 */
  deathDate: string
  status: string
  statusDisplay: string
  source: string
  subtitle: string
  isDeclared: boolean
  isArchived: boolean
  isRevoked: boolean
}

/** 死者基本信息（档案 + 申报行聚合）。 */
export interface DeathStatementPerson {
  name: string
  idCard: string
  gender: string
  birthDate: string
  age: number | null
  category: string
  address: string
  relation: string
}

/** 在库身份（八源聚合，只读）。 */
export interface DeathStatementIdentity {
  source: string
  label: string
  status: string
  summary: string
}

/** 数据信息提醒（只读）。 */
export interface DeathStatementReminder {
  level: string
  text: string
  suggestion: string
}

/** 关联流程卡片（只读 + 预填跳转，不跨域写）。 */
export interface DeathStatementLinkage {
  domain: string
  label: string
  status: string
  statusDisplay: string
  navKey: string
  applicationId: number | null
  elderlyApplicationId: number | null
  canHandle: boolean
  done: boolean
}

/**
 * 已申报行的字段快照（context 可能直接平铺返回，也可能放在 declaration 子对象里；
 * 归一化后统一到这里，用于带出已申报信息）。
 */
export interface DeathStatementDeclarationSnapshot {
  /** 申报台账 id（手工录入按身份证汇总时用于进入文书输出）。 */
  declarationId: number | null
  declaredByName: string
  declaredByIdCard: string
  declaredByPhone: string
  declaredByType: string
  declaredRelation: string
  declaredAtText: string
  status: string
  statusDisplay: string
  hasDeclaration: boolean
  deathDateText: string
  deathTimeText: string
  deathReason: string
  deathPlace: string
  deathCertNo: string
  certType: string
  certIssueOrg: string
  villageName: string
  situationDesc: string
  remainsArrangement: string
  businessAgent: string
  businessMatters: string[]
  cremationDate: string
  cremationPlace: string
  cremationCertNo: string
  burialDate: string
  burialPlace: string
  familySituation: string
  disputeStatus: string
  disputeDesc: string
}

/** 身份聚合上下文（页面第 2 步与文书输出共用）。 */
export interface DeathStatementContext {
  person: DeathStatementPerson
  identities: DeathStatementIdentity[]
  reminders: DeathStatementReminder[]
  linkages: DeathStatementLinkage[]
  declaration: DeathStatementDeclarationSnapshot | null
  /** 跨村被剔除的身份记录数（只计数，不输出明细）。 */
  crossVillageCount: number
}

/**
 * 提交申报请求体（POST /api/death-statements/declare）。
 *
 * 服务端契约（src/NewCosmos.Server/Endpoints/DeathStatementEndpoints.cs）直接绑定
 * Core `DeathDeclareRequest`，因此主字段沿用旧桌面端命名；同时附带任务书里的
 * `personName / deathCertificateNo / businessMatters / sceneDescription / disposalNote /
 * disputeNote / village` 等别名，兼容按新契约实现的后端（未知字段由 System.Text.Json 忽略）。
 */
export interface DeathStatementDeclarePayload {
  /** 死者姓名（旧字段名，服务端主字段）。 */
  name: string
  /** 死者姓名（任务书契约别名）。 */
  personName: string
  idCard: string
  /** 死亡日期时间（yyyy-MM-dd HH:mm:ss；服务端按 Date + Time 拆分落库）。 */
  deathDate: string
  /** 死亡时刻（HH:mm）。 */
  deathTime: string
  deathReason: string
  /** 死亡证明编号（旧字段名，服务端主字段）。 */
  deathCertNo: string
  /** 死亡证明编号（任务书契约别名）。 */
  deathCertificateNo: string
  deathPlace: string
  relation: string
  /** 申报人类型 / 姓名 / 身份证 / 电话（旧字段名，服务端主字段）。 */
  declaredByType: string
  declaredByName: string
  declaredByIdCard: string
  declaredByPhone: string
  /** 发现（报告）人姓名 / 关系 / 电话（旧字段名，服务端主字段）。 */
  reporterName: string
  reporterRelation: string
  reporterPhone: string
  /** 发现（报告）人补充别名，便于后续接口扩展时识别。 */
  discovererName: string
  discovererRelation: string
  discovererPhone: string
  /** 现场所见（旧字段名 + 契约别名）。 */
  situationDesc: string
  sceneDescription: string
  /** 遗体处置「其他」分支说明（旧字段名 + 契约别名）。 */
  remainsArrangement: string
  disposalNote: string
  businessAgent: string
  /** 相关业务事项（旧字段为「、」拼接串，新契约为数组）。 */
  businessMatter: string
  businessMatters: string[]
  certType: string
  certIssueOrg: string
  /** 遗体处置分支（火化 / 安葬 / 其他；旧字段名 + 契约别名）。 */
  dispositionBranch: string
  disposalType: string
  cremationDate: string | null
  cremationPlace: string
  cremationCertNo: string
  burialDate: string | null
  burialPlace: string
  familySituation: string
  /** 矛盾纠纷（旧字段名 + 契约别名）。 */
  disputeStatus: string
  disputeType: string
  disputeDesc: string
  disputeNote: string
  /** 所在村（旧字段名 + 契约别名；镇级降级时必填）。 */
  villageName: string
  village: string
  operatorName: string
  channel: string
}

/** 提交申报结果。 */
export interface DeathStatementDeclareResult {
  id: number
  declarationId: number
  status: string
}

/** 联动待办任务（GET /api/death-statements/linkage-tasks）。 */
export interface DeathStatementLinkageTask {
  id: number
  declarationId: number
  personName: string
  idCard: string
  domain: string
  domainLabel: string
  status: string
  statusDisplay: string
  navKey: string
  createdAt: string
}

export type DeathStatementJobMode = 'preview' | 'print' | 'save'

/** 文书任务提交体（POST /api/death-statements/jobs）。 */
export interface DeathStatementJobInput {
  declarationId: number
  mode: DeathStatementJobMode
  copies: number
  isDuplex: boolean
  printerBindingId?: number | null
  printerName?: string
}

/** 文书任务创建结果；实际渲染/打印由本机 Agent 领取执行。 */
export interface DeathStatementJobResult {
  id: number
  jobNo: string
  status: string
  jobKind: string
}
