<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { ElMessage, ElMessageBox } from 'element-plus'
import { changeApplicationDate } from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import { getIcd10 } from '@/api/icd10'
import { judgeSeverity, judgeSeverityByCost } from '@/api/aiJudge'
import type { AiSeverityResult } from '@/api/aiJudge'
import AddressCascader from '@/components/AddressCascader.vue'
import NcCard from '@/components/NcCard.vue'
import {
  DISEASE_CATEGORY_OPTIONS,
  EDUCATION_LEVEL_OPTIONS,
  ETHNICITY_OPTIONS,
  HUKOU_TYPE_OPTIONS,
  MARITAL_STATUS_OPTIONS,
  POLITICAL_STATUS_OPTIONS,
} from './options'
import type { ApplicationFormBasic, ApplicationFormModel } from './types'
import { deriveHealthStatus, parseDisabilityCertificate } from './healthStatus'
import { getIdCardError, isBasicValid, parseIdCard } from './validation'

const props = defineProps<{
  form: ApplicationFormModel
  /** 查看/复核模式：即使状态仍为草稿（mode=review）也整步禁用。 */
  readonly?: boolean
}>()

const emit = defineEmits<{ reload: [] }>()

const formRef = ref<FormInstance>()
const basic = computed(() => props.form.basic)

/** 旧版疾病字段尚未进入共享类型；按约定 camelCase 局部扩展，不改 types.ts。 */
type BasicWithDiseaseFields = ApplicationFormBasic & {
  isSevereDisease?: boolean
  diseaseCode?: string
  diseaseCategory?: string
}

const diseaseFields = computed(() => props.form.basic as BasicWithDiseaseFields)

/** 非草稿状态（旧版 IsStep1Editable 同口径）或查看/复核模式全部输入只读。 */
const isLocked = computed(() => props.form.status !== 'Draft' || Boolean(props.readonly))

const isSevereDisease = computed<boolean>({
  get: () => Boolean(diseaseFields.value.isSevereDisease),
  set: (value) => {
    diseaseFields.value.isSevereDisease = value
  },
})

const diseaseCode = computed<string>({
  get: () => diseaseFields.value.diseaseCode ?? '',
  set: (value) => {
    diseaseFields.value.diseaseCode = value
  },
})

const diseaseCategory = computed<string>({
  get: () => diseaseFields.value.diseaseCategory ?? '',
  set: (value) => {
    diseaseFields.value.diseaseCategory = value
  },
})

/** 疾病编码变更：共享 ICD-10 查询回填病名/一级分类；只填空值，不覆盖手工修改。 */
async function handleDiseaseCodeChange(): Promise<void> {
  const code = diseaseCode.value.trim()
  if (!code) return
  try {
    const result = await getIcd10(code)
    if (diseaseCode.value.trim().toLowerCase() !== code.toLowerCase()) return
    if (!basic.value.diseaseName?.trim() && result.name) {
      basic.value.diseaseName = result.name
    }
    if (!diseaseCategory.value && result.category) {
      diseaseCategory.value = result.category
    }
    // 名称为空 = ICD-10 字典未命中：名称不可信，不判定（对齐桌面 RequestApplicantAiJudgment）。
    if (!result.name?.trim() || isLocked.value) return
    void requestAiJudgment(code, result.name)
  } catch {
    // 字典查询失败不阻断手工录入。
  }
}

// ── AI 重病判定接线（P0，2026-11-27）────────────────────────────
// 口径唯一权威在 Core：建议文案由服务端 AiJudgeConstants.FormatSuggestion 拼装，
// 自动勾选闸门由服务端 ISevereDiseaseAdviceService.ShouldAutoCheck 判定；
// 前端只负责触发、竞态丢弃与「只勾不取消」。

/** 用户本会话内已人工取消过「是否重病」（true→false 只能来自用户）：此后 AI 只出建议、不再自动勾。 */
const applicantRejected = ref(false)

/** AI/费用口径建议条文案（仅 severe 时非空）。 */
const aiSuggestion = ref('')

/** 建议条可见性：@close 只隐藏、不动勾选。 */
const showAiSuggestion = ref(false)

function dismissAiSuggestion(): void {
  showAiSuggestion.value = false
}

/**
 * 「是否重病」人工取消跟踪：Element Plus 的 change 只在用户点击时触发，
 * 程序化赋值（AI 自动勾选）不会触发，因此无需加载抑制标志。
 */
function handleSevereChange(value: boolean | string | number): void {
  if (value === false) applicantRejected.value = true
}

/** 病种口径判定：失败=无建议，静默降级（不弹提示、不阻塞保存）。 */
async function requestAiJudgment(code: string, name: string): Promise<void> {
  const sentCode = code.toLowerCase()
  const sentRejected = applicantRejected.value
  // 换输入即失效旧建议。
  aiSuggestion.value = ''
  showAiSuggestion.value = false

  try {
    const result = await judgeSeverity({ name, code, userRejected: sentRejected })
    // 过期响应丢弃：编码已变，或用户在此期间取消过勾选。
    if (diseaseCode.value.trim().toLowerCase() !== sentCode) return
    if (applicantRejected.value !== sentRejected) return
    applyAiJudgment(result)
  } catch {
    // 失败=无建议（断网/未配置地址/超时）：静默，不影响表单保存。
  }
}

function applyAiJudgment(result: AiSeverityResult): void {
  if (result.verdict !== 'severe') {
    showAiSuggestion.value = false
    return
  }
  // 只勾不取消：AI 永不把勾选改为 false。
  if (result.autoChecked && !isSevereDisease.value) {
    isSevereDisease.value = true
  }
  aiSuggestion.value = result.suggestion
  showAiSuggestion.value = Boolean(result.suggestion)
}

/** 费用口径判定（自付字段失焦/回车触发）；阈值缺失由服务端静默降级为不判定。 */
async function handleSelfPaidChange(): Promise<void> {
  if (isLocked.value) return
  const paid = basic.value.medicalSelfPaid12m
  if (paid === null || paid === undefined || paid <= 0) return

  const sentRejected = applicantRejected.value
  // 城乡档与表单其它判定同口径：HukouType 非 Urban 即农村档。
  const isRural = basic.value.hukouType !== 'Urban'
  try {
    const result = await judgeSeverityByCost({
      selfPaid12m: paid,
      isRural,
      userRejected: sentRejected,
    })
    if (applicantRejected.value !== sentRejected) return // 用户已否决，丢弃过期结果
    if (!result.severe || !result.suggestion) return
    if (result.autoChecked && !isSevereDisease.value) {
      isSevereDisease.value = true // 只勾不取消
    }
    aiSuggestion.value = result.suggestion
    showAiSuggestion.value = true
  } catch {
    // 失败=无建议：静默降级，不阻塞保存。
  }
}

/** 健康状态只读展示：勾选“是否重病”时显示重病（当前为重残/重病且重残者保留原值）。 */
const healthStatusDisplay = computed(() => {
  const current = basic.value.healthStatus?.trim() ?? ''
  if (isSevereDisease.value && current !== '重残' && current !== '重病且重残') {
    return '重病'
  }
  return current || '—'
})

function displayOrDash(value: string | null | undefined): string {
  return value?.trim() ? value.trim() : '—'
}

/**
 * 申请日期（只读）：编号日期段是权威来源（旧版 ArchiveAnchorHelper 同口径），
 * 新档尚未生成编号时与旧版一致显示今天。
 */
const applicationDateText = computed(() => {
  const match = /^SA(\d{8})/.exec(props.form.applicationNo ?? '')
  if (match) {
    const value = match[1]
    return `${value.slice(0, 4)}-${value.slice(4, 6)}-${value.slice(6, 8)}`
  }
  const today = new Date()
  const month = String(today.getMonth() + 1).padStart(2, '0')
  const day = String(today.getDate()).padStart(2, '0')
  return `${today.getFullYear()}-${month}-${day}`
})

const dateDialogVisible = ref(false)
const newApplicationDate = ref('')
const changingDate = ref(false)

/** 旧版改期上限为今天（更早的下限由 Core 校验：不早于当年 1 月 1 日）。 */
function isFutureDate(date: Date): boolean {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return date.getTime() > today.getTime()
}

function openChangeDate(): void {
  if (isLocked.value) return
  if (props.form.id <= 0) {
    void ElMessageBox.alert('请先保存草稿生成申请编号，再修改申请日期', '提示', {
      confirmButtonText: '确定',
    }).catch(() => {})
    return
  }
  newApplicationDate.value = applicationDateText.value
  dateDialogVisible.value = true
}

function confirmDateSelection(): void {
  if (!newApplicationDate.value) {
    ElMessage.warning('请选择申请日期')
    return
  }
  if (newApplicationDate.value === applicationDateText.value) {
    dateDialogVisible.value = false
    return
  }
  dateDialogVisible.value = false
  void applyApplicationDate(newApplicationDate.value)
}

/** 旧版改期：二次确认后调 Core（同改编号日期段与 created_at），成功回写并刷新表单。 */
async function applyApplicationDate(nextDate: string): Promise<void> {
  const previous = applicationDateText.value
  const confirmMessage = h(
    'div',
    { style: 'white-space: pre-line; line-height: 1.7;' },
    [
      `申请日期：${previous} → ${nextDate}`,
      '',
      '修改后将改变该档案的公示窗口、卷号与文书上的申请日期（批次顺延提示随之重算）；已打印的文书不受影响，需重新出文书才会更新。',
      '',
      '注意：批次按入户调查日期锚定——若入户日期超出新窗口，系统会自动把它同步到「新申请日 ~ 会议日」之间，否则批次不变。',
    ].join('\n'),
  )

  try {
    await ElMessageBox.confirm(confirmMessage, '确认修改申请日期', {
      type: 'warning',
      confirmButtonText: '确认修改',
      cancelButtonText: '取消',
    })
  } catch {
    return
  }

  changingDate.value = true
  try {
    const result = await changeApplicationDate(props.form.id, nextDate)
    if (result.applicationNo) props.form.applicationNo = result.applicationNo

    // 改期同事务联动入户调查日期时，同步回写当前表单（旧版行为）。
    const syncedSurvey = result.surveyDate ? String(result.surveyDate).slice(0, 10) : ''
    if (syncedSurvey) props.form.survey.surveyDate = syncedSurvey

    ElMessage.success(
      syncedSurvey
        ? `申请日期已改为 ${nextDate}，编号 ${result.applicationNo}；入户调查日期已同步为 ${syncedSurvey}`
        : `申请日期已改为 ${nextDate}，编号 ${result.applicationNo}`,
    )
    emit('reload')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '修改申请日期失败'))
  } finally {
    changingDate.value = false
  }
}

const idCardInfo = computed(() => parseIdCard(basic.value.applicantIdCard))
const ageDisplay = computed(() =>
  idCardInfo.value?.age === null || idCardInfo.value?.age === undefined
    ? ''
    : String(idCardInfo.value.age),
)

// ── 身体状况即时派生（残疾证号 / 疾病分类 / 重病勾选 / 年龄）────────
// 与 Core HealthStatusDeriver 同口径（见 healthStatus.ts）；
// 只读态不派生（展示后端已派生的值）；历史值由覆盖守卫保留。

/** 重算身体状况并写回 basic.healthStatus（派生规则见 healthStatus.ts）。 */
function deriveApplicantHealthStatus(): void {
  if (isLocked.value) return
  basic.value.healthStatus = deriveHealthStatus({
    diseaseCategory: diseaseCategory.value,
    disabilityType: basic.value.disabilityType,
    disabilityLevel: basic.value.disabilityLevel,
    age: idCardInfo.value?.age ?? null,
    isSevereDisease: isSevereDisease.value,
    currentHealthStatus: basic.value.healthStatus,
  })
}

/** 残疾证号变化：解析类型/等级后重算（清空证号 → 类型等级清空 → 身体状况回落）。 */
function handleDisabilityCardChange(): void {
  if (isLocked.value) return
  const parsed = parseDisabilityCertificate(basic.value.disabilityCardNo)
  basic.value.disabilityType = parsed.type
  basic.value.disabilityLevel = parsed.level
  deriveApplicantHealthStatus()
}

const rules: FormRules = {
  applicantName: [{ required: true, message: '请输入申请人姓名', trigger: 'blur' }],
  applicantIdCard: [
    { required: true, message: '请输入身份证号', trigger: 'blur' },
    {
      validator: (_rule, value, callback) => {
        const error = getIdCardError(String(value ?? ''))
        callback(error ? new Error(error) : undefined)
      },
      trigger: 'blur',
    },
  ],
}

/** 身份证变化时自动带出性别；年龄由身份证实时计算，不落库。年龄变化同步重算身体状况。 */
watch(
  () => props.form.basic.applicantIdCard,
  (value) => {
    const info = parseIdCard(value)
    if (info) {
      props.form.basic.gender = info.gender
    }
    deriveApplicantHealthStatus()
  },
)

// 一级疾病（AI 编码回填 handleDiseaseCodeChange 程序化赋值）与是否重病
// （AI applyAiJudgment/费用口径自动勾选）都是程序化写值，@change 不触发 → 统一走 watch。
watch(diseaseCategory, () => deriveApplicantHealthStatus())
watch(isSevereDisease, () => deriveApplicantHealthStatus())

// 注：原 `watch(diseaseCode)` 在编码变化时直接置 IsSevereDisease=true，绕过
// ISevereDiseaseAdviceService.ShouldAutoCheck 闸门（目录外编码也会被勾上），
// 是 Web 侧最大的语义 bug，已删除。自动勾选只允许来自 AI 判定/费用口径两条通道。

async function validate(): Promise<boolean> {
  if (!formRef.value) return isBasicValid(basic.value)
  try {
    await formRef.value.validate()
    return true
  } catch {
    return false
  }
}

defineExpose({ validate })
</script>

<template>
  <div class="step-basic">
    <NcCard padding="sm">
      <div class="application-date">
        <span class="application-date__label">申请日期</span>
        <strong class="application-date__value">{{ applicationDateText }}</strong>
        <el-button size="small" :loading="changingDate" :disabled="isLocked" @click="openChangeDate">
          修改
        </el-button>
      </div>
    </NcCard>

    <NcCard title="户籍所在地" subtitle="（限本单位管辖范围）">
      <AddressCascader
        v-model:province="basic.hukouProvince"
        v-model:city="basic.hukouCity"
        v-model:district="basic.hukouDistrict"
        v-model:town="basic.hukouTown"
        v-model:village="basic.hukouVillage"
        :province-readonly="Boolean(basic.hukouProvince)"
        :disabled="isLocked"
      />
    </NcCard>

    <NcCard
      title="申请人所在地址（家庭住址）"
      subtitle="居住地全国可选（默认取本单位区划），不受户籍管辖范围限制"
    >
      <AddressCascader
        v-model:province="basic.province"
        v-model:city="basic.city"
        v-model:district="basic.district"
        v-model:town="basic.town"
        v-model:village="basic.village"
        :disabled="isLocked"
      />
      <div class="field-grid address-detail">
        <label class="field field--full">
          <span class="field__label">详细地址</span>
          <el-input
            v-model="basic.address"
            placeholder="请输入详细地址（如：XX路XX号）"
            :disabled="isLocked"
          />
        </label>
      </div>
    </NcCard>

    <NcCard title="申请人信息">
      <el-form
        ref="formRef"
        :model="basic"
        :rules="rules"
        label-position="top"
        class="basic-form"
        @submit.prevent
      >
        <section class="field-group">
          <h3 class="field-group__title">基本身份信息</h3>
          <div class="field-grid">
            <el-form-item label="申请人姓名" prop="applicantName">
              <el-input
                v-model="basic.applicantName"
                maxlength="30"
                placeholder="请输入姓名"
                clearable
                :disabled="isLocked"
              />
            </el-form-item>

            <el-form-item label="身份证号" prop="applicantIdCard">
              <el-input
                v-model="basic.applicantIdCard"
                maxlength="18"
                placeholder="请输入 18 位身份证号"
                clearable
                :disabled="isLocked"
              />
            </el-form-item>

            <el-form-item label="性别" prop="gender">
              <el-input
                :model-value="basic.gender"
                readonly
                placeholder="身份证自动带出"
                :disabled="isLocked"
              />
            </el-form-item>

            <el-form-item label="年龄">
              <el-input :model-value="ageDisplay" readonly placeholder="身份证自动计算" :disabled="isLocked">
                <template #suffix>岁</template>
              </el-input>
            </el-form-item>

            <el-form-item label="残疾证号" prop="disabilityCardNo">
              <el-input
                v-model="basic.disabilityCardNo"
                maxlength="30"
                placeholder="无残疾可留空"
                clearable
                :disabled="isLocked"
                @change="handleDisabilityCardChange"
              />
            </el-form-item>
          </div>
        </section>

        <section class="field-group">
          <h3 class="field-group__title">联系方式与社会属性</h3>
          <div class="field-grid">
            <el-form-item label="联系电话" prop="phone">
              <el-input
                v-model="basic.phone"
                maxlength="20"
                placeholder="请输入手机号或固定电话"
                clearable
                :disabled="isLocked"
              />
            </el-form-item>

            <el-form-item label="民族" prop="ethnicity">
              <el-select
                v-model="basic.ethnicity"
                filterable
                allow-create
                default-first-option
                placeholder="请选择或输入民族"
                :disabled="isLocked"
              >
                <el-option
                  v-for="item in ETHNICITY_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="婚姻状况" prop="maritalStatus">
              <el-select
                v-model="basic.maritalStatus"
                filterable
                allow-create
                default-first-option
                clearable
                placeholder="请选择或输入婚姻状况"
                :disabled="isLocked"
              >
                <el-option
                  v-for="item in MARITAL_STATUS_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="户口性质" prop="hukouType">
              <el-select v-model="basic.hukouType" placeholder="请选择户口性质" :disabled="isLocked">
                <el-option
                  v-for="item in HUKOU_TYPE_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="文化程度" prop="educationLevel">
              <el-select
                v-model="basic.educationLevel"
                filterable
                allow-create
                default-first-option
                clearable
                placeholder="请选择或输入文化程度"
                :disabled="isLocked"
              >
                <el-option
                  v-for="item in EDUCATION_LEVEL_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="政治面貌" prop="politicalStatus">
              <el-select
                v-model="basic.politicalStatus"
                filterable
                allow-create
                default-first-option
                clearable
                placeholder="请选择或输入政治面貌"
                :disabled="isLocked"
              >
                <el-option
                  v-for="item in POLITICAL_STATUS_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>
          </div>
        </section>

        <section class="field-group field-group--bank">
          <h3 class="field-group__title">银行卡信息</h3>
          <div class="field-grid">
            <el-form-item label="开户行" prop="bankName">
              <el-input
                v-model="basic.bankName"
                maxlength="60"
                placeholder="请输入银行卡开户行"
                clearable
                :disabled="isLocked"
              />
            </el-form-item>

            <el-form-item label="银行卡号" prop="bankAccount">
              <el-input
                v-model="basic.bankAccount"
                maxlength="32"
                placeholder="请输入银行卡号"
                clearable
                :disabled="isLocked"
              />
            </el-form-item>
          </div>
        </section>
      </el-form>
    </NcCard>

    <NcCard title="健康与残疾信息">
      <div class="disease-grid">
        <div class="disease-row disease-row--three">
          <label class="field">
            <span class="field__label">身体状况</span>
            <el-input :model-value="healthStatusDisplay" readonly :disabled="isLocked" />
          </label>

          <label class="field">
            <span class="field__label">残疾类型</span>
            <el-input :model-value="displayOrDash(basic.disabilityType)" readonly :disabled="isLocked" />
          </label>

          <label class="field">
            <span class="field__label">残疾等级</span>
            <el-input :model-value="displayOrDash(basic.disabilityLevel)" readonly :disabled="isLocked" />
          </label>
        </div>

        <div class="disease-row disease-row--four">
          <div class="field">
            <span class="field__label">是否重病</span>
            <el-checkbox
              v-model="isSevereDisease"
              :disabled="isLocked"
              aria-label="是否重病"
              @change="handleSevereChange"
            />
          </div>

          <label class="field">
            <span class="field__label">疾病编码（ICD-10）</span>
            <el-input
              v-model="diseaseCode"
              maxlength="20"
              placeholder="如 A00.900，输入后自动带出疾病"
              clearable
              :disabled="isLocked"
              @change="handleDiseaseCodeChange"
            />
          </label>

          <label class="field">
            <span class="field__label">一级疾病</span>
            <el-select
              v-model="diseaseCategory"
              filterable
              allow-create
              default-first-option
              clearable
              placeholder="请选择或输入一级疾病"
              :disabled="isLocked"
            >
              <el-option
                v-for="item in DISEASE_CATEGORY_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </label>

          <label class="field">
            <span class="field__label">二级疾病</span>
            <el-input
              v-model="basic.diseaseName"
              maxlength="60"
              placeholder="输入疾病编码自动带出，可手工修改"
              clearable
              :disabled="isLocked"
            />
          </label>
        </div>

        <div class="disease-row disease-row--full">
          <label class="field">
            <span class="field__label">前12个月个人医疗自付累计（元）</span>
            <el-input-number
              :model-value="basic.medicalSelfPaid12m"
              :min="0"
              :precision="2"
              :controls="false"
              placeholder="住院+门诊慢特病+高值药品累计自付；超上年度人均可支配收入按费用口径认定重病"
              :disabled="isLocked"
              @update:model-value="basic.medicalSelfPaid12m = $event ?? null"
              @change="handleSelfPaidChange"
            />
          </label>
        </div>

        <!-- AI 重病建议条：仅 severe 时显示；关闭只隐藏，不动勾选。 -->
        <el-alert
          v-if="!isLocked && showAiSuggestion && aiSuggestion"
          :title="aiSuggestion"
          type="warning"
          show-icon
          :closable="true"
          class="ai-suggestion"
          @close="dismissAiSuggestion"
        />
      </div>
    </NcCard>

    <el-dialog v-model="dateDialogVisible" title="修改申请日期" width="380px" append-to-body>
      <el-date-picker
        v-model="newApplicationDate"
        type="date"
        format="YYYY-MM-DD"
        value-format="YYYY-MM-DD"
        placeholder="选择申请日期"
        :disabled-date="isFutureDate"
        class="application-date__picker"
      />
      <template #footer>
        <el-button @click="dateDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="changingDate" @click="confirmDateSelection">
          确认
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.step-basic {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.application-date {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
}

.application-date__label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
}

.application-date__value {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.application-date__picker {
  width: 100%;
}

.basic-form :deep(.el-form-item) {
  margin-bottom: 0;
}

.basic-form :deep(.el-select),
.basic-form :deep(.el-input-number) {
  width: 100%;
}

/* 申请人信息分组（基本身份 / 联系方式与社会属性 / 银行卡）：
   组标题 + 组间细分隔线，与 Step5Classification 分隔线、月报 group-title 同款视觉。 */
.field-group {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.field-group + .field-group {
  margin-top: 6px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.field-group__title {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px 18px;
}

.field--full {
  grid-column: 1 / -1;
}

/* 旧版 Step1 疾病区：第一行 5 列只放 3 项，第二行 Auto+3 列，医疗自付整行 */
.disease-grid {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.disease-row {
  display: grid;
  gap: 16px 18px;
}

.disease-row--three {
  grid-template-columns: repeat(5, minmax(0, 1fr));
}

.disease-row--four {
  grid-template-columns: minmax(88px, auto) repeat(3, minmax(0, 1fr));
}

.disease-row--full {
  grid-template-columns: minmax(0, 1fr);
}

.ai-suggestion {
  width: 100%;
}

.address-detail {
  margin-top: 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field__label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
}

.field :deep(.el-select),
.field :deep(.el-date-editor),
.field :deep(.el-input-number) {
  width: 100%;
}

@media (min-width: 1440px) {
  .field-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  /* 银行卡组仅 2 字段：≥1440px 仍固定 2 列，避免按 3 列栅格换行（窄屏随默认 2/1 列走）。 */
  .field-group--bank .field-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1100px) {
  .disease-row--three {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .disease-row--four {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }

  .disease-grid {
    gap: 13px;
  }

  .disease-row--three,
  .disease-row--four,
  .disease-row--full {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }
}
</style>
