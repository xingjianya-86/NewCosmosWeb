<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { submitMemberChange } from '@/api/changeManagement'
import type {
  ChangeManagementDetail,
  ChangeManagementFilters,
  ChangeMember,
  ChangeOperationResult,
  MemberAddRequest,
  MemberModifyRequest,
  MemberRemoveRequest,
} from '@/types/changeManagement'
import ChangeResultDialog from './ChangeResultDialog.vue'

type ChangeTab = 'add' | 'remove' | 'modify'

const props = defineProps<{
  modelValue: boolean
  applicationId: number
  detail: ChangeManagementDetail
  filters: ChangeManagementFilters
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  success: [result: ChangeOperationResult]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const today = (): string => {
  const now = new Date()
  const pad = (value: number): string => String(value).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

const activeTab = ref<ChangeTab>('add')
const changeReason = ref('')
const additions = ref<MemberAddRequest[]>([])
const removals = ref<MemberRemoveRequest[]>([])
const modifications = ref<MemberModifyRequest[]>([])
const submitting = ref(false)
const result = ref<ChangeOperationResult | null>(null)
const resultVisible = ref(false)

const relationshipOptions = computed(() =>
  props.filters.relationships.filter((option) => option.code !== 'Head'),
)

// ── 新增成员 ──
interface AddFormState {
  name: string
  idCard: string
  relationshipToHead: string
  gender: string
  age: number | undefined
  birthDate: string
  healthStatus: string
  phone: string
  reasonCode: string
  reasonName: string
  eventDate: string
  remark: string
}

const emptyAddForm = (): AddFormState => ({
  name: '',
  idCard: '',
  relationshipToHead: '',
  gender: '',
  age: undefined,
  birthDate: '',
  healthStatus: '',
  phone: '',
  reasonCode: '',
  reasonName: '',
  eventDate: today(),
  remark: '',
})

const addForm = reactive<AddFormState>(emptyAddForm())

function resetAddForm(): void {
  Object.assign(addForm, emptyAddForm())
}

function addReasonChanged(code: string): void {
  const option = props.filters.memberAddReasons.find((item) => item.code === code)
  addForm.reasonName = option?.name ?? ''
}

function queueAddition(): void {
  const name = addForm.name.trim()
  const idCard = addForm.idCard.trim()
  if (!name || !idCard) {
    ElMessage.warning('请填写新增成员姓名与身份证号')
    return
  }

  if (!addForm.relationshipToHead) {
    ElMessage.warning('请选择新增成员与户主关系')
    return
  }

  if (!addForm.reasonCode || !addForm.reasonName) {
    ElMessage.warning('请选择增员原因')
    return
  }

  const duplicate = [...props.detail.members.map((member) => member.idCard), ...additions.value.map((item) => item.idCard)]
    .some((value) => value.trim().toUpperCase() === idCard.toUpperCase())
  if (duplicate) {
    ElMessage.warning('该身份证号已在本户或新增清单中存在')
    return
  }

  additions.value.push({
    name,
    idCard,
    relationshipToHead: addForm.relationshipToHead,
    gender: addForm.gender || null,
    age: addForm.age ?? null,
    birthDate: addForm.birthDate || null,
    healthStatus: addForm.healthStatus || null,
    phone: addForm.phone.trim() || null,
    reasonCode: addForm.reasonCode,
    reasonName: addForm.reasonName,
    eventDate: addForm.eventDate || null,
    remark: addForm.remark.trim() || null,
  })
  resetAddForm()
}

function removeAddition(index: number): void {
  additions.value.splice(index, 1)
}

// ── 减员 ──
const removableMembers = computed(() =>
  props.detail.members.filter((member) => !member.isHouseholdHead && !member.isSupport),
)

const removeDialogVisible = ref(false)
const removeTarget = ref<ChangeMember | null>(null)
interface RemoveFormState {
  reasonCode: string
  reasonName: string
  eventDate: string
  eventTime: string
  remark: string
}

const removeForm = reactive<RemoveFormState>({
  reasonCode: '',
  reasonName: '',
  eventDate: today(),
  eventTime: '',
  remark: '',
})

const isDeathRemove = computed(() => removeForm.reasonCode === 'Death')

function openRemoveDialog(member: ChangeMember): void {
  removeTarget.value = member
  removeForm.reasonCode = ''
  removeForm.reasonName = ''
  removeForm.eventDate = today()
  removeForm.eventTime = ''
  removeForm.remark = ''
  removeDialogVisible.value = true
}

function removeReasonChanged(code: string): void {
  const option = props.filters.memberRemoveReasons.find((item) => item.code === code)
  removeForm.reasonName = option?.name ?? ''
  if (code !== 'Death') {
    removeForm.eventTime = ''
  }
}

function confirmRemove(): void {
  const target = removeTarget.value
  if (!target) return
  if (!removeForm.reasonCode || !removeForm.reasonName) {
    ElMessage.warning('请选择减员原因')
    return
  }

  if (isDeathRemove.value && !removeForm.eventTime) {
    ElMessage.warning('减员原因为「人员死亡」时必须填写死亡确切时间')
    return
  }

  if (removals.value.some((item) => item.memberId === target.id)) {
    ElMessage.warning('该成员已在减员清单中')
    return
  }

  removals.value.push({
    memberId: target.id,
    reasonCode: removeForm.reasonCode,
    reasonName: removeForm.reasonName,
    eventDate: removeForm.eventDate || today(),
    eventTime: isDeathRemove.value ? removeForm.eventTime : null,
    remark: removeForm.remark.trim() || null,
  })
  removeDialogVisible.value = false
}

function cancelRemoval(memberId: number): void {
  removals.value = removals.value.filter((item) => item.memberId !== memberId)
}

function removalMember(memberId: number): ChangeMember | undefined {
  return props.detail.members.find((member) => member.id === memberId)
}

// ── 属性变更 ──
const editableMembers = computed(() => props.detail.members.filter((member) => !member.isSupport))

const modifyDialogVisible = ref(false)
const modifyTarget = ref<ChangeMember | null>(null)
interface ModifyFormState {
  relationshipToHead: string
  healthStatus: string
  employmentStatus: string
  workCapacity: string
  phone: string
  workUnit: string
  educationLevel: string
  maritalStatus: string
  politicalStatus: string
  reason: string
  remark: string
}

const modifyForm = reactive<ModifyFormState>({
  relationshipToHead: '',
  healthStatus: '',
  employmentStatus: '',
  workCapacity: '',
  phone: '',
  workUnit: '',
  educationLevel: '',
  maritalStatus: '',
  politicalStatus: '',
  reason: '',
  remark: '',
})

function openModifyDialog(member: ChangeMember): void {
  modifyTarget.value = member
  modifyForm.relationshipToHead = member.relationship
  modifyForm.healthStatus = member.healthStatus
  modifyForm.employmentStatus = member.employmentStatus
  modifyForm.workCapacity = member.workCapacity
  modifyForm.phone = member.phone
  modifyForm.workUnit = member.workUnit
  modifyForm.educationLevel = member.educationLevel
  modifyForm.maritalStatus = member.maritalStatus
  modifyForm.politicalStatus = member.politicalStatus
  modifyForm.reason = ''
  modifyForm.remark = ''
  modifyDialogVisible.value = true
}

function hasModifyChange(): boolean {
  const member = modifyTarget.value
  if (!member) return false
  return (
    modifyForm.relationshipToHead !== member.relationship ||
    modifyForm.healthStatus !== member.healthStatus ||
    modifyForm.employmentStatus !== member.employmentStatus ||
    modifyForm.workCapacity !== member.workCapacity ||
    modifyForm.phone.trim() !== member.phone.trim() ||
    modifyForm.workUnit.trim() !== member.workUnit.trim() ||
    modifyForm.educationLevel !== member.educationLevel ||
    modifyForm.maritalStatus !== member.maritalStatus ||
    modifyForm.politicalStatus !== member.politicalStatus
  )
}

function confirmModify(): void {
  const target = modifyTarget.value
  if (!target) return
  if (!modifyForm.reason.trim()) {
    ElMessage.warning('请填写属性变更原因')
    return
  }

  if (!hasModifyChange()) {
    ElMessage.warning('未检测到属性变化')
    return
  }

  const request: MemberModifyRequest = {
    memberId: target.id,
    relationshipToHead: modifyForm.relationshipToHead,
    healthStatus: modifyForm.healthStatus,
    employmentStatus: modifyForm.employmentStatus,
    workCapacity: modifyForm.workCapacity,
    phone: modifyForm.phone.trim(),
    workUnit: modifyForm.workUnit.trim(),
    educationLevel: modifyForm.educationLevel,
    maritalStatus: modifyForm.maritalStatus,
    politicalStatus: modifyForm.politicalStatus,
    reason: modifyForm.reason.trim(),
    remark: modifyForm.remark.trim() || null,
  }

  const index = modifications.value.findIndex((item) => item.memberId === request.memberId)
  if (index >= 0) {
    modifications.value.splice(index, 1, request)
  } else {
    modifications.value.push(request)
  }

  modifyDialogVisible.value = false
}

function cancelModification(memberId: number): void {
  modifications.value = modifications.value.filter((item) => item.memberId !== memberId)
}

function modifySummary(request: MemberModifyRequest): string {
  const member = props.detail.members.find((item) => item.id === request.memberId)
  return member?.name ?? `成员 #${request.memberId}`
}

function resetAll(): void {
  activeTab.value = 'add'
  changeReason.value = ''
  additions.value = []
  removals.value = []
  modifications.value = []
  resetAddForm()
  removeDialogVisible.value = false
  modifyDialogVisible.value = false
  result.value = null
  resultVisible.value = false
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) resetAll()
  },
)

const totalChanges = computed(
  () => additions.value.length + removals.value.length + modifications.value.length,
)

async function submit(): Promise<void> {
  if (!changeReason.value.trim()) {
    ElMessage.warning('请填写本次成员变更原因')
    return
  }

  if (totalChanges.value === 0) {
    ElMessage.warning('请至少登记一项成员变化（新增、减员或属性变更）')
    return
  }

  submitting.value = true
  try {
    const response = await submitMemberChange(props.applicationId, {
      changeReason: changeReason.value.trim(),
      additions: additions.value,
      removals: removals.value,
      modifications: modifications.value,
    })
    result.value = response
    resultVisible.value = true
    emit('success', response)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '家庭成员变更失败'))
  } finally {
    submitting.value = false
  }
}

function handleResultDone(): void {
  visible.value = false
  resetAll()
}
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`家庭成员变更 · ${detail.applicantName || ''}`"
    :size="'min(880px, 96vw)'"
    destroy-on-close
    append-to-body
  >
    <div class="member-dialog">
      <el-alert
        type="warning"
        :closable="false"
        show-icon
        title="提交后由 Core 重新认定分类并停旧建新"
        description="新增成员的年度收入默认为 0，请在生成的新档案经济信息中核定；本页金额不参与计算。"
      />

      <div class="segmented" role="tablist">
        <button
          type="button"
          class="segmented__item"
          :class="{ 'is-active': activeTab === 'add' }"
          @click="activeTab = 'add'"
        >
          新增成员
          <span v-if="additions.length > 0">{{ additions.length }}</span>
        </button>
        <button
          type="button"
          class="segmented__item"
          :class="{ 'is-active': activeTab === 'remove' }"
          @click="activeTab = 'remove'"
        >
          减少成员
          <span v-if="removals.length > 0">{{ removals.length }}</span>
        </button>
        <button
          type="button"
          class="segmented__item"
          :class="{ 'is-active': activeTab === 'modify' }"
          @click="activeTab = 'modify'"
        >
          属性变更
          <span v-if="modifications.length > 0">{{ modifications.length }}</span>
        </button>
      </div>

      <section v-if="activeTab === 'add'" class="tab-pane">
        <div class="form-grid">
            <label class="field">
              <span>姓名 *</span>
              <el-input v-model="addForm.name" maxlength="30" placeholder="新增成员姓名" />
            </label>
            <label class="field">
              <span>身份证号 *</span>
              <el-input v-model="addForm.idCard" maxlength="18" placeholder="18 位身份证号" />
            </label>
            <label class="field">
              <span>与户主关系 *</span>
              <el-select v-model="addForm.relationshipToHead" placeholder="请选择家庭关系">
                <el-option
                  v-for="option in relationshipOptions"
                  :key="option.code"
                  :label="option.name"
                  :value="option.code"
                />
              </el-select>
            </label>
            <label class="field">
              <span>性别</span>
              <el-select v-model="addForm.gender" placeholder="可留空，按身份证识别">
                <el-option
                  v-for="option in filters.genders"
                  :key="option.code"
                  :label="option.name"
                  :value="option.code"
                />
              </el-select>
            </label>
            <label class="field">
              <span>出生日期</span>
              <el-date-picker
                v-model="addForm.birthDate"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="可留空，按身份证识别"
              />
            </label>
            <label class="field">
              <span>年龄</span>
              <el-input-number v-model="addForm.age" :min="0" :max="150" controls-position="right" />
            </label>
            <label class="field">
              <span>健康状况</span>
              <el-select v-model="addForm.healthStatus" clearable placeholder="请选择">
                <el-option
                  v-for="option in filters.healthStatuses"
                  :key="option.code"
                  :label="option.name"
                  :value="option.code"
                />
              </el-select>
            </label>
            <label class="field">
              <span>联系电话</span>
              <el-input v-model="addForm.phone" maxlength="20" placeholder="选填" />
            </label>
            <label class="field">
              <span>增员原因 *</span>
              <el-select v-model="addForm.reasonCode" placeholder="请选择" @change="addReasonChanged">
                <el-option
                  v-for="option in filters.memberAddReasons"
                  :key="option.code"
                  :label="option.name"
                  :value="option.code"
                />
              </el-select>
            </label>
            <label class="field">
              <span>事由日期</span>
              <el-date-picker v-model="addForm.eventDate" type="date" value-format="YYYY-MM-DD" />
            </label>
            <label class="field field--wide">
              <span>备注</span>
              <el-input v-model="addForm.remark" maxlength="200" placeholder="选填" />
            </label>
          </div>

          <div class="queue-actions">
            <el-button type="primary" plain @click="queueAddition">加入新增清单</el-button>
          </div>

          <div v-if="additions.length > 0" class="queue">
            <div v-for="(item, index) in additions" :key="`${item.idCard}-${index}`" class="queue-item">
              <div>
                <strong>{{ item.name }}</strong>
                <span>{{ item.idCard }} · {{ item.reasonName }}</span>
              </div>
              <el-button text type="danger" @click="removeAddition(index)">移除</el-button>
            </div>
          </div>
          <el-empty v-else :image-size="48" description="尚未登记新增成员" />
      </section>

      <section v-if="activeTab === 'remove'" class="tab-pane">
          <div v-if="removableMembers.length > 0" class="person-list">
            <article v-for="member in removableMembers" :key="member.id" class="person-row">
              <div class="person-main">
                <strong>{{ member.name }}</strong>
                <span>
                  {{ member.relationshipText || member.relationship }} · {{ member.gender }} ·
                  {{ member.age ?? '—' }} 岁
                </span>
                <small>{{ member.idCardMasked }}</small>
              </div>
              <el-button size="small" type="danger" plain @click="openRemoveDialog(member)">
                减员登记
              </el-button>
            </article>
          </div>
          <el-empty v-else :image-size="52" description="除户主外无可减员成员" />

          <div v-if="removals.length > 0" class="queue">
            <div v-for="item in removals" :key="item.memberId" class="queue-item">
              <div>
                <strong>{{ removalMember(item.memberId)?.name || `成员 #${item.memberId}` }}</strong>
                <span>
                  {{ item.reasonName }} · {{ item.eventDate }}
                  <template v-if="item.eventTime"> {{ item.eventTime }}</template>
                </span>
              </div>
              <el-button text type="danger" @click="cancelRemoval(item.memberId)">移除</el-button>
            </div>
          </div>
      </section>

      <section v-if="activeTab === 'modify'" class="tab-pane">
          <div v-if="editableMembers.length > 0" class="person-list">
            <article v-for="member in editableMembers" :key="member.id" class="person-row">
              <div class="person-main">
                <strong>{{ member.name }}</strong>
                <span>
                  {{ member.relationshipText || member.relationship }} ·
                  {{ member.healthStatusText || member.healthStatus || '健康状况未填' }}
                </span>
                <small>{{ member.idCardMasked }}</small>
              </div>
              <el-button size="small" plain @click="openModifyDialog(member)">编辑属性</el-button>
            </article>
          </div>
          <el-empty v-else :image-size="52" description="暂无可修改成员" />

          <div v-if="modifications.length > 0" class="queue">
            <div v-for="item in modifications" :key="item.memberId" class="queue-item">
              <div>
                <strong>{{ modifySummary(item) }}</strong>
                <span>{{ item.reason }}</span>
              </div>
              <el-button text type="danger" @click="cancelModification(item.memberId)">移除</el-button>
            </div>
          </div>
      </section>

      <section class="reason-block">
        <h3>本次变更原因 *</h3>
        <el-input
          v-model="changeReason"
          type="textarea"
          :rows="3"
          maxlength="500"
          show-word-limit
          placeholder="请说明本次家庭成员变化的整体原因（逐人原因已在各清单登记）"
        />
      </section>

      <p class="summary-line">
        待提交：新增 {{ additions.length }} 人 · 减员 {{ removals.length }} 人 · 属性变更
        {{ modifications.length }} 人
      </p>
    </div>

    <template #footer>
      <div class="footer">
        <el-button @click="visible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submit">提交成员变更</el-button>
      </div>
    </template>

    <el-dialog v-model="removeDialogVisible" title="减员登记" width="min(520px, 94vw)" append-to-body>
      <div class="dialog-form">
        <p class="dialog-member">
          <strong>{{ removeTarget?.name }}</strong>
          <span>{{ removeTarget?.idCardMasked }}</span>
        </p>
        <label class="field">
          <span>减员原因 *</span>
          <el-select v-model="removeForm.reasonCode" placeholder="请选择" @change="removeReasonChanged">
            <el-option
              v-for="option in filters.memberRemoveReasons"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label v-if="isDeathRemove" class="field">
          <span>死亡确切时间 *</span>
          <el-time-picker
            v-model="removeForm.eventTime"
            value-format="HH:mm"
            format="HH:mm"
            placeholder="死亡确切时间"
          />
        </label>
        <label class="field">
          <span>{{ isDeathRemove ? '死亡日期' : '事由日期' }}</span>
          <el-date-picker v-model="removeForm.eventDate" type="date" value-format="YYYY-MM-DD" />
        </label>
        <label class="field">
          <span>备注</span>
          <el-input v-model="removeForm.remark" maxlength="200" placeholder="选填" />
        </label>
      </div>
      <template #footer>
        <el-button @click="removeDialogVisible = false">取消</el-button>
        <el-button type="danger" @click="confirmRemove">加入减员清单</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="modifyDialogVisible" title="属性变更" width="min(620px, 94vw)" append-to-body>
      <div class="dialog-form form-grid">
        <p class="dialog-member field--wide">
          <strong>{{ modifyTarget?.name }}</strong>
          <span>{{ modifyTarget?.idCardMasked }}</span>
        </p>
        <label class="field">
          <span>与户主关系</span>
          <el-select v-model="modifyForm.relationshipToHead" :disabled="modifyTarget?.isHouseholdHead">
            <el-option
              v-for="option in filters.relationships"
              :key="option.code"
              :label="option.name"
              :value="option.code"
              :disabled="option.code === 'Head' && !modifyTarget?.isHouseholdHead"
            />
          </el-select>
        </label>
        <label class="field">
          <span>健康状况</span>
          <el-select v-model="modifyForm.healthStatus" clearable>
            <el-option
              v-for="option in filters.healthStatuses"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field">
          <span>就业状况</span>
          <el-select v-model="modifyForm.employmentStatus" clearable>
            <el-option
              v-for="option in filters.employmentStatuses"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field">
          <span>劳动能力</span>
          <el-select v-model="modifyForm.workCapacity" clearable>
            <el-option
              v-for="option in filters.laborAbilities"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field">
          <span>联系电话</span>
          <el-input v-model="modifyForm.phone" maxlength="20" />
        </label>
        <label class="field">
          <span>工作单位</span>
          <el-input v-model="modifyForm.workUnit" maxlength="100" />
        </label>
        <label class="field">
          <span>文化程度</span>
          <el-select v-model="modifyForm.educationLevel" clearable>
            <el-option
              v-for="option in filters.educationLevels"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field">
          <span>婚姻状况</span>
          <el-select v-model="modifyForm.maritalStatus" clearable>
            <el-option
              v-for="option in filters.maritalStatuses"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field">
          <span>政治面貌</span>
          <el-select v-model="modifyForm.politicalStatus" clearable>
            <el-option
              v-for="option in filters.politicalStatuses"
              :key="option.code"
              :label="option.name"
              :value="option.code"
            />
          </el-select>
        </label>
        <label class="field field--wide">
          <span>变更原因 *</span>
          <el-input v-model="modifyForm.reason" maxlength="200" placeholder="如：健康状况变化、联系方式更正" />
        </label>
        <label class="field field--wide">
          <span>备注</span>
          <el-input v-model="modifyForm.remark" maxlength="200" placeholder="选填" />
        </label>
      </div>
      <template #footer>
        <el-button @click="modifyDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmModify">加入变更清单</el-button>
      </template>
    </el-dialog>

    <ChangeResultDialog
      v-model="resultVisible"
      :result="result"
      title="家庭成员变更结果"
      @done="handleResultDone"
    />
  </el-drawer>
</template>

<style scoped>
.member-dialog {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.tabs {
  min-height: 320px;
}

.segmented {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 4px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
}

.segmented__item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  border: 1px solid transparent;
  background: none;
  color: var(--nc-muted);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  cursor: pointer;
}

.segmented__item span {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  border-radius: 9px;
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: 10px;
}

.segmented__item.is-active {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink);
  box-shadow: var(--nc-shadow-xs);
}

.tab-pane {
  min-height: 300px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.field > span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.field--wide {
  grid-column: 1 / -1;
}

.field :deep(.el-date-editor),
.field :deep(.el-select),
.field :deep(.el-input-number) {
  width: 100%;
}

.queue-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}

.queue {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line);
}

.queue-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.queue-item > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.queue-item strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.queue-item span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.person-list {
  display: flex;
  flex-direction: column;
  gap: 7px;
}

.person-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.person-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.person-main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.person-main span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.person-main small {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.reason-block {
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.reason-block h3 {
  margin-bottom: 7px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.summary-line {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.dialog-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.dialog-member {
  display: flex;
  align-items: baseline;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.dialog-member strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
}

.dialog-member span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

@media (max-width: 700px) {
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
