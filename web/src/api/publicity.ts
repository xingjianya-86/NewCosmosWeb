import type {
  PublicityFamilyListResponse,
  PublicityJobInput,
  PublicityJobResponse,
} from '@/types/publicity'
import { apiHttp } from './http'

/** 预览公示名单：仅取 5 个导入库在保家庭（不参考当前库）。 */
export function getPublicityFamilies(
  year: number,
  month: number,
): Promise<PublicityFamilyListResponse> {
  return apiHttp
    .get<PublicityFamilyListResponse>('/publicity/families', { params: { year, month } })
    .then((response) => response.data)
}

/** 生成口径名单：导入库 + 当前库在保 − 已退出（月度口径）。 */
export function getPublicityGroups(
  year: number,
  month: number,
): Promise<PublicityFamilyListResponse> {
  return apiHttp
    .get<PublicityFamilyListResponse>('/publicity/groups', { params: { year, month } })
    .then((response) => response.data)
}

/**
 * 创建公示文档生成任务（按村逐份生成《公共_每月公示名单》）。
 *
 * 实际渲染、落盘与打开输出目录由本机 PC Agent 领取执行；Web 不内嵌文件。
 */
export function createPublicityJobs(
  input: PublicityJobInput,
): Promise<PublicityJobResponse> {
  return apiHttp
    .post<PublicityJobResponse>('/publicity/jobs', input)
    .then((response) => response.data)
}
