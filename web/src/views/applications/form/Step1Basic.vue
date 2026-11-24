<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import NcCard from '@/components/NcCard.vue'
import {
  APPLICATION_REASON_OPTIONS,
  ETHNICITY_OPTIONS,
  GENDER_OPTIONS,
  HUKOU_TYPE_OPTIONS,
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
    <NcCard title="申请人信息" subtitle="带 * 为必填项；身份证自动计算性别与年龄。">
      <el-form
        ref="formRef"
        :model="basic"
        :rules="rules"
        label-position="top"
        class="basic-form"
        @submit.prevent
      >
        <div class="field-grid">
          <el-form-item label="户口性质" prop="hukouType">
            <el-radio-group v-model="basic.hukouType">
              <el-radio-button
                v-for="item in HUKOU_TYPE_OPTIONS"
                :key="item.value"
                :value="item.value"
              >
                {{ item.label }}
              </el-radio-button>
            </el-radio-group>
          </el-form-item>

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
              placeholder="18 位身份证号"
              clearable
            />
          </el-form-item>

          <el-form-item label="性别" prop="gender">
            <el-select v-model="basic.gender" placeholder="身份证自动带出" clearable>
              <el-option
                v-for="item in GENDER_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="年龄">
            <el-input :model-value="ageDisplay" readonly placeholder="身份证自动计算">
              <template #suffix>岁</template>
            </el-input>
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

          <el-form-item label="联系电话" prop="phone">
            <el-input v-model="basic.phone" maxlength="20" placeholder="手机号或固定电话" clearable />
          </el-form-item>

          <el-form-item label="残疾证号" prop="disabilityCardNo">
            <el-input
              v-model="basic.disabilityCardNo"
              maxlength="30"
              placeholder="无残疾可留空"
              clearable
            />
          </el-form-item>
        </div>
      </el-form>
    </NcCard>

    <NcCard title="户籍地址" subtitle="按省 / 市 / 县 / 乡镇 / 村逐级填写。">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">省</span>
          <el-input v-model="basic.hukouProvince" placeholder="省" />
        </label>
        <label class="field">
          <span class="field__label">市</span>
          <el-input v-model="basic.hukouCity" placeholder="市" />
        </label>
        <label class="field">
          <span class="field__label">县 / 区</span>
          <el-input v-model="basic.hukouDistrict" placeholder="县 / 区" />
        </label>
        <label class="field">
          <span class="field__label">乡镇 / 街道</span>
          <el-input v-model="basic.hukouTown" placeholder="乡镇 / 街道" />
        </label>
        <label class="field">
          <span class="field__label">村 / 社区</span>
          <el-input v-model="basic.hukouVillage" placeholder="村 / 社区" />
        </label>
        <label class="field field--wide">
          <span class="field__label">详细地址</span>
          <el-input v-model="basic.hukouDetailAddress" placeholder="门牌号、组、屯等" />
        </label>
      </div>
    </NcCard>

    <NcCard title="现居住地址" subtitle="与户籍地址不一致时据实填写。">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">省</span>
          <el-input v-model="basic.province" placeholder="省" />
        </label>
        <label class="field">
          <span class="field__label">市</span>
          <el-input v-model="basic.city" placeholder="市" />
        </label>
        <label class="field">
          <span class="field__label">县 / 区</span>
          <el-input v-model="basic.district" placeholder="县 / 区" />
        </label>
        <label class="field">
          <span class="field__label">乡镇 / 街道</span>
          <el-input v-model="basic.town" placeholder="乡镇 / 街道" />
        </label>
        <label class="field">
          <span class="field__label">村 / 社区</span>
          <el-input v-model="basic.village" placeholder="村 / 社区" />
        </label>
        <label class="field field--wide">
          <span class="field__label">详细地址</span>
          <el-input v-model="basic.address" placeholder="门牌号、组、屯等" />
        </label>
      </div>
    </NcCard>

    <NcCard title="银行账户" subtitle="用于救助资金发放。">
      <div class="field-grid">
        <label class="field field--wide">
          <span class="field__label">开户行</span>
          <el-input v-model="basic.bankName" placeholder="如：中国农业银行林口县支行" />
        </label>
        <label class="field">
          <span class="field__label">银行账号</span>
          <el-input v-model="basic.bankAccount" maxlength="32" placeholder="银行卡号" />
        </label>
      </div>
    </NcCard>

    <NcCard title="申请说明" subtitle="说明申请原因与家庭主要困难。">
      <div class="field-grid">
        <label class="field">
          <span class="field__label">申请原因</span>
          <el-select
            v-model="basic.applicationReason"
            filterable
            allow-create
            default-first-option
            placeholder="请选择或输入"
          >
            <el-option
              v-for="item in APPLICATION_REASON_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </label>

        <label class="field field--wide">
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

        <label class="field field--wide">
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

.field-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px 18px;
}

.field--wide {
  grid-column: span 2;
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
.field :deep(.el-date-editor) {
  width: 100%;
}

@media (max-width: 1279px) {
  .field-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .field--wide {
    grid-column: span 2;
  }
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }

  .field--wide {
    grid-column: span 1;
  }
}
</style>
