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
  { label: '农村户口', value: 'Rural' },
  { label: '城市户口', value: 'Urban' },
]

/**
 * 民族：key 与旧版字典 Ethnicities（ethnicities.yaml）一致，显示中文。
 * 字典只有 19 项（无 Daur/Xibe），未知 key 由后端原样透传以兼容历史数据。
 */
export const ETHNICITY_OPTIONS: SelectOption[] = [
  { label: '汉族', value: 'Han' },
  { label: '蒙古族', value: 'Mongol' },
  { label: '回族', value: 'Hui' },
  { label: '藏族', value: 'Tibetan' },
  { label: '维吾尔族', value: 'Uyghur' },
  { label: '苗族', value: 'Miao' },
  { label: '彝族', value: 'Yi' },
  { label: '壮族', value: 'Zhuang' },
  { label: '布依族', value: 'Bouyei' },
  { label: '朝鲜族', value: 'Korean' },
  { label: '满族', value: 'Manchu' },
  { label: '侗族', value: 'Dong' },
  { label: '瑶族', value: 'Yao' },
  { label: '白族', value: 'Bai' },
  { label: '土家族', value: 'Tujia' },
  { label: '哈尼族', value: 'Hani' },
  { label: '傣族', value: 'Dai' },
  { label: '黎族', value: 'Li' },
  { label: '其他', value: 'Other' },
]

/**
 * 家庭关系：value 用旧版中文显示值（FamilyRelationships 字典），后端 ToCoreRelationship
 * 负责换算为 Head/Spouse/Son…；历史英文 key 也能被后端映射回中文。
 */
export const RELATION_OPTIONS: SelectOption[] = [
  { label: '本人/户主', value: '本人/户主' },
  { label: '配偶', value: '配偶' },
  { label: '子/婿', value: '子/婿' },
  { label: '女/媳', value: '女/媳' },
  { label: '孙子女/外孙子女', value: '孙子女/外孙子女' },
  { label: '父母/岳父母/公婆', value: '父母/岳父母/公婆' },
  { label: '祖父母/外祖父母', value: '祖父母/外祖父母' },
  { label: '兄弟姐妹', value: '兄弟姐妹' },
  { label: '其他', value: '其他' },
]

/**
 * 健康状况：按字典 HealthStatuses 的 item_value（nc_dict_items 权威）——
 * 健康或良好 / 一般或较弱 / 重病 / 重残 / 重病且重残 / 其他。
 * 与后端 DictValueMapper.ToFormHealthStatus、DefaultValuesConstants.HEALTH_STATUS、
 * PageDefaultValues.HealthOptions、DictDisplayHelper.GetHealthStatusDisplay 同词表。
 * 历史短词「健康 / 一般」由 normalizeHealthStatus 归一，不在选项里重复出现。
 */
export const HEALTH_STATUS_OPTIONS: SelectOption[] = [
  { label: '健康或良好', value: '健康或良好' },
  { label: '一般或较弱', value: '一般或较弱' },
  { label: '重病', value: '重病' },
  { label: '重残', value: '重残' },
  { label: '重病且重残', value: '重病且重残' },
  { label: '其他', value: '其他' },
]

/** 文化程度（与字典 EducationLevels 的中文显示值一致）。 */
export const EDUCATION_LEVEL_OPTIONS: SelectOption[] = [
  { label: '文盲/半文盲', value: '文盲/半文盲' },
  { label: '小学', value: '小学' },
  { label: '初中', value: '初中' },
  { label: '高中/中专', value: '高中/中专' },
  { label: '大专', value: '大专' },
  { label: '本科', value: '本科' },
  { label: '研究生及以上', value: '研究生及以上' },
]

/** 婚姻状况（与字典 MaritalStatuses 的中文显示值/顺序一致）。 */
export const MARITAL_STATUS_OPTIONS: SelectOption[] = [
  { label: '未婚', value: '未婚' },
  { label: '已婚', value: '已婚' },
  { label: '丧偶', value: '丧偶' },
  { label: '离婚', value: '离婚' },
  { label: '其他', value: '其他' },
]

/** 政治面貌（与字典 PoliticalStatuses 的中文显示值一致）。 */
export const POLITICAL_STATUS_OPTIONS: SelectOption[] = [
  { label: '中共党员', value: '中共党员' },
  { label: '共青团员', value: '共青团员' },
  { label: '群众', value: '群众' },
  { label: '民主党派', value: '民主党派' },
  { label: '其他', value: '其他' },
]

/** 疾病分类（与字典 DiseaseCategories 的 item_key 一致，本身为中文）。 */
export const DISEASE_CATEGORY_OPTIONS: SelectOption[] = [
  { label: '无任何疾病', value: '无任何疾病' },
  { label: '恶性肿瘤', value: '恶性肿瘤' },
  { label: '慢性肾功能不全', value: '慢性肾功能不全' },
  { label: '心脏病', value: '心脏病' },
  { label: '脑血管疾病', value: '脑血管疾病' },
  { label: '糖尿病', value: '糖尿病' },
  { label: '慢性呼吸系统疾病', value: '慢性呼吸系统疾病' },
  { label: '泌尿系统疾病', value: '泌尿系统疾病' },
  { label: '儿科先天性疾病', value: '儿科先天性疾病' },
  { label: '感染性疾病', value: '感染性疾病' },
  { label: '骨科及结缔组织疾病', value: '骨科及结缔组织疾病' },
  { label: '精神神经系统疾病', value: '精神神经系统疾病' },
  { label: '免疫系统疾病', value: '免疫系统疾病' },
  { label: '消化系统疾病', value: '消化系统疾病' },
  { label: '血液系统疾病', value: '血液系统疾病' },
  { label: '严重创伤及并发症', value: '严重创伤及并发症' },
  { label: '其他', value: '其他' },
]

/** 自理能力（三档常用口径）。 */
export const SELF_CARE_ABILITY_OPTIONS: SelectOption[] = [
  { label: '自理', value: '自理' },
  { label: '半自理', value: '半自理' },
  { label: '不能自理', value: '不能自理' },
]

/** 人员类型：赡养 / 抚养 / 扶养（与后端 PersonType 约定一致）。 */
export const PERSON_TYPE_OPTIONS: SelectOption[] = [
  { label: '赡养', value: '赡养' },
  { label: '抚养', value: '抚养' },
  { label: '扶养', value: '扶养' },
]

/** 就业状况：严格按旧字典 EmploymentStatuses（在职/失业/务农/学生/退休/其他）。 */
export const EMPLOYMENT_STATUS_OPTIONS: SelectOption[] = [
  { label: '在职', value: '在职' },
  { label: '失业', value: '失业' },
  { label: '务农', value: '务农' },
  { label: '学生', value: '学生' },
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

/** 赡养关系：沿用家庭关系字典（排除“本人/户主”），value 用旧版中文显示值。 */
export const SUPPORT_RELATION_OPTIONS: SelectOption[] = [
  { label: '配偶', value: '配偶' },
  { label: '子/婿', value: '子/婿' },
  { label: '女/媳', value: '女/媳' },
  { label: '孙子女/外孙子女', value: '孙子女/外孙子女' },
  { label: '父母/岳父母/公婆', value: '父母/岳父母/公婆' },
  { label: '祖父母/外祖父母', value: '祖父母/外祖父母' },
  { label: '兄弟姐妹', value: '兄弟姐妹' },
  { label: '其他', value: '其他' },
]
