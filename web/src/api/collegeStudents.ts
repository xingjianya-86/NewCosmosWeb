import type {
  CollegeStudentCreated,
  CollegeStudentFormRequest,
  CollegeStudentListItem,
  EligibleCollegeMember,
} from '@/types/collegeStudents'
import { apiHttp } from './http'

/** 全部大学生档案（COLLEGE_MANAGE）。keyword 仅按姓名/身份证精确或模糊匹配。 */
export function listCollegeStudents(keyword?: string): Promise<CollegeStudentListItem[]> {
  return apiHttp
    .get<CollegeStudentListItem[]>('/social-assistance/college-students', {
      params: keyword ? { keyword } : undefined,
    })
    .then((response) => response.data ?? [])
}

/** 可关联的 18-23 周岁家庭成员（未建立大学生档案）。 */
export function searchEligibleCollegeMembers(
  keyword?: string,
): Promise<EligibleCollegeMember[]> {
  return apiHttp
    .get<EligibleCollegeMember[]>('/social-assistance/college-students/eligible-members', {
      params: keyword ? { keyword } : undefined,
    })
    .then((response) => response.data ?? [])
}

/** 今年毕业的在读/已毕业大学生（年度毕业提醒）。 */
export function listGraduatingCollegeStudents(
  year?: number,
): Promise<CollegeStudentListItem[]> {
  return apiHttp
    .get<CollegeStudentListItem[]>('/social-assistance/college-students/graduating', {
      params: year ? { year } : undefined,
    })
    .then((response) => response.data ?? [])
}

/** 关联新增大学生档案。 */
export function createCollegeStudent(
  request: CollegeStudentFormRequest,
): Promise<CollegeStudentCreated> {
  return apiHttp
    .post<CollegeStudentCreated>('/social-assistance/college-students', request)
    .then((response) => response.data)
}

/** 编辑大学生档案（学校/学历/学制/入学年份/状态/备注）。 */
export function updateCollegeStudent(
  id: number | string,
  request: CollegeStudentFormRequest,
): Promise<void> {
  return apiHttp
    .put(`/social-assistance/college-students/${encodeURIComponent(String(id))}`, request)
    .then(() => undefined)
}

/** 删除大学生档案（服务端软删除）。 */
export function deleteCollegeStudent(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/social-assistance/college-students/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}
