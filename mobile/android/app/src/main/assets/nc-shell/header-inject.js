/*
 * NewCosmos Android 壳注入脚本（不修改 Web 源码）。
 *
 * 1. 同源请求补 X-NC-Client / X-NC-App-Version。仅限同源，避免给跨域业务 API
 *    （cosmos-static.sua17.cn:20000）触发 CORS 预检；跨域请求的客户端标识由原生
 *    User-Agent 追加 "NewCosmosAndroid/<version>" 承担。
 * 2. blob: 下载转交原生保存（WebView 本身不能把 blob 交给 DownloadManager）。
 * 3. 暴露 window.NcShell，便于后续 Web 侧按需调用（当前页面无需改动）。
 */
(function () {
  'use strict';

  if (window.__ncShellInstalled) return;
  window.__ncShellInstalled = true;

  var CLIENT = 'android';
  var VERSION = '__NC_VERSION__';

  function nativeApi() {
    try {
      return window.NcShellNative || null;
    } catch (e) {
      return null;
    }
  }

  function sameOrigin(url) {
    if (!url) return false;
    try {
      var resolved = new URL(url, window.location.href);
      return resolved.origin === window.location.origin && /^https?:$/.test(resolved.protocol);
    } catch (e) {
      return false;
    }
  }

  function applyHeaders(headers) {
    try {
      headers.set('X-NC-Client', CLIENT);
      headers.set('X-NC-App-Version', VERSION);
    } catch (e) {
      /* 部分请求头被冻结时忽略 */
    }
  }

  var nativeFetch = typeof window.fetch === 'function' ? window.fetch.bind(window) : null;

  if (nativeFetch) {
    window.fetch = function (input, init) {
      try {
        var url = typeof input === 'string' ? input : (input && (input.url || input.href)) || '';
        if (sameOrigin(url)) {
          var next = init ? Object.assign({}, init) : {};
          var headers = new Headers(next.headers || (input && input.headers) || undefined);
          applyHeaders(headers);
          next.headers = headers;
          init = next;
        }
      } catch (e) {
        /* 保持原请求 */
      }
      return nativeFetch(input, init);
    };
  }

  var xhrOpen = XMLHttpRequest.prototype.open;
  var xhrSend = XMLHttpRequest.prototype.send;

  XMLHttpRequest.prototype.open = function (method, url) {
    try {
      this.__ncShellUrl = url;
    } catch (e) {
      /* ignore */
    }
    return xhrOpen.apply(this, arguments);
  };

  XMLHttpRequest.prototype.send = function () {
    try {
      if (sameOrigin(this.__ncShellUrl)) {
        this.setRequestHeader('X-NC-Client', CLIENT);
        this.setRequestHeader('X-NC-App-Version', VERSION);
      }
    } catch (e) {
      /* ignore */
    }
    return xhrSend.apply(this, arguments);
  };

  function toBase64(bytes) {
    var binary = '';
    var step = 0x8000;
    for (var i = 0; i < bytes.length; i += step) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + step));
    }
    return window.btoa(binary);
  }

  function saveBlob(url, name) {
    var api = nativeApi();
    if (!api || !nativeFetch) return false;

    nativeFetch(url)
      .then(function (response) {
        return response.blob();
      })
      .then(function (blob) {
        var mime = blob.type || 'application/octet-stream';
        var fallback = 'download-' + Date.now();
        var id = api.beginBlob(name || fallback, mime);
        if (!id) return;
        blob.arrayBuffer().then(function (buffer) {
          var bytes = new Uint8Array(buffer);
          var chunk = 384 * 1024;
          try {
            for (var i = 0; i < bytes.length; i += chunk) {
              api.appendBlob(id, toBase64(bytes.subarray(i, i + chunk)));
            }
            api.finishBlob(id);
          } catch (e) {
            try {
              api.abortBlob(id);
            } catch (e2) {
              /* ignore */
            }
          }
        });
      })
      .catch(function () {
        /* 原生侧无文件可提示；页面本身也会显示下载错误 */
      });

    return true;
  }

  // DownloadListener 收到 blob:/data: 时由原生回调用到。
  window.__ncShellSaveUrl = function (url, name) {
    return saveBlob(url, name || '');
  };

  function isBlobDownload(anchor) {
    return !!anchor && typeof anchor.href === 'string' && anchor.href.indexOf('blob:') === 0;
  }

  var anchorClick = HTMLAnchorElement.prototype.click;

  HTMLAnchorElement.prototype.click = function () {
    try {
      if (isBlobDownload(this) && saveBlob(this.href, this.getAttribute('download') || '')) {
        return;
      }
    } catch (e) {
      /* 回退到浏览器默认行为 */
    }
    return anchorClick.apply(this, arguments);
  };

  // 真实手指点击（非 element.click()）同样接管 blob 下载。
  document.addEventListener(
    'click',
    function (event) {
      try {
        var target = event.target;
        var anchor = target && target.closest ? target.closest('a[download]') : null;
        if (anchor && isBlobDownload(anchor) && saveBlob(anchor.href, anchor.getAttribute('download') || '')) {
          event.preventDefault();
          event.stopPropagation();
        }
      } catch (e) {
        /* ignore */
      }
    },
    true
  );

  // 页面里的 window.open（首页外链卡片等）直接交给系统浏览器；
  // 同源 window.open 仍走原生 WebChromeClient（可继续在壳内打开）。
  var nativeOpen = window.open;
  if (typeof nativeOpen === 'function') {
    window.open = function (url, target, features) {
      try {
        var resolved = new URL(String(url), window.location.href);
        if (/^https?:$/.test(resolved.protocol) && resolved.origin !== window.location.origin) {
          var api = nativeApi();
          if (api) {
            api.openExternal(resolved.href);
            return null;
          }
        }
      } catch (e) {
        /* 回退到默认行为 */
      }
      return nativeOpen.apply(this, arguments);
    };
  }

  window.NcShell = window.NcShell || {};
  window.NcShell.client = CLIENT;
  window.NcShell.version = VERSION;
  window.NcShell.checkUpdate = function () {
    try {
      var api = nativeApi();
      if (api) api.checkUpdate();
    } catch (e) {
      /* ignore */
    }
  };
  window.NcShell.openExternal = function (url) {
    try {
      var api = nativeApi();
      if (api) api.openExternal(String(url));
    } catch (e) {
      /* ignore */
    }
  };
})();
