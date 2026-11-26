/**
 * 每月公示文档输出：字段与 src/NewCosmos.Server 公示接口契约保持一致。
 *
 * 预览（GET /api/publicity/families）只取 5 个导入库在保家庭；
 * 生成口径（GET /api/publicity/groups）为导入库 + 当前库在保 − 已退出，families 另有近亲属标记。
 */

/** 公示家庭行（公示场景不展示身份证号）。 */
export interface PublicityFamily {
  /** 户主身份证号（仅用于近亲属标记匹配，不展示）。 */
  idCard: string
  /** 户主姓名。 */
  headName: string
  /** 救助类别显示名（农村低保/城市低保/低保边缘/特困供养/刚性支出）。 */
  category: string
  /** 保障人数。 */
  familySize: number
  /** 基础保障金额（户/月）。 */
  baseAmount: number
  /** 分类施保金额（户/月）。 */
  classifiedAmount: number
  /** 合计金额（户/月）。 */
  totalAmount: number
  /** 备注（申请原因/供养方式等）。 */
  remark: string
  /**
   * 近亲属备案标记。
   *
   * 契约中仅生成口径名单返回该字段；预览名单缺省，前端按可选字段兼容。
   */
  nearRelative?: boolean | null
}

/** 按村分组的公示名单。 */
export interface PublicityVillage {
  village: string
  familyCount: number
  families: PublicityFamily[]
}

/** 预览名单 / 生成口径名单响应（两者结构一致）。 */
export interface PublicityFamilyListResponse {
  year: number
  month: number
  totalFamilies: number
  villages: PublicityVillage[]
}

/** 公示文档生成任务提交体（POST /api/publicity/jobs）。 */
export interface PublicityJobInput {
  year: number
  month: number
  /** 指定村；留空 = 生成全部村。 */
  villages?: string[]
}

/** 单个公示文档生成任务。 */
export interface PublicityJob {
  id: number
  jobNo: string
  village: string
  pageCount: number
  /** 任务状态；契约未返回时前端按「已提交」展示。 */
  status?: string
}

/** 公示文档生成任务创建结果；实际生成与落盘由本机 PC Agent 执行。 */
export interface PublicityJobResponse {
  jobs: PublicityJob[]
  /** 输出提示（输出目录 / 保存说明）。 */
  outputHint: string
}
