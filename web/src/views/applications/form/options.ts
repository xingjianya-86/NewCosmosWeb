/** 表单下拉选项；值与后端枚举/文案约定保持一致（性别、关系为中文文案）。 */

export interface SelectOption {
  label: string
  value: string
}

export const GENDER_OPTIONS: SelectOption[] = [
  { label: '男', value: '男' },
  { label: '女', value: '女' },
]

export const HUKOU_TYPE_OPTIONS: SelectOption[] = [
  { label: '农村', value: 'Rural' },
  { label: '城市', value: 'Urban' },
]

export const ETHNICITY_OPTIONS: SelectOption[] = [
  { label: '汉族', value: 'Han' },
  { label: '满族', value: 'Manchu' },
  { label: '朝鲜族', value: 'Korean' },
  { label: '蒙古族', value: 'Mongolian' },
  { label: '回族', value: 'Hui' },
  { label: '达斡尔族', value: 'Daur' },
  { label: '锡伯族', value: 'Xibe' },
  { label: '其他', value: 'Other' },
]

export const RELATION_OPTIONS: SelectOption[] = [
  { label: '户主', value: '户主' },
  { label: '配偶', value: '配偶' },
  { label: '子女', value: '子女' },
  { label: '父母', value: '父母' },
  { label: '兄弟姐妹', value: '兄弟姐妹' },
  { label: '祖父母', value: '祖父母' },
  { label: '孙子女', value: '孙子女' },
  { label: '其他', value: '其他' },
]

export const HEALTH_STATUS_OPTIONS: SelectOption[] = [
  { label: '健康', value: '健康' },
  { label: '一般', value: '一般' },
  { label: '患病', value: '患病' },
  { label: '残疾', value: '残疾' },
  { label: '重病', value: '重病' },
]

export const EMPLOYMENT_STATUS_OPTIONS: SelectOption[] = [
  { label: '务农', value: '务农' },
  { label: '务工', value: '务工' },
  { label: '在职', value: '在职' },
  { label: '个体经营', value: '个体经营' },
  { label: '学生', value: '学生' },
  { label: '无业', value: '无业' },
  { label: '退休', value: '退休' },
  { label: '其他', value: '其他' },
]

export const DISABILITY_TYPE_OPTIONS: SelectOption[] = [
  { label: '视力残疾', value: '视力' },
  { label: '听力残疾', value: '听力' },
  { label: '言语残疾', value: '言语' },
  { label: '肢体残疾', value: '肢体' },
  { label: '智力残疾', value: '智力' },
  { label: '精神残疾', value: '精神' },
  { label: '多重残疾', value: '多重' },
]

export const DISABILITY_LEVEL_OPTIONS: SelectOption[] = [
  { label: '一级', value: '一级' },
  { label: '二级', value: '二级' },
  { label: '三级', value: '三级' },
  { label: '四级', value: '四级' },
]

export const APPLICATION_REASON_OPTIONS: SelectOption[] = [
  { label: '因病', value: 'Illness' },
  { label: '因灾', value: 'Disaster' },
  { label: '因残', value: 'Disability' },
  { label: '因学', value: 'Education' },
  { label: '收入低', value: 'LowIncome' },
  { label: '失业', value: 'Unemployment' },
  { label: '失地', value: 'LandLoss' },
  { label: '因意外事故', value: 'Accident' },
  { label: '其他', value: 'Other' },
]

export const SUPPORT_RELATION_OPTIONS: SelectOption[] = [
  { label: '子女', value: '子女' },
  { label: '配偶', value: '配偶' },
  { label: '父母', value: '父母' },
  { label: '兄弟姐妹', value: '兄弟姐妹' },
  { label: '孙子女', value: '孙子女' },
  { label: '其他', value: '其他' },
]
