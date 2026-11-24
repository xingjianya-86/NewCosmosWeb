<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import NcCard from '@/components/NcCard.vue'
import {
  APPLICATION_REASON_OPTIONS,
  DISABILITY_LEVEL_OPTIONS,
  DISABILITY_TYPE_OPTIONS,
  EDUCATION_LEVEL_OPTIONS,
  ETHNICITY_OPTIONS,
  HEALTH_STATUS_OPTIONS,
  HUKOU_TYPE_OPTIONS,
  MARITAL_STATUS_OPTIONS,
  POLITICAL_STATUS_OPTIONS,
} from './options'
import type { ApplicationFormModel } from './types'
import { getIdCardError, isBasicValid, parseIdCard } from './validation'

const props = defineProps<{
  form: ApplicationFormModel
}>()

const formRef = ref<FormInstance>()
const basic = computed(() => props.form.basic)

const idCardInfo = computed(() => parseIdCard(basic.value.applicantIdCard))
const ageDisplay = computed(() =>
  idCardInfo.value?.age === null || idCardInfo.value?.age === undefined
    ? ''
    : String(idCardInfo.value.age),
)

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

/** 身份证变化时自动带出性别；年龄由身份证实时计算，不落库。 */
watch(
  () => props.form.basic.applicantIdCard,
  (value) => {
    const info = parseIdCard(value)
    if (info) {
      props.form.basic.gender = info.gender
    }
  },
)

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
    <NcCard title="户籍所在地" subtitle="（限本单位管辖范围）">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">省份</span>
          <el-input v-model="basic.hukouProvince" placeholder="请选择或输入省份" />
        </label>
        <label class="field">
          <span class="field__label">城市</span>
          <el-input v-model="basic.hukouCity" placeholder="请选择或输入城市" />
        </label>
        <label class="field">
          <span class="field__label">区县</span>
          <el-input v-model="basic.hukouDistrict" placeholder="请选择或输入区县" />
        </label>
        <label class="field">
          <span class="field__label">乡镇</span>
          <el-input v-model="basic.hukouTown" placeholder="请选择或输入乡镇" />
        </label>
        <label class="field">
          <span class="field__label">村社区</span>
          <el-input v-model="basic.hukouVillage" placeholder="请选择或输入村社区" />
        </label>
        <label class="field field--full">
          <span class="field__label">详细地址</span>
          <el-input v-model="basic.hukouDetailAddress" placeholder="请输入详细地址（如：XX路XX号）" />
        </label>
      </div>
    </NcCard>

    <NcCard
      title="申请人所在地址（家庭住址）"
      subtitle="居住地全国可选（默认取本单位区划），不受户籍管辖范围限制"
    >
      <div class="field-grid">
        <label class="field">
          <span class="field__label">省份</span>
          <el-input v-model="basic.province" placeholder="请选择或输入省份" />
        </label>
        <label class="field">
          <span class="field__label">城市</span>
          <el-input v-model="basic.city" placeholder="请选择或输入城市" />
        </label>
        <label class="field">
          <span class="field__label">区县</span>
          <el-input v-model="basic.district" placeholder="请选择或输入区县" />
        </label>
        <label class="field">
          <span class="field__label">乡镇社区</span>
          <el-input v-model="basic.town" placeholder="请选择或输入乡镇社区" />
        </label>
        <label class="field">
          <span class="field__label">村社区</span>
          <el-input v-model="basic.village" placeholder="请选择或输入村社区" />
        </label>
        <label class="field field--full">
          <span class="field__label">详细地址</span>
          <el-input v-model="basic.address" placeholder="请输入详细地址（如：XX路XX号）" />
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
        <div class="field-grid">
          <el-form-item label="申请人姓名" prop="applicantName">
            <el-input
              v-model="basic.applicantName"
              maxlength="30"
              placeholder="请输入姓名"
              clearable
            />
          </el-form-item>

          <el-form-item label="身份证号" prop="applicantIdCard">
            <el-input
              v-model="basic.applicantIdCard"
              maxlength="18"
              placeholder="请输入 18 位身份证号"
              clearable
            />
          </el-form-item>

          <el-form-item label="残疾证号" prop="disabilityCardNo">
            <el-input
              v-model="basic.disabilityCardNo"
              maxlength="30"
              placeholder="无残疾可留空"
              clearable
            />
          </el-form-item>

          <el-form-item label="性别" prop="gender">
            <el-input
              :model-value="basic.gender"
              readonly
              placeholder="身份证自动带出"
            />
          </el-form-item>

          <el-form-item label="年龄">
            <el-input :model-value="ageDisplay" readonly placeholder="身份证自动计算">
              <template #suffix>岁</template>
            </el-input>
          </el-form-item>

          <el-form-item label="联系电话" prop="phone">
            <el-input v-model="basic.phone" maxlength="20" placeholder="请输入手机号或固定电话" clearable />
          </el-form-item>

          <el-form-item label="民族" prop="ethnicity">
            <el-select
              v-model="basic.ethnicity"
              filterable
              allow-create
              default-first-option
              placeholder="请选择或输入民族"
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
            <el-select v-model="basic.hukouType" placeholder="请选择户口性质">
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
            >
              <el-option
                v-for="item in POLITICAL_STATUS_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="开户行" prop="bankName">
            <el-input
              v-model="basic.bankName"
              maxlength="60"
              placeholder="请输入银行卡开户行"
              clearable
            />
          </el-form-item>

          <el-form-item label="银行卡号" prop="bankAccount">
            <el-input
              v-model="basic.bankAccount"
              maxlength="32"
              placeholder="请输入银行卡号"
              clearable
            />
          </el-form-item>
        </div>
      </el-form>
    </NcCard>

    <NcCard title="健康与残疾信息">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">健康状态</span>
          <el-select
            v-model="basic.healthStatus"
            filterable
            allow-create
            default-first-option
            clearable
            placeholder="请选择或输入健康状态"
          >
            <el-option
              v-for="item in HEALTH_STATUS_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </label>

        <label class="field">
          <span class="field__label">残疾类型</span>
          <el-select
            v-model="basic.disabilityType"
            filterable
            allow-create
            default-first-option
            clearable
            placeholder="无残疾可留空"
          >
            <el-option
              v-for="item in DISABILITY_TYPE_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </label>

        <label class="field">
          <span class="field__label">残疾等级</span>
          <el-select
            v-model="basic.disabilityLevel"
            filterable
            allow-create
            default-first-option
            clearable
            placeholder="无残疾可留空"
          >
            <el-option
              v-for="item in DISABILITY_LEVEL_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </label>

        <label class="field field--full">
          <span class="field__label">疾病名称</span>
          <el-input
            v-model="basic.diseaseName"
            maxlength="60"
            placeholder="无疾病可留空"
            clearable
          />
        </label>

        <label class="field field--full">
          <span class="field__label">前 12 个月医疗自付（元）</span>
          <el-input-number
            :model-value="basic.medicalSelfPaid12m"
            :min="0"
            :precision="2"
            :controls="false"
            placeholder="0.00"
            @update:model-value="basic.medicalSelfPaid12m = $event ?? null"
          />
        </label>
      </div>
    </NcCard>

    <NcCard title="其他信息">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">申请原因</span>
          <el-select
            v-model="basic.applicationReason"
            filterable
            allow-create
            default-first-option
            placeholder="请选择或输入申请原因"
          >
            <el-option
              v-for="item in APPLICATION_REASON_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </label>

        <label class="field field--full">
          <span class="field__label">原因说明</span>
          <el-input
            v-model="basic.applicationReasonDetail"
            type="textarea"
            :rows="3"
            maxlength="1000"
            show-word-limit
            placeholder="请具体说明家庭困难情形"
          />
        </label>

        <label class="field field--full">
          <span class="field__label">备注</span>
          <el-input
            v-model="basic.remark"
            type="textarea"
            :rows="2"
            maxlength="500"
            show-word-limit
            placeholder="其他需要说明的情况"
          />
        </label>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.step-basic {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.basic-form :deep(.el-form-item) {
  margin-bottom: 0;
}

.basic-form :deep(.el-select),
.basic-form :deep(.el-input-number) {
  width: 100%;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px 18px;
}

.field--full {
  grid-column: 1 / -1;
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
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }
}
</style>
