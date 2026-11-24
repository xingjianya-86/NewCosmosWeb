<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { Lock, User } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { APP_NAME, APP_VERSION } from '@/config/app'
import { useAuthStore } from '@/stores/auth'

const LAST_USERNAME_KEY = 'nc.web.lastUsername'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const formRef = ref<FormInstance>()

function readLastUsername(): string {
  try {
    return localStorage.getItem(LAST_USERNAME_KEY) ?? ''
  } catch {
    return ''
  }
}

const form = reactive({
  username: readLastUsername(),
  password: '',
})
const remember = ref(Boolean(form.username))
const loading = ref(false)
const errorMessage = ref('')
const year = new Date().getFullYear()

const rules: FormRules<typeof form> = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }],
}

function resolveRedirect(): string {
  const raw = route.query.redirect
  const value = Array.isArray(raw) ? raw[0] : raw
  if (typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && value !== '/login') {
    return value
  }
  return '/'
}

async function submit(): Promise<void> {
  if (loading.value) return

  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  loading.value = true
  errorMessage.value = ''

  try {
    await auth.login(form.username.trim(), form.password)

    try {
      if (remember.value) localStorage.setItem(LAST_USERNAME_KEY, form.username.trim())
      else localStorage.removeItem(LAST_USERNAME_KEY)
    } catch {
      // 忽略存储异常
    }

    await router.replace(resolveRedirect())
  } catch (error) {
    errorMessage.value = getErrorMessage(error, '登录失败，请稍后重试')
    form.password = ''
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <section class="login-hero">
      <div class="hero-inner">
        <div class="hero-badge">NewCosmos · Web</div>
        <h1 class="hero-title">新宇宙<br />民政服务平台</h1>
        <p class="hero-desc">社会救助业务一体化协同，数据在政务网络内安全流转。</p>

        <ul class="hero-points">
          <li><span class="point-dot" />统一身份认证，会话按设备严格管控</li>
          <li><span class="point-dot" />打印机按账户绑定，手机入队、电脑执行</li>
          <li><span class="point-dot" />桌面与手机自适应，无需安装客户端</li>
        </ul>

        <div class="hero-foot">
          <span class="hero-line" />
          <span>v{{ APP_VERSION }} · 政务内网专用</span>
        </div>
      </div>
    </section>

    <section class="login-panel">
      <div class="mobile-brand">
        <span class="mobile-mark">新</span>
        <div>
          <strong>新宇宙民政</strong>
          <span>政务服务平台</span>
        </div>
      </div>

      <div class="login-card">
        <header class="login-head">
          <h2>账号登录</h2>
          <p>请使用政务账号登录系统</p>
        </header>

        <el-alert
          v-if="errorMessage"
          class="login-alert"
          :title="errorMessage"
          type="error"
          :closable="false"
          show-icon
        />

        <el-form
          ref="formRef"
          :model="form"
          :rules="rules"
          label-position="top"
          size="large"
          @submit.prevent="submit"
        >
          <el-form-item label="用户名" prop="username">
            <el-input
              v-model="form.username"
              :prefix-icon="User"
              placeholder="请输入用户名"
              autocomplete="username"
              clearable
            />
          </el-form-item>

          <el-form-item label="密码" prop="password">
            <el-input
              v-model="form.password"
              type="password"
              :prefix-icon="Lock"
              placeholder="请输入密码"
              autocomplete="current-password"
              show-password
              @keyup.enter="submit"
            />
          </el-form-item>

          <div class="login-options">
            <el-checkbox v-model="remember">记住账号</el-checkbox>
            <span class="login-hint">忘记密码请联系管理员</span>
          </div>

          <el-button type="primary" native-type="submit" :loading="loading" class="login-submit">
            登 录
          </el-button>
        </el-form>

        <p class="login-tip">登录即表示同意按政务数据安全要求使用本系统。</p>
      </div>

      <footer class="login-footer">© {{ year }} {{ APP_NAME }}</footer>
    </section>
  </div>
</template>

<style scoped>
.login-page {
  display: grid;
  grid-template-columns: minmax(420px, 1.05fr) minmax(420px, 0.95fr);
  min-height: 100vh;
  background: #fff;
}

.login-hero {
  position: relative;
  display: flex;
  align-items: center;
  overflow: hidden;
  padding: 64px clamp(40px, 6vw, 88px);
  background:
    radial-gradient(900px 480px at 12% 8%, rgba(84, 154, 214, 0.4), transparent 60%),
    radial-gradient(700px 500px at 88% 92%, rgba(24, 92, 145, 0.55), transparent 62%),
    linear-gradient(155deg, #10395d 0%, #175487 52%, #1c5f9e 100%);
  color: #fff;
}

.login-hero::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.045) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.045) 1px, transparent 1px);
  background-size: 56px 56px;
  mask-image: linear-gradient(120deg, rgba(0, 0, 0, 0.7), transparent 72%);
  pointer-events: none;
}

.login-hero::after {
  content: '';
  position: absolute;
  right: -120px;
  bottom: -160px;
  width: 420px;
  height: 420px;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 50%;
  box-shadow: 0 0 0 60px rgba(255, 255, 255, 0.03), 0 0 0 120px rgba(255, 255, 255, 0.02);
  pointer-events: none;
}

.hero-inner {
  position: relative;
  z-index: 1;
  max-width: 480px;
}

.hero-badge {
  display: inline-flex;
  align-items: center;
  height: 28px;
  padding: 0 12px;
  border: 1px solid rgba(255, 255, 255, 0.28);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  font-size: 12px;
  letter-spacing: 1.2px;
}

.hero-title {
  margin-top: 26px;
  font-size: clamp(32px, 3.2vw, 46px);
  font-weight: 700;
  line-height: 1.24;
  letter-spacing: 1px;
}

.hero-desc {
  margin-top: 16px;
  max-width: 380px;
  color: rgba(255, 255, 255, 0.78);
  font-size: 14px;
  line-height: 1.8;
}

.hero-points {
  margin: 34px 0 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 13px;
}

.hero-points li {
  display: flex;
  align-items: center;
  gap: 10px;
  color: rgba(255, 255, 255, 0.86);
  font-size: 13px;
}

.point-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #8ec5f0;
  box-shadow: 0 0 0 4px rgba(142, 197, 240, 0.16);
}

.hero-foot {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 52px;
  color: rgba(255, 255, 255, 0.5);
  font-size: 11px;
  letter-spacing: 0.6px;
}

.hero-line {
  width: 32px;
  height: 1px;
  background: rgba(255, 255, 255, 0.35);
}

.login-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 24px 28px;
  background: #fff;
}

.mobile-brand {
  display: none;
}

.login-card {
  width: 100%;
  max-width: 380px;
}

.login-head h2 {
  font-size: 26px;
  font-weight: 600;
  letter-spacing: 0.5px;
}

.login-head p {
  margin-top: 8px;
  color: var(--nc-text-secondary);
  font-size: 13px;
}

.login-alert {
  margin: 20px 0 4px;
}

.login-card :deep(.el-form) {
  margin-top: 24px;
}

.login-card :deep(.el-form-item) {
  margin-bottom: 20px;
}

.login-card :deep(.el-form-item__label) {
  padding-bottom: 6px;
  font-size: 13px;
  color: var(--nc-text-secondary);
}

.login-card :deep(.el-input__wrapper) {
  padding: 2px 14px;
  border-radius: 8px;
}

.login-options {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin: 4px 0 22px;
}

.login-hint {
  color: var(--nc-text-muted);
  font-size: 12px;
}

.login-submit {
  width: 100%;
  height: 44px;
  font-size: 15px;
  letter-spacing: 3px;
}

.login-tip {
  margin-top: 22px;
  color: var(--nc-text-muted);
  font-size: 11px;
  line-height: 1.7;
}

.login-footer {
  margin-top: 48px;
  color: var(--nc-text-muted);
  font-size: 11px;
}

@media (max-width: 900px) {
  .login-page {
    grid-template-columns: 1fr;
    background: var(--nc-bg);
  }

  .login-hero {
    display: none;
  }

  .login-panel {
    justify-content: flex-start;
    min-height: 100vh;
    padding: 40px 20px calc(28px + env(safe-area-inset-bottom));
    background: var(--nc-bg);
  }

  .mobile-brand {
    display: flex;
    align-items: center;
    gap: 11px;
    width: 100%;
    max-width: 380px;
    margin-bottom: 26px;
  }

  .mobile-mark {
    display: grid;
    place-items: center;
    width: 40px;
    height: 40px;
    border-radius: 10px;
    background: linear-gradient(150deg, #2a76ba, #164c7e);
    color: #fff;
    font-size: 18px;
    font-weight: 700;
  }

  .mobile-brand div {
    display: flex;
    flex-direction: column;
    line-height: 1.35;
  }

  .mobile-brand strong {
    font-size: 15px;
  }

  .mobile-brand span:last-child {
    font-size: 11px;
    letter-spacing: 1.4px;
    color: var(--nc-text-muted);
  }

  .login-card {
    padding: 22px 18px 20px;
    background: #fff;
    border: 1px solid var(--nc-border);
    border-radius: 14px;
    box-shadow: var(--nc-shadow);
  }

  .login-head h2 {
    font-size: 21px;
  }

  .login-card :deep(.el-form) {
    margin-top: 18px;
  }

  .login-footer {
    margin-top: 32px;
  }
}
</style>
