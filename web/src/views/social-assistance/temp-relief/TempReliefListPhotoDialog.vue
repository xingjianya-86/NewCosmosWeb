<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { getTempReliefApplication } from '@/api/tempRelief'
import TempReliefPhotoPanel from './TempReliefPhotoPanel.vue'

/**
 * 列表页「照片」入口（对齐 PC TempReliefListViewModel.OpenPhotosAsync）。
 *
 * PC 列表不携带身份证明文，只带申请 id；这里先读详情拿到 applicant_id_card，
 * 确认该记录确有身份证后再打开照片面板（面板接口由服务端按 applicant_id_card
 * 聚合 nc_biz_archive_photos，临时救助主表 id 与低保申请 id 不同源）。
 * 详情拿不到身份证时明确提示，不拿脱敏证件或申请编号臆造查询条件。
 */
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    applicationId: number | null
    applicantName?: string
    /** 列表入口默认只读；与 PC「档案照片查询」一致时可放开上传/删除。 */
    editable?: boolean
  }>(),
  {
    applicantName: '',
    editable: false,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
}>()

const open = ref(false)
const subjectName = ref('')

let requestTicket = 0

watch(
  () => props.modelValue,
  async (visible) => {
    if (!visible) {
      requestTicket += 1
      open.value = false
      return
    }

    const id = props.applicationId
    if (id === null) {
      ElMessage.warning('该记录缺少申请编号，无法查看照片')
      emit('update:modelValue', false)
      return
    }

    const ticket = ++requestTicket
    open.value = false
    try {
      const detail = await getTempReliefApplication(id)
      if (ticket !== requestTicket) return

      const idCard = (detail.applicantIdCard ?? '').trim()
      if (!idCard) {
        ElMessage.warning('该记录缺少身份证号，无法关联照片')
        emit('update:modelValue', false)
        return
      }

      subjectName.value = detail.applicantName || props.applicantName
      open.value = true
    } catch (error) {
      if (ticket !== requestTicket) return
      ElMessage.error(getErrorMessage(error, '读取申请人身份证失败'))
      emit('update:modelValue', false)
    }
  },
  { immediate: true },
)

watch(open, (value) => {
  if (!value && props.modelValue) {
    emit('update:modelValue', false)
  }
})
</script>

<template>
  <TempReliefPhotoPanel
    v-model="open"
    :application-id="applicationId"
    :applicant-name="subjectName || applicantName"
    :editable="editable"
  />
</template>
