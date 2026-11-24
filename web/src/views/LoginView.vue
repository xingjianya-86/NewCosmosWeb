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
        <p class="hero-kicker">NewCosmos · Archive</p>
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

      <div class="hero-stamp" aria-hidden="true">
        <span>档案</span>
        <span>专用</span>
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
          <p class="login-kicker">Account · Sign in</p>
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
  background-color: var(--nc-paper);
}

/* ── 左侧：档案封面 ───────────────────────────────────────────── */
.login-hero {
  position: relative;
  display: flex;
  align-items: center;
  overflow: hidden;
  padding: 64px clamp(36px, 5vw, 80px) 64px clamp(56px, 6vw, 96px);
  background:
    repeating-linear-gradient(
      0deg,
      transparent 0 34px,
      rgba(23, 19, 16, 0.045) 34px 35px
    ),
    var(--nc-paper-3);
  border-right: 1px solid var(--nc-line-strong);
}

.login-hero::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: clamp(30px, 3.6vw, 58px);
  width: 1px;
  background-color: rgba(171, 50, 39, 0.25);
  pointer-events: none;
}

.login-hero::after {
  content: '档';
  position: absolute;
  top: -18px;
  right: clamp(12px, 4vw, 48px);
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: clamp(160px, 18vw, 240px);
  font-weight: 700;
  line-height: 1;
  opacity: 0.035;
  pointer-events: none;
}

.hero-inner {
  position: relative;
  z-index: 1;
  max-width: 480px;
  padding-left: clamp(20px, 2vw, 30px);
  border-left: 1px solid rgba(171, 50, 39, 0.32);
}

.hero-kicker {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.22em;
  text-transform: uppercase;
}

.hero-kicker::before {
  content: '';
  flex: none;
  width: 7px;
  height: 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.hero-title {
  margin-top: 22px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: clamp(34px, 3.4vw, 48px);
  font-weight: 700;
  line-height: 1.28;
  letter-spacing: 0.07em;
}

.hero-desc {
  margin-top: 18px;
  max-width: 380px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
  line-height: 1.9;
}

.hero-points {
  display: grid;
  gap: 13px;
  margin: 34px 0 0;
  padding: 0;
  list-style: none;
}

.hero-points li {
  display: flex;
  align-items: flex-start;
  gap: 11px;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
}

.point-dot {
  flex: none;
  width: 7px;
  height: 7px;
  margin-top: 8px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
  opacity: 0.85;
}

.hero-foot {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 52px;
  padding-top: 16px;
  border-top: 1px solid var(--nc-line);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.08em;
}

.hero-line {
  width: 30px;
  height: 1px;
  background-color: var(--nc-seal);
  opacity: 0.6;
}

.hero-stamp {
  position: absolute;
  right: clamp(30px, 5vw, 70px);
  bottom: clamp(40px, 7vh, 82px);
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  width: 88px;
  height: 88px;
  padding: 6px;
  border: 2px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  box-shadow: inset 0 0 0 2px rgba(171, 50, 39, 0.12);
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 19px;
  font-weight: 700;
  line-height: 1.15;
  letter-spacing: 0.16em;
  text-align: center;
  opacity: 0.82;
  transform: rotate(-5deg);
  pointer-events: none;
}

/* ── 右侧：登录卡 ─────────────────────────────────────────────── */
.login-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 28px 28px;
  background-color: var(--nc-paper);
}

.mobile-brand {
  display: none;
}

.login-card {
  width: 100%;
  max-width: 388px;
  padding: 30px 30px 28px;
  border: 1px solid var(--nc-line);
  border-top: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-sm);
}

.login-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.22em;
  text-transform: uppercase;
}

.login-head h2 {
  margin-top: 6px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  letter-spacing: 0.06em;
}

.login-head p {
  margin-top: 8px;
  color: var(--nc-muted-2);
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
  color: var(--nc-ink-2);
  font-size: 13px;
}

.login-card :deep(.el-input__wrapper) {
  padding: 2px 14px;
  border-radius: var(--nc-radius-sm);
}

.login-options {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin: 4px 0 22px;
}

.login-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.login-submit {
  width: 100%;
  height: 46px;
  font-size: 15px;
  letter-spacing: 0.32em;
}

.login-tip {
  margin-top: 22px;
  padding-top: 16px;
  border-top: 1px dashed var(--nc-line);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.login-footer {
  margin-top: 44px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.04em;
}

/* ── 手机 ─────────────────────────────────────────────────────── */
@media (max-width: 900px) {
  .login-page {
    grid-template-columns: 1fr;
    background-color: var(--nc-paper);
  }

  .login-hero {
    display: none;
  }

  .login-panel {
    justify-content: flex-start;
    min-height: 100vh;
    padding: 36px 18px calc(28px + env(safe-area-inset-bottom));
    background:
      repeating-linear-gradient(
        0deg,
        transparent 0 34px,
        rgba(23, 19, 16, 0.03) 34px 35px
      ),
      var(--nc-paper);
  }

  .mobile-brand {
    display: flex;
    align-items: center;
    gap: 12px;
    width: 100%;
    max-width: 388px;
    margin-bottom: 24px;
    padding-left: 13px;
    border-left: 3px solid var(--nc-seal);
  }

  .mobile-mark {
    display: grid;
    flex: none;
    place-items: center;
    width: 42px;
    height: 42px;
    border: 1px solid rgba(242, 236, 225, 0.34);
    border-radius: var(--nc-radius-sm);
    background-color: var(--nc-seal);
    box-shadow: inset 0 0 0 2px rgba(23, 19, 16, 0.16);
    color: var(--nc-paper-2);
    font-family: var(--nc-font-serif);
    font-size: 20px;
    font-weight: 700;
  }

  .mobile-brand div {
    display: flex;
    flex-direction: column;
    line-height: 1.4;
  }

  .mobile-brand strong {
    color: var(--nc-ink);
    font-family: var(--nc-font-serif);
    font-size: 16px;
    letter-spacing: 0.08em;
  }

  .mobile-brand span:last-child {
    color: var(--nc-muted);
    font-size: 10px;
    letter-spacing: 0.2em;
  }

  .login-card {
    max-width: 388px;
    padding: 22px 18px 20px;
  }

  .login-head h2 {
    font-size: 22px;
  }

  .login-card :deep(.el-form) {
    margin-top: 18px;
  }

  .login-footer {
    margin-top: 30px;
  }
}
</style>
